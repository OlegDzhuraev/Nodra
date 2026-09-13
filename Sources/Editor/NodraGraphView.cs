/*
 * Nodra
 * Copyright (C) 2026 Oleg Dzhuraev <godlikeaurora@gmail.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nodra
{
	/// <summary> The canvas that draws and edits one ProceduralMeshGenerator's GeoGraph - nodes and edges are
	/// rebuilt from the component's serialized state on bind/undo, and every edit (add/remove/connect/move node)
	/// writes straight back into that same GeoGraph instance via Undo.RecordObject, so it behaves like any other
	/// Inspector edit (Undo, prefab overrides, multi-scene). Beyond syncing that data, most edits are left for
	/// GraphView's own default behaviour to apply visually (adding the new edge, removing a deleted node's
	/// elements, ...) - forcing a full re-population there too would fight GraphView's own bookkeeping. </summary>
	public class NodraGraphView : GraphView, IEdgeConnectorListener
	{
		readonly Dictionary<string, NodraNodeView> viewsById = new ();

		ProceduralMeshGenerator generator;
		SerializedObject serializedObject;
		Action onGraphChanged;

		// Set for the duration of Populate() - rebuilding pulls every node back into its GeoGroup's visual Group
		// via Group.AddElement, same as a user dragging one in would, which would otherwise fire the
		// elementsAddedToGroup/elementsRemovedFromGroup callbacks below and write the (already-current) data back
		// with their own spurious Undo step.
		bool populating;

		public NodraGraphView()
		{
			SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

			this.AddManipulator(new ContentDragger());
			this.AddManipulator(new SelectionDragger());
			this.AddManipulator(new RectangleSelector());
			this.AddManipulator(new ContentZoomer());

			// Safety-net fill in case the stylesheet below fails to load - GridBackground's own fill (from
			// --grid-background-color) should normally cover the whole canvas on top of this.
			style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);

			// GridBackground reads its line/fill colors from USS custom properties and otherwise falls back to
			// Unity's own (fairly subtle, easy to miss) defaults - Resources/NodraGraphView.uss defines them
			// explicitly, targeted at the name assigned to the grid below so it applies regardless of Unity version.
			var stylesheet = Resources.Load<StyleSheet>("NodraGraphView");
			if (stylesheet != null)
				styleSheets.Add(stylesheet);

			var grid = new GridBackground { name = "nodra-grid-background" };
			Insert(0, grid);
			grid.StretchToParentSize();

			graphViewChanged = OnGraphViewChanged;
			groupTitleChanged = OnGroupTitleChanged;
			elementsAddedToGroup = OnElementsAddedToGroup;
			elementsRemovedFromGroup = OnElementsRemovedFromGroup;

			serializeGraphElements = SerializeSelection;
			canPasteSerializedData = data => !string.IsNullOrEmpty(data);
			unserializeAndPaste = UnserializeAndPaste;
		}

		public void Bind(ProceduralMeshGenerator target, Action onChanged)
		{
			generator = target;
			serializedObject = target != null ? new SerializedObject(target) : null;
			onGraphChanged = onChanged;

			Populate();
		}

		public void Populate()
		{
			populating = true;

			try
			{
				PopulateCore();
			}
			finally
			{
				populating = false;
			}
		}

		void PopulateCore()
		{
			// Plain RemoveElement, not DeleteElements - the latter raises graphViewChanged, which would read this
			// purely-visual clear as "the user deleted everything" and wipe the underlying GeoGraph.
			foreach (var element in graphElements.ToList())
				RemoveElement(element);

			viewsById.Clear();

			if (generator == null || generator.Graph == null)
				return;

			serializedObject.Update();

			var graphProperty = serializedObject.FindProperty(nameof(ProceduralMeshGenerator.Graph));
			var nodesProperty = graphProperty.FindPropertyRelative(nameof(GeoGraph.Nodes));

			for (var i = 0; i < generator.Graph.Nodes.Count; i++)
			{
				var node = generator.Graph.Nodes[i];
				if (node == null)
					continue;

				var view = new NodraNodeView(node, nodesProperty.GetArrayElementAtIndex(i), this);
				viewsById[node.Id] = view;
				AddElement(view);
			}

			foreach (var edge in generator.Graph.Edges)
			{
				if (!viewsById.TryGetValue(edge.FromNodeId, out var from) || !viewsById.TryGetValue(edge.ToNodeId, out var to))
					continue;

				if (edge.ToPortIndex < 0 || edge.ToPortIndex >= to.InputPorts.Length)
					continue;

				AddElement(from.OutputPort.ConnectTo(to.InputPorts[edge.ToPortIndex]));
			}

			// Each NodraNodeView calls RefreshPorts() once in its own constructor, before any of the edges above
			// exist yet - so the very first time a port gets connected, its connector dot can be left showing
			// "disconnected" until something else happens to refresh it. Doing it again now, after every edge for
			// every node is in place, keeps that visual in sync with the connections actually just made.
			foreach (var view in viewsById.Values)
				view.RefreshPorts();

			// Groups are plain UnityEditor.Experimental.GraphView.Group instances, same as GroupSelection() below
			// creates - each one added empty, then given back its members via Group.AddElement so it immediately
			// resizes/repositions itself around them, rather than trying to keep a separately-stored rect in sync.
			foreach (var geoGroup in generator.Graph.Groups)
			{
				var groupView = new Group { title = geoGroup.Title, userData = geoGroup };
				AddElement(groupView);

				foreach (var nodeId in geoGroup.NodeIds)
					if (viewsById.TryGetValue(nodeId, out var view))
						groupView.AddElement(view);
			}

			RefreshOutputHighlight();
		}

		/// <summary> True if `node` is explicitly pinned as the graph's output (GeoGraph.OutputNodeId) - as opposed
		/// to merely being the one FindDefaultOutput would currently pick. Drives the node's context menu wording. </summary>
		public bool IsExplicitOutput(GeoNode node) => generator?.Graph != null && generator.Graph.OutputNodeId == node.Id;

		public void SetOutputNode(GeoNode node)
		{
			Undo.RecordObject(generator, "Set Output Node");
			generator.Graph.OutputNodeId = node.Id;
			CommitOutputChange();
		}

		public void ClearOutputNode()
		{
			Undo.RecordObject(generator, "Clear Output Node");
			generator.Graph.OutputNodeId = null;
			CommitOutputChange();
		}

		void CommitOutputChange()
		{
			EditorUtility.SetDirty(generator);
			serializedObject.Update();
			RefreshOutputHighlight();
			onGraphChanged?.Invoke();
		}

		// Highlights whichever node GeoGraph.GetOutputNode() currently resolves to - not just an explicit
		// OutputNodeId, since most graphs never set one and rely entirely on the "last dangling node" default.
		void RefreshOutputHighlight()
		{
			var outputId = generator?.Graph?.GetOutputNode()?.Id;

			foreach (var view in viewsById.Values)
				view.SetIsOutput(view.Node.Id == outputId);
		}

		public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			if (generator == null)
				return;

			var position = contentViewContainer.WorldToLocal(evt.mousePosition);

			// Generators (InputCount 0) start a brand new shape rather than act on whatever's upstream - kept in
			// their own submenu so they don't get lost in the same flat list as every modifier/combine node. Split
			// into two passes (rather than branching per-type inline) so that submenu consistently lands at the
			// top of "Create Node", instead of wherever its first alphabetically-sorted type happens to fall.
			var types = TypeCache.GetTypesDerivedFrom<GeoNode>().Where(t => !t.IsAbstract).OrderBy(t => t.Name).ToList();

			foreach (var type in types)
				if (((GeoNode) Activator.CreateInstance(type)).InputCount == 0)
					evt.menu.AppendAction($"Create Node/Generator/{NodraNodeView.GetDisplayName(type)}", _ => CreateNode(type, position));

			foreach (var type in types)
				if (((GeoNode) Activator.CreateInstance(type)).InputCount != 0)
					evt.menu.AppendAction($"Create Node/{NodraNodeView.GetDisplayName(type)}", _ => CreateNode(type, position));

			evt.menu.AppendSeparator();

			var selectedNodes = selection.OfType<NodraNodeView>().ToList();

			evt.menu.AppendAction("Group Selection", _ => GroupSelection(selectedNodes),
				_ => selectedNodes.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

			evt.menu.AppendAction("Ungroup Selection", _ => UngroupSelection(selectedNodes),
				_ => selectedNodes.Any(view => view.GetContainingScope() is Group)
					? DropdownMenuAction.Status.Normal
					: DropdownMenuAction.Status.Disabled);

			evt.menu.AppendSeparator();
			base.BuildContextualMenu(evt);
		}

		// Both actions lean entirely on the elementsAddedToGroup/elementsRemovedFromGroup callbacks below to sync
		// GeoGraph.Groups - Group.AddElement/RemoveElement is what actually reparents the node view (and, for
		// AddElement, resizes the group to fit), and GraphView invokes those callbacks as a consequence.
		void GroupSelection(List<NodraNodeView> nodeViews)
		{
			if (nodeViews.Count == 0)
				return;

			Undo.RecordObject(generator, "Group Selection");

			var geoGroup = new GeoGroup();
			generator.Graph.Groups.Add(geoGroup);

			var groupView = new Group { title = geoGroup.Title, userData = geoGroup };
			AddElement(groupView);

			foreach (var view in nodeViews)
				groupView.AddElement(view);

			EditorUtility.SetDirty(generator);
			onGraphChanged?.Invoke();
		}

		void UngroupSelection(List<NodraNodeView> nodeViews)
		{
			foreach (var view in nodeViews)
				if (view.GetContainingScope() is Group group)
					group.RemoveElement(view);
		}

		public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
			ports.ToList().Where(p => p.direction != startPort.direction && p.node != startPort.node).ToList();

		// Runs outside any GraphView-driven transaction (it's a direct context-menu action, or the node-search menu
		// below), so a full repopulate here is safe - nothing else is about to touch the view tree afterwards the
		// way it would mid-graphViewChanged.
		void CreateNode(Type type, Vector2 position) => CreateNode(type, position, null, null, -1);

		void CreateNodeConnectedFromOutput(Type type, Vector2 position, NodraNodeView sourceView) =>
			CreateNode(type, position, sourceView, null, -1);

		void CreateNodeConnectedToInput(Type type, Vector2 position, NodraNodeView targetView, int targetPortIndex) =>
			CreateNode(type, position, null, targetView, targetPortIndex);

		void CreateNode(Type type, Vector2 position, NodraNodeView sourceView, NodraNodeView targetView, int targetPortIndex)
		{
			Undo.RecordObject(generator, "Add Node");

			var node = (GeoNode) Activator.CreateInstance(type);
			node.Position = position;
			generator.Graph.Nodes.Add(node);

			// Dragged out of sourceView's output - wire it into the new node's first input, if it has one
			// (a Generator dragged this way never does, but the search menu already excludes those in that case).
			if (sourceView != null && node.InputCount > 0)
				generator.Graph.Edges.Add(new GeoEdge { FromNodeId = sourceView.Node.Id, ToNodeId = node.Id, ToPortIndex = 0 });

			// Dragged out of targetView's input (backwards, looking for a source) - wire the new node's output into it.
			if (targetView != null)
				generator.Graph.Edges.Add(new GeoEdge { FromNodeId = node.Id, ToNodeId = targetView.Node.Id, ToPortIndex = targetPortIndex });

			EditorUtility.SetDirty(generator);
			Populate();
			onGraphChanged?.Invoke();
		}

		// IEdgeConnectorListener - installed on every port (see NodraPort) instead of Port's own default listener,
		// purely to get a hook for the "dropped on empty canvas" case below. This has to fully reproduce what that
		// default listener does for a normal drop onto a compatible port, though, since replacing it means nothing
		// else calls graphViewChanged for a drag-made connection any more: it's what invokes OnGraphViewChanged /
		// SyncEdgeCreated below, which is what actually writes the edge into GeoGraph.Edges - skip it (as an
		// earlier version of this method did) and the connection is purely a visual Edge with no data behind it,
		// discarded the moment anything else triggers a repopulate. Connect() on both ports is what fills in each
		// port's connector dot, otherwise cosmetically indistinguishable from a disconnected one.
		public void OnDrop(GraphView graphView, Edge edge)
		{
			var change = new GraphViewChange { edgesToCreate = new List<Edge> { edge } };
			var edgesToCreate = graphViewChanged != null ? graphViewChanged(change).edgesToCreate : change.edgesToCreate;

			foreach (var created in edgesToCreate)
			{
				created.input.Connect(created);
				created.output.Connect(created);
				graphView.AddElement(created);
			}

			edge.input?.node?.RefreshPorts();
			edge.output?.node?.RefreshPorts();
		}

		// The dragged edge still has whichever end it started from set (output XOR input) and the other left null -
		// that tells us which direction to search: dragged from an output needs a node with an input to plug into,
		// dragged from an input (backwards) needs a source, which every node has exactly one of.
		public void OnDropOutsidePort(Edge edge, Vector2 position)
		{
			if (generator == null)
				return;

			var sourceView = edge.output?.node as NodraNodeView;
			var targetView = edge.input?.node as NodraNodeView;

			if (sourceView == null && targetView == null)
				return;

			var targetPortIndex = targetView != null ? Array.IndexOf(targetView.InputPorts, edge.input) : -1;
			var graphPosition = contentViewContainer.WorldToLocal(position);

			ShowNodeSearchMenu(graphPosition, sourceView, targetView, targetPortIndex);
		}

		void ShowNodeSearchMenu(Vector2 position, NodraNodeView sourceView, NodraNodeView targetView, int targetPortIndex)
		{
			var menu = new GenericMenu();

			foreach (var type in TypeCache.GetTypesDerivedFrom<GeoNode>().Where(t => !t.IsAbstract).OrderBy(t => t.Name))
			{
				var isGenerator = ((GeoNode) Activator.CreateInstance(type)).InputCount == 0;

				// Dragged out of an output looking for somewhere to plug into - a generator has nowhere for it to go.
				if (sourceView != null && isGenerator)
					continue;

				var path = isGenerator ? $"Generator/{NodraNodeView.GetDisplayName(type)}" : NodraNodeView.GetDisplayName(type);

				menu.AddItem(new GUIContent(path), false, () =>
				{
					if (sourceView != null)
						CreateNodeConnectedFromOutput(type, position, sourceView);
					else
						CreateNodeConnectedToInput(type, position, targetView, targetPortIndex);
				});
			}

			menu.ShowAsContext();
		}

		// Only syncs GeoGraph's data - the visual side (adding the new edge, removing a deleted node/edge's
		// elements, cascading a node deletion into its edges, ...) is applied by GraphView itself right after this
		// callback returns the (unmodified) change.
		GraphViewChange OnGraphViewChanged(GraphViewChange change)
		{
			var dirty = false;

			if (change.edgesToCreate != null)
				foreach (var edge in change.edgesToCreate)
					dirty |= SyncEdgeCreated(edge);

			if (change.elementsToRemove != null)
				foreach (var element in change.elementsToRemove)
					dirty |= SyncElementRemoved(element);

			if (change.movedElements != null)
				dirty |= SyncElementsMoved(change.movedElements);

			if (dirty)
			{
				EditorUtility.SetDirty(generator);
				serializedObject.Update();
				RefreshOutputHighlight();
				onGraphChanged?.Invoke();
			}

			return change;
		}

		bool SyncEdgeCreated(Edge edge)
		{
			var from = (NodraNodeView) edge.output.node;
			var to = (NodraNodeView) edge.input.node;
			var portIndex = Array.IndexOf(to.InputPorts, edge.input);

			if (portIndex < 0)
				return false;

			Undo.RecordObject(generator, "Connect Nodes");

			// A port models a single argument slot - wiring something new into it replaces whatever fed it before.
			generator.Graph.Edges.RemoveAll(e => e.ToNodeId == to.Node.Id && e.ToPortIndex == portIndex);
			generator.Graph.Edges.Add(new GeoEdge { FromNodeId = from.Node.Id, ToNodeId = to.Node.Id, ToPortIndex = portIndex });

			return true;
		}

		bool SyncElementRemoved(GraphElement element)
		{
			switch (element)
			{
				case NodraNodeView nodeView:
					Undo.RecordObject(generator, "Remove Node");
					generator.Graph.Nodes.Remove(nodeView.Node);
					generator.Graph.Edges.RemoveAll(e => e.FromNodeId == nodeView.Node.Id || e.ToNodeId == nodeView.Node.Id);
					if (generator.Graph.OutputNodeId == nodeView.Node.Id)
						generator.Graph.OutputNodeId = null;
					viewsById.Remove(nodeView.Node.Id);
					return true;

				case Edge edgeView when edgeView.output?.node is NodraNodeView from && edgeView.input?.node is NodraNodeView to:
					var portIndex = Array.IndexOf(to.InputPorts, edgeView.input);
					Undo.RecordObject(generator, "Disconnect Nodes");
					generator.Graph.Edges.RemoveAll(e => e.FromNodeId == from.Node.Id && e.ToNodeId == to.Node.Id && e.ToPortIndex == portIndex);
					return true;

				// Deleting the group itself, not its members - GraphView releases each contained node view back
				// onto the canvas rather than deleting them too, so only the GeoGroup entry needs to go here.
				case Group groupView when groupView.userData is GeoGroup geoGroup:
					Undo.RecordObject(generator, "Remove Group");
					generator.Graph.Groups.Remove(geoGroup);
					return true;

				default:
					return false;
			}
		}

		bool SyncElementsMoved(List<GraphElement> moved)
		{
			var any = false;

			foreach (var element in moved)
			{
				if (element is not NodraNodeView view)
					continue;

				if (!any)
					Undo.RecordObject(generator, "Move Node");

				any = true;
				view.Node.Position = view.GetPosition().position;
			}

			return any;
		}

		// Fires for a Group's title Label losing focus after an edit - covers both a brand new group (still named
		// "New Group") and renaming an existing one.
		void OnGroupTitleChanged(Group group, string title)
		{
			if (populating || group.userData is not GeoGroup geoGroup || geoGroup.Title == title)
				return;

			Undo.RecordObject(generator, "Rename Group");
			geoGroup.Title = title;
			EditorUtility.SetDirty(generator);
			onGraphChanged?.Invoke();
		}

		// Fires for every way a node ends up inside a group: GroupSelection() above, and the user dragging a node
		// (or a whole other selection) onto one directly on the canvas.
		void OnElementsAddedToGroup(Group group, IEnumerable<GraphElement> elements)
		{
			if (populating || group.userData is not GeoGroup geoGroup)
				return;

			var dirty = false;

			foreach (var element in elements)
			{
				if (element is not NodraNodeView view || geoGroup.NodeIds.Contains(view.Node.Id))
					continue;

				if (!dirty)
					Undo.RecordObject(generator, "Group Node(s)");

				dirty = true;
				geoGroup.NodeIds.Add(view.Node.Id);
			}

			if (dirty)
			{
				EditorUtility.SetDirty(generator);
				onGraphChanged?.Invoke();
			}
		}

		// Fires for UngroupSelection() above, dragging a node out of its group, and (as a side effect of the group
		// releasing its members first) deleting a group that still has some.
		void OnElementsRemovedFromGroup(Group group, IEnumerable<GraphElement> elements)
		{
			if (populating || group.userData is not GeoGroup geoGroup)
				return;

			var dirty = false;

			foreach (var element in elements)
			{
				if (element is not NodraNodeView view || !geoGroup.NodeIds.Remove(view.Node.Id))
					continue;

				if (!dirty)
					Undo.RecordObject(generator, "Ungroup Node(s)");

				dirty = true;
			}

			if (dirty)
			{
				EditorUtility.SetDirty(generator);
				onGraphChanged?.Invoke();
			}
		}

		// Ctrl+C/Ctrl+X hand the current selection to this - GraphView only wires up copy/cut/paste at all once
		// serializeGraphElements is set, and otherwise ignores those shortcuts entirely. Each selected node is
		// serialized on its own (via its concrete runtime type - JsonUtility.ToJson(object) uses GetType(), not the
		// GeoNode-typed reference) since JsonUtility can't handle a heterogeneous List<GeoNode> directly; edges are
		// kept only when both endpoints are in the copied set, referencing nodes by their *original* Id, remapped
		// to fresh ones on paste.
		string SerializeSelection(IEnumerable<GraphElement> elements)
		{
			if (generator == null)
				return string.Empty;

			var payload = new ClipboardPayload();
			var copiedIds = new HashSet<string>();

			foreach (var element in elements)
			{
				if (element is not NodraNodeView view)
					continue;

				copiedIds.Add(view.Node.Id);
				payload.Nodes.Add(new ClipboardNode
				{
					TypeName = view.Node.GetType().AssemblyQualifiedName,
					Json = JsonUtility.ToJson(view.Node),
					OriginalId = view.Node.Id,
					Position = view.Node.Position,
				});
			}

			foreach (var edge in generator.Graph.Edges)
				if (copiedIds.Contains(edge.FromNodeId) && copiedIds.Contains(edge.ToNodeId))
					payload.Edges.Add(new ClipboardEdge { FromId = edge.FromNodeId, ToId = edge.ToNodeId, ToPortIndex = edge.ToPortIndex });

			return payload.Nodes.Count > 0 ? JsonUtility.ToJson(payload) : string.Empty;
		}

		// A fixed nudge (rather than pasting exactly on top of the originals) so the pasted copies are immediately
		// visible and draggable as their own selection.
		static readonly Vector2 PasteOffset = new (40f, 40f);

		void UnserializeAndPaste(string operationName, string data)
		{
			if (generator == null)
				return;

			ClipboardPayload payload;

			try
			{
				payload = JsonUtility.FromJson<ClipboardPayload>(data);
			}
			catch (ArgumentException)
			{
				return;
			}

			if (payload?.Nodes == null || payload.Nodes.Count == 0)
				return;

			Undo.RecordObject(generator, operationName);

			var idRemap = new Dictionary<string, string>();
			var pastedIds = new List<string>();

			foreach (var clipboardNode in payload.Nodes)
			{
				var type = Type.GetType(clipboardNode.TypeName);
				if (type == null)
					continue;

				var node = (GeoNode) JsonUtility.FromJson(clipboardNode.Json, type);
				node.Id = Guid.NewGuid().ToString("N"); // never collide with the node(s) it was copied from
				node.Position = clipboardNode.Position + PasteOffset;

				idRemap[clipboardNode.OriginalId] = node.Id;
				generator.Graph.Nodes.Add(node);
				pastedIds.Add(node.Id);
			}

			foreach (var clipboardEdge in payload.Edges)
				if (idRemap.TryGetValue(clipboardEdge.FromId, out var from) && idRemap.TryGetValue(clipboardEdge.ToId, out var to))
					generator.Graph.Edges.Add(new GeoEdge { FromNodeId = from, ToNodeId = to, ToPortIndex = clipboardEdge.ToPortIndex });

			EditorUtility.SetDirty(generator);
			Populate();

			ClearSelection();
			foreach (var id in pastedIds)
				if (viewsById.TryGetValue(id, out var view))
					AddToSelection(view);

			onGraphChanged?.Invoke();
		}

		[Serializable]
		class ClipboardNode
		{
			public string TypeName;
			public string Json;
			public string OriginalId;
			public Vector2 Position;
		}

		[Serializable]
		class ClipboardEdge
		{
			public string FromId;
			public string ToId;
			public int ToPortIndex;
		}

		[Serializable]
		class ClipboardPayload
		{
			public List<ClipboardNode> Nodes = new ();
			public List<ClipboardEdge> Edges = new ();
		}
	}
}
