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
	/// <summary> Splits every primitive into one quad per corner, fanned around its own centroid - the topological
	/// half of Catmull-Clark (no position averaging/smoothing, so it adds detail without rounding sharp shapes).
	/// Works on any polygon size, not just triangles or quads. An edge shared by index between two primitives gets
	/// one shared midpoint; an edge only shared by position (BoxGeneratorNode's per-face corners) gets two,
	/// keeping the hard edge intact through the split. </summary>
	[Serializable]
	public class SubdivideNode : GeoNode
	{
		public override string Category => "Build";

		[Range(1, 4)] public int Iterations = 1;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			var iterations = Mathf.Max(1, Iterations);
			for (var i = 0; i < iterations; i++)
				SubdivideOnce(input);

			return input;
		}

		// Winding verified by hand on a concrete quad: (corner, edgeMidpoint, centroid, previousEdgeMidpoint)
		// keeps the same outward Cross-product direction as the original primitive, for every one of its corners.
		static void SubdivideOnce(GeoData data)
		{
			var sourcePrimitives = new List<int[]>(data.Primitives);
			data.Primitives.Clear();

			var midpoints = new Dictionary<(int a, int b), int>();

			foreach (var primitive in sourcePrimitives)
			{
				var count = primitive.Length;
				var edgeMidpoints = new int[count];

				for (var i = 0; i < count; i++)
					edgeMidpoints[i] = Midpoint(data, midpoints, primitive[i], primitive[(i + 1) % count]);

				var centroid = Centroid(data, primitive);

				for (var i = 0; i < count; i++)
				{
					var previous = edgeMidpoints[(i - 1 + count) % count];
					data.AddPrimitive(primitive[i], edgeMidpoints[i], centroid, previous);
				}
			}
		}

		static int Midpoint(GeoData data, Dictionary<(int a, int b), int> cache, int a, int b)
		{
			var key = a < b ? (a, b) : (b, a);

			if (cache.TryGetValue(key, out var index))
				return index;

			var position = (data.Points[a] + data.Points[b]) * 0.5f;
			var normal = (data.Normals[a] + data.Normals[b]).normalized;
			var uv = (data.Uvs[a] + data.Uvs[b]) * 0.5f;
			var color = (data.Colors[a] + data.Colors[b]) * 0.5f;

			index = data.AddPoint(position, normal, uv, color);
			cache[key] = index;

			return index;
		}

		static int Centroid(GeoData data, int[] primitive)
		{
			var position = Vector3.zero;
			var normal = Vector3.zero;
			var uv = Vector2.zero;
			var color = Color.clear;

			foreach (var index in primitive)
			{
				position += data.Points[index];
				normal += data.Normals[index];
				uv += data.Uvs[index];
				color += data.Colors[index];
			}

			var count = primitive.Length;
			return data.AddPoint(position / count, normal.normalized, uv / count, color / count);
		}
	}
}
