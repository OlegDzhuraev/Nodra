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
	/// <summary> Extrudes all input primitives as a single connected shell, the way a whole connected surface
	/// should extrude together rather than each face separately. Each ORIGINAL point is offset once (along the
	/// average normal of the faces around it) and reused by every primitive/wall touching it, and side walls
	/// are only added along the outer boundary - edges used by exactly one primitive. A group of primitives
	/// that don't actually share points (e.g. BoxGeneratorNode's per-face verts) naturally has every edge count
	/// as "boundary", so each disconnected face still extrudes on its own - there's nothing connected to keep
	/// together. </summary>
	[Serializable]
	public class ExtrudeNode : GeoNode
	{
		public float Distance = 1f;
		public bool CapNewFace = true;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			// Snapshotted before any of the loops below - primitives/points added while extruding must not
			// themselves be considered part of the shape being extruded.
			var sourcePrimitives = new List<int[]>(input.Primitives);
			var originalPointCount = input.PointCount;

			var topIndices = BuildOffsetPoints(input, sourcePrimitives, originalPointCount);

			if (CapNewFace)
				foreach (var primitive in sourcePrimitives)
					input.AddPrimitive(Remap(primitive, topIndices));

			AddBoundaryWalls(input, sourcePrimitives, topIndices);

			return input;
		}

		int[] BuildOffsetPoints(GeoData data, List<int[]> primitives, int originalPointCount)
		{
			var pointNormals = new Vector3[originalPointCount];

			foreach (var primitive in primitives)
			{
				var faceNormal = ComputeFaceNormal(data, primitive);
				foreach (var pointIndex in primitive)
					pointNormals[pointIndex] += faceNormal;
			}

			var topIndices = new int[originalPointCount];

			for (var i = 0; i < originalPointCount; i++)
			{
				var normal = pointNormals[i].sqrMagnitude > 0f ? pointNormals[i].normalized : Vector3.up;
				topIndices[i] = data.AddPoint(data.Points[i] + normal * Distance, normal, data.Uvs[i]);
			}

			return topIndices;
		}

		// Boundary = a directed edge (a -> b) that no primitive in the group walks in reverse (b -> a). A shared
		// interior edge of a connected surface is always walked in both directions by the two primitives on either
		// side of it (verified against every generator's winding in this package), so it's the only reliable way
		// to tell "seam between two neighboring faces" apart from "outer edge of the whole shape" without any
		// extra topology bookkeeping.
		static void AddBoundaryWalls(GeoData data, List<int[]> primitives, int[] topIndices)
		{
			var edgeOccurrences = new Dictionary<(int from, int to), int>();

			foreach (var primitive in primitives)
			{
				var count = primitive.Length;
				for (var i = 0; i < count; i++)
				{
					var edge = (from: primitive[i], to: primitive[(i + 1) % count]);
					edgeOccurrences[edge] = edgeOccurrences.TryGetValue(edge, out var existing) ? existing + 1 : 1;
				}
			}

			foreach (var (edge, occurrences) in edgeOccurrences)
			{
				if (edgeOccurrences.ContainsKey((edge.to, edge.from)))
					continue;

				for (var i = 0; i < occurrences; i++)
					data.AddPrimitive(edge.from, edge.to, topIndices[edge.to], topIndices[edge.from]);
			}
		}

		static int[] Remap(int[] primitive, int[] topIndices)
		{
			var remapped = new int[primitive.Length];
			for (var i = 0; i < primitive.Length; i++)
				remapped[i] = topIndices[primitive[i]];

			return remapped;
		}

		/// <summary> Newell's method - robust for any planar polygon (unlike a 3-point cross product, which breaks
		/// down if the first three points happen to be collinear), and matches the winding convention every other
		/// generator node in this package already relies on. </summary>
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
	}
}
