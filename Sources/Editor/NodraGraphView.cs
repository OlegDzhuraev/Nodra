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
	public class NodraGraphView : GraphView
	{
		readonly Dictionary<string, NodraNodeView> viewsById = new ();

		ProceduralMeshGenerator generator;
		SerializedObject serializedObject;
		Action onGraphChanged;

		public NodraGraphView()
		{
			SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

			this.AddManipulator(new ContentDragger());
			this.AddManipulator(new SelectionDragger());
			this.AddManipulator(new RectangleSelector());
			this.AddManipulator(new ContentZoomer());

			var grid = new GridBackground();
			Insert(0, grid);
			grid.StretchToParentSize();

			graphViewChanged = OnGraphViewChanged;
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

			foreach (var type in TypeCache.GetTypesDerivedFrom<GeoNode>().OrderBy(t => t.Name))
			{
				if (type.IsAbstract)
					continue;

				evt.menu.AppendAction($"Create Node/{NodraNodeView.GetDisplayName(type)}", _ => CreateNode(type, position));
			}

			evt.menu.AppendSeparator();
			base.BuildContextualMenu(evt);
		}

		public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
			ports.ToList().Where(p => p.direction != startPort.direction && p.node != startPort.node).ToList();

		// Runs outside any GraphView-driven transaction (it's a direct context-menu action), so a full repopulate
		// here is safe - nothing else is about to touch the view tree afterwards the way it would mid-graphViewChanged.
		void CreateNode(Type type, Vector2 position)
		{
			Undo.RecordObject(generator, "Add Node");

			var node = (GeoNode) Activator.CreateInstance(type);
			node.Position = position;
			generator.Graph.Nodes.Add(node);

			EditorUtility.SetDirty(generator);
			Populate();
			onGraphChanged?.Invoke();
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
	}
}
