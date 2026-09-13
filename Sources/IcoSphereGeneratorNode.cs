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
using UnityEngine;

namespace Nodra
{
	/// <summary> Generates a sphere by subdividing an icosahedron and projecting every vertex onto Radius - unlike
	/// SphereGeneratorNode's UV sphere, triangles stay near-equal in size everywhere, with no pinching at the
	/// poles. Subdivisions 0 gives the plain 20-triangle icosahedron; each step after that quarters every
	/// triangle. </summary>
	[Serializable]
	public class IcoSphereGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		public float Radius = 1f;
		[Range(0, 6)] public int Subdivisions = 2;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var points = BuildIcosahedron(out var triangles);

			for (var i = 0; i < Subdivisions; i++)
				triangles = Subdivide(points, triangles);

			var startIndex = data.PointCount;

			foreach (var point in points)
			{
				var direction = point.normalized;
				var u = Mathf.Atan2(direction.z, direction.x) / (Mathf.PI * 2f) + 0.5f;
				var v = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) / Mathf.PI + 0.5f;

				data.AddPoint(direction * Radius, direction, new Vector2(u, v));
			}

			foreach (var triangle in triangles)
				data.AddPrimitive(startIndex + triangle[0], startIndex + triangle[1], startIndex + triangle[2]);

			return data;
		}

		// Standard 12-vertex/20-face icosahedron, golden-ratio construction. Face winding verified by hand
		// (Cross(v1 - v0, v2 - v0) checked against the face's own centroid direction) to point outward, matching
		// this package's Cross-product winding convention.
		static List<Vector3> BuildIcosahedron(out List<int[]> triangles)
		{
			var t = (1f + Mathf.Sqrt(5f)) * 0.5f;

			var points = new List<Vector3>
			{
				new (-1, t, 0), new (1, t, 0), new (-1, -t, 0), new (1, -t, 0),
				new (0, -1, t), new (0, 1, t), new (0, -1, -t), new (0, 1, -t),
				new (t, 0, -1), new (t, 0, 1), new (-t, 0, -1), new (-t, 0, 1),
			};

			triangles = new List<int[]>
			{
				new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
				new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
				new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
				new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
			};

			return points;
		}

		// Splits every triangle into 4 by adding a point at each edge's flat midpoint - a shared edge's midpoint
		// is cached and reused by both triangles on either side of it, so subdividing never duplicates a vertex.
		// Left flat rather than re-projected onto the sphere at every level - all that matters is the final
		// direction from the origin, and Process() normalizes every point exactly once, at the end.
		static List<int[]> Subdivide(List<Vector3> points, List<int[]> triangles)
		{
			var midpointCache = new Dictionary<(int a, int b), int>();
			var result = new List<int[]>(triangles.Count * 4);

			foreach (var triangle in triangles)
			{
				var a = MidPoint(points, midpointCache, triangle[0], triangle[1]);
				var b = MidPoint(points, midpointCache, triangle[1], triangle[2]);
				var c = MidPoint(points, midpointCache, triangle[2], triangle[0]);

				result.Add(new[] { triangle[0], a, c });
				result.Add(new[] { triangle[1], b, a });
				result.Add(new[] { triangle[2], c, b });
				result.Add(new[] { a, b, c });
			}

			return result;
		}

		static int MidPoint(List<Vector3> points, Dictionary<(int, int), int> cache, int i0, int i1)
		{
			var key = i0 < i1 ? (i0, i1) : (i1, i0);

			if (cache.TryGetValue(key, out var index))
				return index;

			points.Add((points[i0] + points[i1]) * 0.5f);
			index = points.Count - 1;
			cache[key] = index;

			return index;
		}
	}
}
