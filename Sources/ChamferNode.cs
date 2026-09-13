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
	/// <summary> Cuts a small flat facet at every shared edge of a connected surface, replacing the sharp corner
	/// between two faces with a new facet between them: each face gets its own independent copy of its points
	/// pulled toward its own centroid by Distance, a bridging facet fills the gap that opens up along every edge
	/// shared by two faces, and a fanned cap closes the smaller gap left at every original vertex where 3 or more
	/// faces meet. This is a simplified stand-in for a true topological bevel (no per-edge angle limiting, no
	/// mitered vertex offsets) - the one case it can't close is an edge with only one face touching it (an open
	/// boundary, e.g. a bare GridGeneratorNode), which has no second face to bevel against and is left as a gap. </summary>
	[Serializable]
	public class ChamferNode : GeoNode
	{
		[Min(0f)] public float Distance = 0.05f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			// Snapshotted before any of the loops below - primitives/points added while chamfering must not
			// themselves be treated as part of the shape being chamfered (mirrors ExtrudeNode's own approach).
			var sourcePrimitives = new List<int[]>(input.Primitives);
			input.Primitives.Clear();

			var insetIndices = new int[sourcePrimitives.Count][];

			for (var p = 0; p < sourcePrimitives.Count; p++)
				insetIndices[p] = BuildInsetFace(input, sourcePrimitives[p], Distance);

			// Maps a directed edge to whichever (primitive, slot) it came from, so the same edge as seen by
			// whatever face is on the other side of it - stored as its reverse direction - can be looked up
			// directly instead of searching for it. Keyed by each endpoint's *position* (quantized - see
			// PositionKey), not by original point index: a generator like BoxGeneratorNode deliberately gives every
			// face its own separate points for hard shading, so two faces meeting at the same edge don't actually
			// share any indices there, only positions.
			var edgeOwners = new Dictionary<(PositionKey from, PositionKey to), (int primitive, int slot)>();

			for (var p = 0; p < sourcePrimitives.Count; p++)
			{
				var primitive = sourcePrimitives[p];
				for (var slot = 0; slot < primitive.Length; slot++)
				{
					var from = PositionKey.Of(input.Points[primitive[slot]]);
					var to = PositionKey.Of(input.Points[primitive[(slot + 1) % primitive.Length]]);
					edgeOwners[(from, to)] = (p, slot);
				}
			}

			var visited = new HashSet<(PositionKey from, PositionKey to)>();

			foreach (var (edge, owner) in edgeOwners)
			{
				if (!visited.Add(edge))
					continue; // already bridged from the other face's side

				var reverse = (edge.to, edge.from);
				if (!edgeOwners.TryGetValue(reverse, out var otherOwner))
					continue; // boundary edge - nothing on the other side to bridge to

				visited.Add(reverse);
				AddBridgeFacet(input, sourcePrimitives, insetIndices, owner, otherOwner);
			}

			// Closes the small gap the edge bridges above leave at every original vertex where 3 or more faces
			// meet (most corners): fans a cap across each face's own inset copy of that vertex, walked in the
			// cyclic order the faces actually meet in via the same edge-adjacency map. A vertex with only 1 or 2
			// faces around it needs no cap - either it's an open boundary the walk can't close, or exactly 2 faces
			// already meet edge-to-edge there with nothing left to fill.
			var cappedOccurrences = new HashSet<(int primitive, int slot)>();

			foreach (var occurrence in edgeOwners.Values)
			{
				if (!cappedOccurrences.Add(occurrence))
					continue;

				var fan = WalkVertexFan(input, sourcePrimitives, edgeOwners, occurrence);
				foreach (var visitedOccurrence in fan)
					cappedOccurrences.Add(visitedOccurrence);

				if (fan.Count < 3)
					continue;

				// The walk comes out facing into the solid - see AddBridgeFacet's own winding note, same idea here.
				fan.Reverse();

				var capIndices = new int[fan.Count];
				for (var i = 0; i < fan.Count; i++)
					capIndices[i] = insetIndices[fan[i].primitive][fan[i].slot];

				input.AddPrimitive(capIndices);
			}

			foreach (var indices in insetIndices)
				input.AddPrimitive(indices);

			return input;
		}

		// Every vertex of the face gets its own copy, pulled toward the face's own centroid - clamped so it can
		// never cross over the centroid (which would invert the face) even for a very large Distance.
		static int[] BuildInsetFace(GeoData data, int[] primitive, float distance)
		{
			var centroid = Vector3.zero;
			foreach (var index in primitive)
				centroid += data.Points[index];
			centroid /= primitive.Length;

			var insetIndices = new int[primitive.Length];

			for (var slot = 0; slot < primitive.Length; slot++)
			{
				var originalIndex = primitive[slot];
				var toCentroid = centroid - data.Points[originalIndex];
				var moveDistance = Mathf.Min(distance, toCentroid.magnitude * 0.99f);
				var position = toCentroid.sqrMagnitude > 0f
					? data.Points[originalIndex] + toCentroid.normalized * moveDistance
					: data.Points[originalIndex];

				insetIndices[slot] = data.AddPoint(position, data.Normals[originalIndex], data.Uvs[originalIndex]);
			}

			return insetIndices;
		}

		// Winding verified by hand against a concrete box edge (top face meeting the +X face): the two faces'
		// "near" inset corners - fromA and toB, both close to the same original position - need to sit next to
		// each other in the loop, not the two corners belonging to the same face, for the facet to end up facing
		// outward instead of into the solid.
		static void AddBridgeFacet(GeoData data, List<int[]> sourcePrimitives, int[][] insetIndices,
			(int primitive, int slot) ownerA, (int primitive, int slot) ownerB)
		{
			var lengthA = sourcePrimitives[ownerA.primitive].Length;
			var lengthB = sourcePrimitives[ownerB.primitive].Length;

			var insetFromA = insetIndices[ownerA.primitive][ownerA.slot];
			var insetToA = insetIndices[ownerA.primitive][(ownerA.slot + 1) % lengthA];
			var insetFromB = insetIndices[ownerB.primitive][ownerB.slot];
			var insetToB = insetIndices[ownerB.primitive][(ownerB.slot + 1) % lengthB];

			data.AddPrimitive(insetFromA, insetToB, insetFromB, insetToA);
		}

		// Starting from one face's occurrence of a vertex, walks around it face-by-face via edge pairing - each
		// step follows this face's outgoing edge from the vertex to whichever face owns that edge's reverse, then
		// lands on THAT face's own occurrence of the same vertex (one slot past where its matching edge starts) -
		// until it's all the way back to the start, meaning every face around this vertex has been visited once.
		// Stops early (an unclosed fan) if that outgoing edge has no reverse owner at all - an open boundary the
		// walk can't continue past - or after a full lap without closing, which only a non-manifold input could do.
		static List<(int primitive, int slot)> WalkVertexFan(GeoData data, List<int[]> sourcePrimitives,
			Dictionary<(PositionKey from, PositionKey to), (int primitive, int slot)> edgeOwners, (int primitive, int slot) start)
		{
			var fan = new List<(int primitive, int slot)> { start };
			var current = start;

			for (var step = 0; step < sourcePrimitives.Count; step++)
			{
				var primitive = sourcePrimitives[current.primitive];
				var fromPos = PositionKey.Of(data.Points[primitive[current.slot]]);
				var toPos = PositionKey.Of(data.Points[primitive[(current.slot + 1) % primitive.Length]]);

				if (!edgeOwners.TryGetValue((toPos, fromPos), out var next))
					return fan;

				var nextPrimitive = sourcePrimitives[next.primitive];
				var candidate = (next.primitive, (next.slot + 1) % nextPrimitive.Length);

				if (candidate == start)
					return fan;

				fan.Add(candidate);
				current = candidate;
			}

			return fan;
		}

		// A position rounded to a fixed grid, used as a Dictionary/HashSet key so two edges that sit at the exact
		// same place in space - but reference entirely different, unshared point indices, as every edge of a
		// BoxGeneratorNode does - are still recognized as "the same edge" for pairing two faces up.
		readonly struct PositionKey : IEquatable<PositionKey>
		{
			const float Scale = 100000f; // 1e-5 unit resolution - far tighter than any deliberate gap, loose enough for float noise

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
