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
	/// <summary> Laplacian smoothing: moves every point toward the average position of its edge-connected
	/// neighbors by Factor, Iterations times - softens jagged NoiseDisplaceNode/BooleanNode results without
	/// changing topology. PreserveBoundary keeps an open mesh's outer rim from shrinking inward. </summary>
	[Serializable]
	public class RelaxNode : GeoNode
	{
		public override string Category => "Deform";

		[Range(0f, 1f)] public float Factor = 0.5f;
		[Min(1)] public int Iterations = 1;
		public bool PreserveBoundary = true;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			var neighbors = BuildNeighbors(input);
			var boundary = PreserveBoundary ? FindBoundaryPoints(input) : null;
			var iterations = Mathf.Max(1, Iterations);

			for (var iteration = 0; iteration < iterations; iteration++)
			{
				var relaxed = new Vector3[input.PointCount];

				for (var i = 0; i < input.PointCount; i++)
				{
					var list = neighbors[i];

					if (list == null || list.Count == 0 || (boundary != null && boundary.Contains(i)))
					{
						relaxed[i] = input.Points[i];
						continue;
					}

					var average = Vector3.zero;
					foreach (var neighbor in list)
						average += input.Points[neighbor];

					relaxed[i] = Vector3.Lerp(input.Points[i], average / list.Count, Factor);
				}

				for (var i = 0; i < input.PointCount; i++)
					input.Points[i] = relaxed[i];
			}

			return input;
		}

		// Neighbors via each primitive's own edges (consecutive index pairs), not position - a generator like
		// BoxGeneratorNode gives every face its own separate index at a hard edge, and this keeps those from
		// getting pulled toward an unrelated face the way position-based adjacency would.
		static List<int>[] BuildNeighbors(GeoData data)
		{
			var neighbors = new List<int>[data.PointCount];

			foreach (var primitive in data.Primitives)
			{
				var count = primitive.Length;
				for (var i = 0; i < count; i++)
				{
					var a = primitive[i];
					var b = primitive[(i + 1) % count];

					Add(neighbors, a, b);
					Add(neighbors, b, a);
				}
			}

			return neighbors;
		}

		static void Add(List<int>[] neighbors, int from, int to)
		{
			neighbors[from] ??= new List<int>();
			if (!neighbors[from].Contains(to))
				neighbors[from].Add(to);
		}

		// Boundary = a directed edge no primitive walks in reverse - same definition ExtrudeNode uses for its own
		// boundary walls, see there for why that reliably means "outer edge of the shape" here too.
		static HashSet<int> FindBoundaryPoints(GeoData data)
		{
			var edgeOccurrences = new HashSet<(int from, int to)>();

			foreach (var primitive in data.Primitives)
			{
				var count = primitive.Length;
				for (var i = 0; i < count; i++)
					edgeOccurrences.Add((primitive[i], primitive[(i + 1) % count]));
			}

			var boundary = new HashSet<int>();

			foreach (var edge in edgeOccurrences)
				if (!edgeOccurrences.Contains((edge.to, edge.from)))
				{
					boundary.Add(edge.from);
					boundary.Add(edge.to);
				}

			return boundary;
		}
	}
}
