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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nodra
{
	/// <summary> Converts a GeoData snapshot into a renderable Unity Mesh, fan-triangulating each primitive.
	/// Normals are always recalculated on the built mesh rather than trusting GeoData.Normals, since nodes like
	/// NoiseDisplaceNode move points without keeping normals in sync - GeoData.Normals only needs to stay correct
	/// long enough for nodes further down the chain (e.g. displacement direction, Copy to Points alignment).
	/// SmoothByAngleNode's own groups are the one exception, reapplied after the fact - see ApplySmoothGroups. </summary>
	public static class GeoMeshBuilder
	{
		public static Mesh Build(GeoData data, string name = "ProceduralMesh")
		{
			var mesh = new Mesh { name = name };

			if (data.PointCount > 65000)
				mesh.indexFormat = IndexFormat.UInt32;

			mesh.SetVertices(data.Points);
			mesh.SetUVs(0, data.Uvs);
			mesh.SetColors(data.Colors);
			mesh.SetTriangles(Triangulate(data), 0);

			mesh.RecalculateNormals();
			ApplySmoothGroups(data, mesh);
			mesh.RecalculateBounds();
			mesh.RecalculateTangents();

			return mesh;
		}

		// RecalculateNormals above works per vertex INDEX and by triangle AREA, neither of which SmoothByAngleNode
		// can rely on: it can't merge normals across a UV seam's position-duplicate indices, and a pole/apex
		// quad's fan-triangulation always makes one of its two triangles zero-area (both its non-pole corners are
		// the same pole point) - a vertex used only by that degenerate triangle gets exactly Vector3.zero back,
		// not something close to the right direction. SmoothByAngleNode already computed the correct normal
		// itself (Newell's method on the actual faces, no triangle-area weighting involved) and left it in
		// GeoData.Normals, so tagged points just take that value directly instead of trusting RecalculateNormals.
		static void ApplySmoothGroups(GeoData data, Mesh mesh)
		{
			if (!data.HasAttribute(GeoData.SmoothGroupAttribute))
				return;

			var normals = mesh.normals;

			for (var i = 0; i < data.PointCount; i++)
				if (data.GetAttribute(GeoData.SmoothGroupAttribute, i) != 0)
					normals[i] = data.Normals[i];

			mesh.SetNormals(normals);
		}

		static List<int> Triangulate(GeoData data)
		{
			var triangles = new List<int>();

			foreach (var primitive in data.Primitives)
			{
				for (var i = 1; i < primitive.Length - 1; i++)
				{
					triangles.Add(primitive[0]);
					triangles.Add(primitive[i]);
					triangles.Add(primitive[i + 1]);
				}
			}

			return triangles;
		}
	}
}
