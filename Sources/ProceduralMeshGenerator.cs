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

using UnityEngine;

namespace Nodra
{
	/// <summary> Runs an ordered list of GeoNodes (generators, modifiers, scatter/copy) and bakes the result into
	/// the attached MeshFilter - the "compile" step of the procedural graph. Nodes are edited as a plain
	/// reorderable list rather than a visual graph for now; each one only ever reads the output of the node right
	/// before it. </summary>
	[ExecuteAlways]
	[RequireComponent(typeof(MeshFilter))]
	[AddComponentMenu("Nodra/Procedural Mesh Generator")]
	public class ProceduralMeshGenerator : MonoBehaviour
	{
		/// <summary> When on, any Inspector edit to Nodes (add/remove/reorder/field change) regenerates the mesh -
		/// see OnValidate. Off by default since regenerating on every keystroke can get expensive for a heavy pipeline. </summary>
		public bool AutoGenerate;

		public GeoNodeList Nodes = new ();

		MeshFilter meshFilter;

		[ContextMenu("Generate")]
		public void Generate()
		{
			if (!meshFilter)
				meshFilter = GetComponent<MeshFilter>();

			var data = Nodes.Process(null);

			meshFilter.sharedMesh = data != null ? GeoMeshBuilder.Build(data, gameObject.name) : null;
		}

#if UNITY_EDITOR
		bool regenerateQueued;

		// Inspector edits (including our custom Add Node menu, which calls ApplyModifiedProperties like any other
		// property change) call OnValidate synchronously, sometimes several times per frame - deferring the actual
		// Generate() by one delayCall collapses those into a single rebuild and avoids running it mid-serialization.
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
