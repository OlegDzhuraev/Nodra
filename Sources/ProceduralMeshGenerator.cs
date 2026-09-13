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
	/// <summary> Runs a GeoGraph (generators, modifiers, scatter/copy, merge - wired together in NodraGraphWindow)
	/// and bakes the result into the attached MeshFilter - the "compile" step of the procedural graph. </summary>
	[ExecuteAlways]
	[RequireComponent(typeof(MeshFilter))]
	[AddComponentMenu("Nodra/Procedural Mesh Generator")]
	public class ProceduralMeshGenerator : MonoBehaviour
	{
		/// <summary> When on, any graph edit (add/remove/connect/field change) regenerates the mesh - see
		/// OnValidate. Off by default since regenerating on every keystroke can get expensive for a heavy graph. </summary>
		public bool AutoGenerate;

		public GeoGraph Graph = new ();

		MeshFilter meshFilter;

		/// <summary> The GeoData Generate() last evaluated, kept around purely so ProceduralMeshGeneratorEditor can
		/// draw it as gizmos (e.g. ScatterNode's points, invisible in the baked Mesh since it has no faces) without
		/// re-evaluating the whole graph every OnSceneGUI call. Not meant for anything else - it's not cleared when
		/// the graph changes without a Generate(), so it can briefly go stale between an edit and the next bake. </summary>
		public GeoData LastEvaluatedData { get; private set; }

		[ContextMenu("Generate")]
		public void Generate()
		{
			if (!meshFilter)
				meshFilter = GetComponent<MeshFilter>();

			GeoData data;

			try
			{
				data = Graph.Evaluate();
			}
			catch (TimeoutException e)
			{
				// GeoCsg (BooleanNode) is the only node that can throw this - a controlled abort instead of the
				// stack overflow a runaway BSP-tree CSG used to cause on heavy/pathological input. The previous
				// mesh (and LastEvaluatedData) are left exactly as they were rather than cleared, so one failed
				// regeneration doesn't also blank out whatever was already there.
				Debug.LogError(e.Message, this);
				return;
			}

			LastEvaluatedData = data;

			meshFilter.sharedMesh = data != null ? GeoMeshBuilder.Build(data, gameObject.name) : null;
		}

#if UNITY_EDITOR
		// Only fires when the component is first added (or via the Inspector's own "Reset" context menu action) -
		// a brand new graph starts with somewhere for the mesh to actually come from, instead of an empty canvas
		// needing a manual right-click every time. Left out of the GeoGraph constructor on purpose: Unity re-runs
		// a plain [Serializable] class's constructor on every deserialize, including an already-saved graph full
		// of the user's own nodes, which isn't a safe place for a one-time "seed the default state" side effect.
		void Reset() => Graph.Nodes.Add(new GeometryOutputNode());

		bool regenerateQueued;

		// Covers edits to a node's own field values, which reach here through the normal SerializedProperty ->
		// ApplyModifiedProperties -> OnValidate path (NodraNodeView binds its fields the same way an Inspector
		// PropertyField would). Structural graph edits (add/remove/connect/move a node) bypass SerializedProperty
		// entirely - NodraGraphView triggers Generate() for those itself; see its onGraphChanged callback.
		// OnValidate can also run several times per frame, so deferring the actual Generate() by one delayCall
		// collapses those into a single rebuild and avoids running it mid-serialization.
		void OnValidate()
		{
			if (!AutoGenerate || regenerateQueued)
				return;

			regenerateQueued = true;
			UnityEditor.EditorApplication.delayCall += DelayedGenerate;
		}

		void DelayedGenerate()
		{
			regenerateQueued = false;
			UnityEditor.EditorApplication.delayCall -= DelayedGenerate;

			if (this)
				Generate();
		}
#endif
	}
}
