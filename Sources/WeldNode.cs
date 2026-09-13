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
	/// <summary> Merges points within Distance of each other into one, transitively (a chain of near points all
	/// end up in the same cluster even if its two ends are farther apart than Distance), averaging position/
	/// normal/UV/color and remapping every primitive to the merged index. Cleans up the duplicate points a
	/// BooleanNode/MergeNode/CopyToPointsNode seam typically leaves behind. </summary>
	[Serializable]
	public class WeldNode : GeoNode
	{
		public override string Category => "Cleanup";

		[Min(0f)] public float Distance = 0.0001f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0)
				return input;

			var parent = new int[input.PointCount];
			for (var i = 0; i < parent.Length; i++)
				parent[i] = i;

			var cellSize = Mathf.Max(Distance, 0.0001f);
			var buckets = BuildBuckets(input, cellSize);
			UnionNearbyPoints(input, buckets, Distance, parent);

			var remap = BuildMergedPoints(input, parent, out var mergedPoints, out var mergedNormals, out var mergedUvs, out var mergedColors);
			var mergedPrimitives = RemapPrimitives(input, remap);

			input.Points.Clear();
			input.Points.AddRange(mergedPoints);
			input.Normals.Clear();
			input.Normals.AddRange(mergedNormals);
			input.Uvs.Clear();
			input.Uvs.AddRange(mergedUvs);
			input.Colors.Clear();
			input.Colors.AddRange(mergedColors);
			input.Primitives.Clear();
			input.Primitives.AddRange(mergedPrimitives);

			return input;
		}

		// Bucketed by cell size Distance, so only points that could plausibly be within Distance of each other
		// (their own cell or one of its 26 neighbors) are ever compared - avoids an all-pairs O(n^2) scan.
		static Dictionary<(int x, int y, int z), List<int>> BuildBuckets(GeoData data, float cellSize)
		{
			var buckets = new Dictionary<(int, int, int), List<int>>();

			for (var i = 0; i < data.PointCount; i++)
			{
				var cell = Cell(data.Points[i], cellSize);
				if (!buckets.TryGetValue(cell, out var members))
					buckets[cell] = members = new List<int>();

				members.Add(i);
			}

			return buckets;
		}

		// Every pair is checked exactly once: when point i's neighborhood scan reaches a member j > i, i is
		// always the smaller of the two, so the same unordered pair is never re-checked from j's own scan.
		static void UnionNearbyPoints(GeoData data, Dictionary<(int x, int y, int z), List<int>> buckets, float distance, int[] parent)
		{
			for (var i = 0; i < data.PointCount; i++)
			{
				var cell = Cell(data.Points[i], Mathf.Max(distance, 0.0001f));

				for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
				for (var dz = -1; dz <= 1; dz++)
				{
					if (!buckets.TryGetValue((cell.x + dx, cell.y + dy, cell.z + dz), out var members))
						continue;

					foreach (var j in members)
						if (j > i && Vector3.Distance(data.Points[i], data.Points[j]) <= distance)
							Union(parent, i, j);
				}
			}
		}

		static int[] BuildMergedPoints(GeoData data, int[] parent, out List<Vector3> points, out List<Vector3> normals, out List<Vector2> uvs, out List<Color> colors)
		{
			var positionSum = new Dictionary<int, Vector3>();
			var normalSum = new Dictionary<int, Vector3>();
			var uvSum = new Dictionary<int, Vector2>();
			var colorSum = new Dictionary<int, Color>();
			var clusterCount = new Dictionary<int, int>();

			for (var i = 0; i < data.PointCount; i++)
			{
				var root = Find(parent, i);

				positionSum[root] = positionSum.TryGetValue(root, out var p) ? p + data.Points[i] : data.Points[i];
				normalSum[root] = normalSum.TryGetValue(root, out var n) ? n + data.Normals[i] : data.Normals[i];
				uvSum[root] = uvSum.TryGetValue(root, out var uv) ? uv + data.Uvs[i] : data.Uvs[i];
				colorSum[root] = colorSum.TryGetValue(root, out var c) ? c + data.Colors[i] : data.Colors[i];
				clusterCount[root] = clusterCount.TryGetValue(root, out var count) ? count + 1 : 1;
			}

			points = new List<Vector3>();
			normals = new List<Vector3>();
			uvs = new List<Vector2>();
			colors = new List<Color>();

			var clusterIndex = new Dictionary<int, int>();
			var remap = new int[data.PointCount];

			for (var i = 0; i < data.PointCount; i++)
			{
				var root = Find(parent, i);

				if (clusterIndex.TryGetValue(root, out var newIndex))
				{
					remap[i] = newIndex;
					continue;
				}

				var count = clusterCount[root];
				points.Add(positionSum[root] / count);
				normals.Add((normalSum[root] / count).normalized);
				uvs.Add(uvSum[root] / count);
				colors.Add(colorSum[root] / count);

				newIndex = points.Count - 1;
				clusterIndex[root] = newIndex;
				remap[i] = newIndex;
			}

			return remap;
		}

		static List<int[]> RemapPrimitives(GeoData data, int[] remap)
		{
			var remapped = new List<int[]>();

			foreach (var primitive in data.Primitives)
			{
				var indices = new int[primitive.Length];
				for (var i = 0; i < primitive.Length; i++)
					indices[i] = remap[primitive[i]];

				// A primitive with two or more corners welded into the same point contributes nothing but a
				// zero-area sliver - dropped rather than baked into the mesh.
				if (!HasDuplicateIndex(indices))
					remapped.Add(indices);
			}

			return remapped;
		}

		static bool HasDuplicateIndex(int[] indices)
		{
			for (var i = 0; i < indices.Length; i++)
			for (var j = i + 1; j < indices.Length; j++)
				if (indices[i] == indices[j])
					return true;

			return false;
		}

		static (int x, int y, int z) Cell(Vector3 position, float cellSize) =>
			(Mathf.FloorToInt(position.x / cellSize), Mathf.FloorToInt(position.y / cellSize), Mathf.FloorToInt(position.z / cellSize));

		static int Find(int[] parent, int i)
		{
			while (parent[i] != i)
			{
				parent[i] = parent[parent[i]];
				i = parent[i];
			}

			return i;
		}

		static void Union(int[] parent, int a, int b)
		{
			var rootA = Find(parent, a);
			var rootB = Find(parent, b);

			if (rootA != rootB)
				parent[rootA] = rootB;
		}
	}
}
