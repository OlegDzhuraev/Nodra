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
	/// <summary> A GeoGraph saved as its own reusable project asset instead of living inside one
	/// ProceduralMeshGenerator component - referenced by SubGraphNode so several graphs (on different objects, even
	/// different scenes) can all build on the same shared piece of graph instead of duplicating it. Edited in the
	/// same NodraGraphWindow a ProceduralMeshGenerator's own graph is: double-click the asset (or use its Inspector
	/// button, GeoGraphAssetEditor) to open it. </summary>
	[CreateAssetMenu(fileName = "New Geo Graph", menuName = "Nodra/Geo Graph")]
	public class GeoGraphAsset : ScriptableObject
	{
		public GeoGraph Graph = new ();

#if UNITY_EDITOR
		// Only fires right after the asset is first created (or via the Inspector's own "Reset" context menu
		// action) - same reasoning/pattern as ProceduralMeshGenerator.Reset(): a brand new asset starts with
		// somewhere for a SubGraphNode to actually get a result from, instead of an empty canvas needing a manual
		// right-click every time. Left out of the GeoGraph constructor on purpose: Unity re-runs a plain
		// [Serializable] class's constructor on every deserialize, including an already-saved graph full of the
		// user's own nodes, which isn't a safe place for a one-time "seed the default state" side effect.
		void Reset() => Graph.Nodes.Add(new GeometryOutputNode());
#endif
	}
}
