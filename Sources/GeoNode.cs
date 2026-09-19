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
using UnityEngine;

namespace Nodra
{
	/// <summary> One node of a GeoGraph. Generator nodes usually ignore their input and add fresh points/primitives
	/// to it, modifier nodes mutate what they receive, and nodes like Scatter deliberately return a brand new
	/// GeoData instead of passing the input through. </summary>
	[Serializable]
	public abstract class GeoNode
	{
		public bool Enabled = true;

		/// <summary> Stable identity used by GeoGraph.Edges to reference this node regardless of its position in
		/// the Nodes list - assigned once on creation and never touched afterwards. </summary>
		public string Id = Guid.NewGuid().ToString("N");

		/// <summary> Node position in the graph editor's canvas, in graph-local (unzoomed) coordinates. </summary>
		public Vector2 Position;

		/// <summary> How many input ports the graph editor should draw for this node - 0 for a generator, which
		/// ignores input entirely. A node with more than one input (MergeNode) overrides Process(GeoData[])
		/// directly instead of the single-input Process(GeoData) below. </summary>
		public virtual int InputCount => 1;

		/// <summary> Label for input port `index`, shown in the graph editor. </summary>
		public virtual string GetInputPortName(int index) => InputCount <= 1 ? "In" : $"In {index}";

		/// <summary> Whether the graph editor should draw an output port for this node - true for everything
		/// except GeometryOutputNode, which is the graph's terminal node and so has nothing to feed downstream. </summary>
		public virtual bool HasOutput => true;

		/// <summary> Submenu this node type is filed under in the graph editor's "Create Node" menu - purely a
		/// grouping label, read nowhere else. Defaults to "Modifiers"; override with something more specific
		/// (see NodraGraphView.CategoryOrder for the established set). </summary>
		public virtual string Category => "Modifiers";

		/// <summary> Null/empty for none (the default) - otherwise shown as a warning box in the node's own body
		/// in the graph editor, e.g. DecimateNode surfacing "its optional dependency isn't installed" without
		/// anyone having to open the Inspector to notice. </summary>
		public virtual string Warning => null;

		/// <summary> Null for every node except SubGraphNode - extra content this node's Process() output depends
		/// on that GeoGraph.ComputeNodeHash's JsonUtility-based field hash can't see, folded into that hash so
		/// GeoGraph.resultCache still invalidates correctly. SubGraphNode needs this because its SubGraph field is
		/// a UnityEngine.Object reference: JsonUtility can't be relied on to capture which asset that is, and even
		/// if it could, that says nothing about what's currently inside it. </summary>
		public virtual string ExtraHash => null;

		/// <summary> Field name of this node's UnityEngine.Object reference meant to be edited on its own
		/// (SubGraphNode's "SubGraph") - null for every other node. Drives a live "Open" button next to that field
		/// in the graph editor (NodraNodeView) so jumping into it doesn't require hunting the asset down in the
		/// Project window. A field name rather than the resolved reference itself so the button can track the same
		/// SerializedProperty the field's own PropertyField is bound to, live-enabling the moment something's
		/// actually assigned instead of needing this node's view rebuilt first. </summary>
		public virtual string EditableAssetFieldName => null;

		/// <summary> Entry point used by GeoGraph evaluation. The default forwards the first connected input (or
		/// null, if none) to the single-input overload below - override this instead when a node needs more than
		/// one input. </summary>
		public virtual GeoData Process(GeoData[] inputs) => Process(inputs.Length > 0 ? inputs[0] : null);

		/// <summary> Single-input processing - most nodes only need to override this one. </summary>
		public virtual GeoData Process(GeoData input) => input;
	}
}
