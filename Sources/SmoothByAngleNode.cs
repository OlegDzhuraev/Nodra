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
	/// <summary> Recomputes normals from face angles instead of trusting whatever the input already carries: at
	/// every shared point, primitives whose face-normal angle is within AngleThreshold blend into one smooth
	/// normal, and the rest keep their own flat-faceted one - duplicating the point where a single original index
	/// would otherwise have to carry two different results. Every point also gets tagged with GeoData.
	/// SmoothGroupAttribute, so GeoMeshBuilder can keep a smoothed group's normals matching on the baked mesh too. </summary>
	[Serializable]
	public class SmoothByAngleNode : GeoNode
	{
		public override string Category => "Cleanup";

		[Range(0f, 180f)] public float AngleThreshold = 60f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			var faceNormals = new Vector3[input.Primitives.Count];
			for (var p = 0; p < input.Primitives.Count; p++)
				faceNormals[p] = ComputeFaceNormal(input, input.Primitives[p]);

			// Grouped by position, not point index - a generator like BoxGeneratorNode gives every face its own
			// separate index even where they meet, so index alone would never see them as sharing a point.
			var positionGroups = new Dictionary<PositionKey, List<(int primitive, int slot)>>();

			for (var p = 0; p < input.Primitives.Count; p++)
			{
				var primitive = input.Primitives[p];
				for (var slot = 0; slot < primitive.Length; slot++)
				{
					var key = PositionKey.Of(input.Points[primitive[slot]]);
					if (!positionGroups.TryGetValue(key, out var corners))
						positionGroups[key] = corners = new List<(int, int)>();

					corners.Add((p, slot));
				}
			}

			var cosThreshold = Mathf.Cos(AngleThreshold * Mathf.Deg2Rad);

			foreach (var corners in positionGroups.Values)
				ApplyGroup(input, corners, faceNormals, cosThreshold);

			return input;
		}

		// Corners sharing a position are clustered by union-find over the AngleThreshold test between their
		// faces' normals - transitively, so a chain of gently-angled faces around a rounded corner still ends up
		// smooth end to end even where the two ends of the chain wouldn't pass the test directly against each
		// other. Each cluster shares one averaged normal; a corner whose original index is already claimed by a
		// different cluster gets a duplicated point instead of overwriting that other cluster's result.
		static void ApplyGroup(GeoData data, List<(int primitive, int slot)> corners, Vector3[] faceNormals, float cosThreshold)
		{
			var parent = new int[corners.Count];
			for (var i = 0; i < parent.Length; i++)
				parent[i] = i;

			for (var i = 0; i < corners.Count; i++)
			for (var j = i + 1; j < corners.Count; j++)
				if (Vector3.Dot(faceNormals[corners[i].primitive], faceNormals[corners[j].primitive]) >= cosThreshold)
					Union(parent, i, j);

			// Each cluster also gets a stable id for the SmoothGroup attribute below - the original index of
			// whichever corner reaches that root first, which is always a real, globally unique point index.
			var clusterNormals = new Dictionary<int, Vector3>();
			var clusterIds = new Dictionary<int, int>();

			for (var i = 0; i < corners.Count; i++)
			{
				var root = Find(parent, i);
				var faceNormal = faceNormals[corners[i].primitive];
				clusterNormals[root] = clusterNormals.TryGetValue(root, out var sum) ? sum + faceNormal : faceNormal;

				if (!clusterIds.ContainsKey(root))
				{
					var (primitiveIndex, slot) = corners[i];
					clusterIds[root] = data.Primitives[primitiveIndex][slot];
				}
			}

			var claimedBy = new Dictionary<int, int>(); // original point index -> cluster root that owns it

			for (var i = 0; i < corners.Count; i++)
			{
				var (primitiveIndex, slot) = corners[i];
				var primitive = data.Primitives[primitiveIndex];
				var originalIndex = primitive[slot];
				var root = Find(parent, i);
				var normal = clusterNormals[root].normalized;
				// SmoothGroupAttribute is 1-based (0 stays "untagged") since GeoData's sparse attribute storage
				// can't otherwise tell an explicit 0 apart from a point nobody ever tagged.
				var group = clusterIds[root] + 1f;
				int finalIndex;

				if (!claimedBy.TryGetValue(originalIndex, out var owner))
				{
					claimedBy[originalIndex] = root;
					data.Normals[originalIndex] = normal;
					finalIndex = originalIndex;
				}
				else if (owner == root)
				{
					data.Normals[originalIndex] = normal;
					finalIndex = originalIndex;
				}
				else
				{
					finalIndex = data.AddPoint(data.Points[originalIndex], normal, data.Uvs[originalIndex]);
					primitive[slot] = finalIndex;
				}

				data.SetAttribute(GeoData.SmoothGroupAttribute, finalIndex, group);
			}
		}

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

		/// <summary> Newell's method, matching ComputeFaceNormal in ExtrudeNode/ChamferNode/AutoUVNode. </summary>
		static Vector3 ComputeFaceNormal(GeoData data, int[] primitive)
		{
			var normal = Vector3.zero;
			var count = primitive.Length;

			for (var i = 0; i < count; i++)
			{
				var current = data.Points[primitive[i]];
				var next = data.Points[primitive[(i + 1) % count]];

				normal.x += (current.y - next.y) * (current.z + next.z);
				normal.y += (current.z - next.z) * (current.x + next.x);
				normal.z += (current.x - next.x) * (current.y + next.y);
			}

			return normal.normalized;
		}

		// A position rounded to a fixed grid, used as a Dictionary key so two corners at the exact same place in
		// space are recognized as sharing a point even when they reference different indices - see ChamferNode.
		readonly struct PositionKey : IEquatable<PositionKey>
		{
			const float Scale = 100000f;

			readonly long x, y, z;

			PositionKey(long x, long y, long z)
			{
				this.x = x;
				this.y = y;
				this.z = z;
			}

			public static PositionKey Of(Vector3 position) =>
				new (Quantize(position.x), Quantize(position.y), Quantize(position.z));

			static long Quantize(float value) => (long) Mathf.Round(value * Scale);

			public bool Equals(PositionKey other) => x == other.x && y == other.y && z == other.z;
			public override bool Equals(object obj) => obj is PositionKey other && Equals(other);
			public override int GetHashCode() => HashCode.Combine(x, y, z);
		}
	}
}
