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

namespace Nodra
{
	/// <summary> Fans an N-gon across every open boundary loop it finds - fills a hole punched through the middle
	/// of a patch, or the open edge ChamferNode/ExtrudeNode/BooleanNode leaves by design. Assumes each loop is a
	/// simple, non-self-touching cycle; a non-manifold boundary (two holes sharing one vertex) isn't handled
	/// specially - that vertex's two boundary edges just overwrite each other in the walk. </summary>
	[Serializable]
	public class CapHolesNode : GeoNode
	{
		public override string Category => "Build";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			var edgeOccurrences = new HashSet<(int from, int to)>();

			foreach (var primitive in input.Primitives)
			{
				var count = primitive.Length;
				for (var i = 0; i < count; i++)
					edgeOccurrences.Add((primitive[i], primitive[(i + 1) % count]));
			}

			var next = new Dictionary<int, int>();

			foreach (var edge in edgeOccurrences)
				if (!edgeOccurrences.Contains((edge.to, edge.from)))
					next[edge.from] = edge.to;

			var visited = new HashSet<int>();

			foreach (var start in next.Keys)
			{
				if (visited.Contains(start))
					continue;

				var loop = WalkLoop(next, start, visited);
				if (loop.Count < 3)
					continue;

				// The walk follows each edge exactly as the surrounding surface recorded it, which is the reverse
				// of the correct cap winding - verified by hand on a hole punched through the middle of a 3x3 grid.
				loop.Reverse();
				input.AddPrimitive(loop.ToArray());
			}

			return input;
		}

		static List<int> WalkLoop(Dictionary<int, int> next, int start, HashSet<int> visited)
		{
			var loop = new List<int> { start };
			visited.Add(start);
			var current = start;

			while (next.TryGetValue(current, out var nextPoint) && nextPoint != start)
			{
				if (!visited.Add(nextPoint))
					return new List<int>(); // hit an already-visited point without closing - not a simple loop

				loop.Add(nextPoint);
				current = nextPoint;
			}

			// Only a loop that actually made it back to start is a real hole boundary.
			return next.TryGetValue(current, out var closing) && closing == start ? loop : new List<int>();
		}
	}
}
