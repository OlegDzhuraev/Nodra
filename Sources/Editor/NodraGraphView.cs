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
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nodra
{
	/// <summary> The canvas that draws and edits one GeoGraph - either a ProceduralMeshGenerator component's own, or
	/// a standalone GeoGraphAsset's (see SubGraphNode). Nodes and edges are rebuilt from the owner's serialized
	/// state on bind/undo, and every edit (add/remove/connect/move node) writes straight back into that same
	/// GeoGraph instance via Undo.RecordObject, so it behaves like any other Inspector edit (Undo, prefab overrides,
	/// multi-scene). Beyond syncing that data, most edits are left for GraphView's own default behaviour to apply
	/// visually (adding the new edge, removing a deleted node's elements, ...) - forcing a full re-population there
	/// too would fight GraphView's own bookkeeping. </summary>
	public class NodraGraphView : GraphView, IEdgeConnectorListener
	{
		readonly Dictionary<string, NodraNodeView> viewsById = new ();

		// owner is whichever UnityEngine.Object actually holds `graph` (a ProceduralMeshGenerator or a
		// GeoGraphAsset) - kept separate from graph itself since Undo.RecordObject/EditorUtility.SetDirty/
		// SerializedObject all need the owning Object, not the plain [Serializable] GeoGraph nested inside it.
		UnityEngine.Object owner;
		GeoGraph graph;
		SerializedObject serializedObject;
		Action onGraphChanged;

		bool populating;
		bool repopulateScheduled;

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

			RegisterCallback<KeyDownEvent>(OnKeyDown);
		}

		// actionKey is Ctrl on Windows/Linux, Cmd on Mac - a text field mid-edit (a node's own Segments field,
		// say) gets first crack at the same keystroke for its own "select all text" and stops it there, so this
		// only ever fires when the graph canvas itself has focus.
		void OnKeyDown(KeyDownEvent evt)
		{
			if (!evt.actionKey || evt.keyCode != KeyCode.A)
				return;

			ClearSelection();
			foreach (var view in viewsById.Values)
				AddToSelection(view);

			evt.StopPropagation();
		}

		public void Bind(UnityEngine.Object newOwner, GeoGraph newGraph, Action onChanged)
		{
			owner = newOwner;
			graph = newGraph;
			serializedObject = owner != null ? new SerializedObject(owner) : null;
			onGraphChanged = onChanged;

			Populate();

			// Deferred a tick rather than called right here - UI Toolkit only computes each new NodraNodeView's
			// actual laid-out size/position on the next update, and FrameAll() needs real geometry to frame
			// around. Otherwise a freshly-opened window (e.g. a brand new graph's lone GeometryOutputNode, seeded
			// well away from wherever the view happens to default to) can leave its only content off-screen.
			schedule.Execute(() => FrameAll()).ExecuteLater(0);
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
			// purely-visual clear as "the user deleted everything" and wipe the underlying GeoGraph. Each old
			// NodraNodeView is explicitly Unbind()'d first - RemoveElement alone only detaches it visually, its
			// PropertyFields stay bound to their old (soon stale) SerializedProperty paths otherwise, and Unity
			// eventually throws ObjectDisposedException trying to resync one of those on a completely unrelated
			// field's blur.
			foreach (var element in graphElements.ToList())
			{
				if (element is NodraNodeView nodeView)
					nodeView.Unbind();

				RemoveElement(element);
			}

			viewsById.Clear();

			if (owner == null || graph == null)
				return;

			serializedObject.Update();

			// "Graph" (not nameof(ProceduralMeshGenerator.Graph)) since owner can be either a ProceduralMeshGenerator
			// or a GeoGraphAsset - both happen to name their own GeoGraph field exactly this, which is what this
			// lookup actually depends on, not which concrete type owner is.
			var graphProperty = serializedObject.FindProperty("Graph");
			var nodesProperty = graphProperty.FindPropertyRelative(nameof(GeoGraph.Nodes));

			for (var i = 0; i < graph.Nodes.Count; i++)
			{
				var node = graph.Nodes[i];
				if (node == null)
					continue;

				var view = new NodraNodeView(node, nodesProperty.GetArrayElementAtIndex(i), this);
				viewsById[node.Id] = view;
				AddElement(view);
			}

			foreach (var edge in graph.Edges)
			{
				if (!viewsById.TryGetValue(edge.FromNodeId, out var from) || !viewsById.TryGetValue(edge.ToNodeId, out var to))
					continue;

				if (from.OutputPort == null)
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

			foreach (var geoGroup in graph.Groups)
			{
				var groupView = new Group { title = geoGroup.Title, userData = geoGroup };
				AddElement(groupView);

				foreach (var nodeId in geoGroup.NodeIds)
					if (viewsById.TryGetValue(nodeId, out var view))
						groupView.AddElement(view);
			}

			RefreshOutputHighlight();
		}

		void RefreshOutputHighlight()
		{
			var outputId = graph?.GetOutputNode()?.Id;

			foreach (var view in viewsById.Values)
				view.SetIsOutput(view.Node.Id == outputId);
		}

		// Fixed order for the "Create Node" submenus - Generators first (the natural start of any new graph),
		// Combine last (the natural end); a category not listed here (a future node nobody categorized) still
		// shows up, just after all of these. Node type list has grown past a single flat list being usable at all.
		static readonly string[] CategoryOrder = { "Generators", "Deform", "Build", "Cleanup", "Color & UV", "Scatter/Copy", "Combine", "Output", "Modifiers" };

		// Every non-abstract GeoNode, one instance each - reused for both the category grouping below and the
		// InputCount check, instead of the type list being walked (and each type instantiated) twice over.
		static List<(Type type, GeoNode instance)> AllNodeTypes() =>
			TypeCache.GetTypesDerivedFrom<GeoNode>()
				.Where(t => !t.IsAbstract)
				.Select(t => (type: t, instance: (GeoNode) Activator.CreateInstance(t)))
				.OrderBy(pair => CategoryRank(pair.instance.Category))
				.ThenBy(pair => pair.type.Name)
				.ToList();

		static int CategoryRank(string category)
		{
			var index = Array.IndexOf(CategoryOrder, category);
			return index >= 0 ? index : CategoryOrder.Length;
		}

		public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			if (owner == null || graph == null)
				return;

			var position = contentViewContainer.WorldToLocal(evt.mousePosition);

			foreach (var (type, instance) in AllNodeTypes())
				evt.menu.AppendAction($"Create Node/{instance.Category}/{NodraNodeView.GetDisplayName(type)}", _ => CreateNode(type, position));

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

		void GroupSelection(List<NodraNodeView> nodeViews)
		{
			if (nodeViews.Count == 0)
				return;

			Undo.RecordObject(owner, "Group Selection");

			var geoGroup = new GeoGroup();
			graph.Groups.Add(geoGroup);

			var groupView = new Group { title = geoGroup.Title, userData = geoGroup };
			AddElement(groupView);

			foreach (var view in nodeViews)
				groupView.AddElement(view);

			EditorUtility.SetDirty(owner);
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
			Undo.RecordObject(owner, "Add Node");

			var node = (GeoNode) Activator.CreateInstance(type);
			node.Position = position;
			graph.Nodes.Add(node);

			// Dragged out of sourceView's output - wire it into the new node's first input, if it has one
			// (a Generator dragged this way never does, but the search menu already excludes those in that case).
			if (sourceView != null && node.InputCount > 0)
				graph.Edges.Add(new GeoEdge { FromNodeId = sourceView.Node.Id, ToNodeId = node.Id, ToPortIndex = 0 });

			// Dragged out of targetView's input (backwards, looking for a source) - wire the new node's output into it.
			if (targetView != null)
			{
				graph.Edges.RemoveAll(e => e.ToNodeId == targetView.Node.Id && e.ToPortIndex == targetPortIndex);
				graph.Edges.Add(new GeoEdge { FromNodeId = node.Id, ToNodeId = targetView.Node.Id, ToPortIndex = targetPortIndex });
			}

			EditorUtility.SetDirty(owner);
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
			var toDelete = new List<GraphElement>();

			if (edge.input?.capacity == Port.Capacity.Single)
				foreach (var connection in edge.input.connections)
					if (connection != edge)
						toDelete.Add(connection);

			if (edge.output?.capacity == Port.Capacity.Single)
				foreach (var connection in edge.output.connections)
					if (connection != edge)
						toDelete.Add(connection);

			if (toDelete.Count > 0)
				graphView.DeleteElements(toDelete);

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
			if (owner == null || graph == null)
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

			foreach (var (type, instance) in AllNodeTypes())
			{
				// Dragged out of an output looking for somewhere to plug into - a generator (InputCount 0) has
				// nowhere for it to go.
				if (sourceView != null && instance.InputCount == 0)
					continue;

				var path = $"{instance.Category}/{NodraNodeView.GetDisplayName(type)}";

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
			var structural = false; // could actually change what the graph computes - a move alone never does
			var nodeRemoved = false;

			if (change.edgesToCreate != null)
				foreach (var edge in change.edgesToCreate)
				{
					var created = SyncEdgeCreated(edge);
					dirty |= created;
					structural |= created;
				}

			if (change.elementsToRemove != null)
				foreach (var element in change.elementsToRemove)
				{
					nodeRemoved |= element is NodraNodeView;

					var removed = SyncElementRemoved(element);
					dirty |= removed;
					structural |= removed;
				}

			// Position doesn't feed into GeoGraph.ComputeNodeHash at all (see GeoGraph.cs) - still needs Undo/
			// SetDirty below so the new layout is actually saved, but it's not "structural" the way an edge or a
			// node coming or going is, so it alone shouldn't call onGraphChanged (Generate() would just walk the
			// whole graph's hashes to confirm nothing changed, every single drag frame, for no reason).
			if (change.movedElements != null)
				dirty |= SyncElementsMoved(change.movedElements);

			if (dirty)
			{
				EditorUtility.SetDirty(owner);
				serializedObject.Update();
				RefreshOutputHighlight();
			}

			if (structural)
				onGraphChanged?.Invoke();

			// A removed node shifts every later node's index in Graph.Nodes - every surviving node's fields are
			// still bound to their old index-based SerializedProperty path (Array.data[N]...), which is now either
			// wrong or gone. Deferred rather than done inline: GraphView is still mid-way through removing this
			// change's own elements right after this callback returns, and rebuilding the whole view tree out from
			// under that would fight it - scheduling it for the next update tick lets that finish first.
			if (nodeRemoved && !repopulateScheduled)
			{
				repopulateScheduled = true;
				schedule.Execute(() =>
				{
					repopulateScheduled = false;
					Populate();
				}).ExecuteLater(0);
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

			Undo.RecordObject(owner, "Connect Nodes");

			// A port models a single argument slot - wiring something new into it replaces whatever fed it before.
			graph.Edges.RemoveAll(e => e.ToNodeId == to.Node.Id && e.ToPortIndex == portIndex);
			graph.Edges.Add(new GeoEdge { FromNodeId = from.Node.Id, ToNodeId = to.Node.Id, ToPortIndex = portIndex });

			return true;
		}

		bool SyncElementRemoved(GraphElement element)
		{
			switch (element)
			{
				case NodraNodeView nodeView:
					Undo.RecordObject(owner, "Remove Node");
					graph.Nodes.Remove(nodeView.Node);
					graph.Edges.RemoveAll(e => e.FromNodeId == nodeView.Node.Id || e.ToNodeId == nodeView.Node.Id);
					viewsById.Remove(nodeView.Node.Id);
					nodeView.Unbind();
					return true;

				case Edge edgeView when edgeView.output?.node is NodraNodeView from && edgeView.input?.node is NodraNodeView to:
					var portIndex = Array.IndexOf(to.InputPorts, edgeView.input);
					Undo.RecordObject(owner, "Disconnect Nodes");
					graph.Edges.RemoveAll(e => e.FromNodeId == from.Node.Id && e.ToNodeId == to.Node.Id && e.ToPortIndex == portIndex);
					return true;

				case Group groupView when groupView.userData is GeoGroup geoGroup:
					Undo.RecordObject(owner, "Remove Group");
					graph.Groups.Remove(geoGroup);
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
					Undo.RecordObject(owner, "Move Node");

				any = true;
				view.Node.Position = view.GetPosition().position;
			}

			return any;
		}

		void OnGroupTitleChanged(Group group, string title)
		{
			if (populating || group.userData is not GeoGroup geoGroup || geoGroup.Title == title)
				return;

			Undo.RecordObject(owner, "Rename Group");
			geoGroup.Title = title;
			EditorUtility.SetDirty(owner);
			onGraphChanged?.Invoke();
		}

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
					Undo.RecordObject(owner, "Group Node(s)");

				dirty = true;
				geoGroup.NodeIds.Add(view.Node.Id);
			}

			if (dirty)
			{
				EditorUtility.SetDirty(owner);
				onGraphChanged?.Invoke();
			}
		}

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
					Undo.RecordObject(owner, "Ungroup Node(s)");

				dirty = true;
			}

			if (dirty)
			{
				EditorUtility.SetDirty(owner);
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
			if (owner == null || graph == null)
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

			foreach (var edge in graph.Edges)
				if (copiedIds.Contains(edge.FromNodeId) && copiedIds.Contains(edge.ToNodeId))
					payload.Edges.Add(new ClipboardEdge { FromId = edge.FromNodeId, ToId = edge.ToNodeId, ToPortIndex = edge.ToPortIndex });

			return payload.Nodes.Count > 0 ? JsonUtility.ToJson(payload) : string.Empty;
		}

		// A fixed nudge (rather than pasting exactly on top of the originals) so the pasted copies are immediately
		// visible and draggable as their own selection.
		static readonly Vector2 PasteOffset = new (40f, 40f);

		void UnserializeAndPaste(string operationName, string data)
		{
			if (owner == null || graph == null)
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

			Undo.RecordObject(owner, operationName);

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
				graph.Nodes.Add(node);
				pastedIds.Add(node.Id);
			}

			foreach (var clipboardEdge in payload.Edges)
				if (idRemap.TryGetValue(clipboardEdge.FromId, out var from) && idRemap.TryGetValue(clipboardEdge.ToId, out var to))
					graph.Edges.Add(new GeoEdge { FromNodeId = from, ToNodeId = to, ToPortIndex = clipboardEdge.ToPortIndex });

			EditorUtility.SetDirty(owner);
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
