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
using UnityEngine;

namespace Nodra
{
	/// <summary> Cuts a small flat facet at every shared edge of a connected surface, replacing the sharp corner
	/// between two faces with a new facet between them: each face gets its own independent copy of its points
	/// pulled toward its own centroid by Distance, a bridging facet fills the gap that opens up along every edge
	/// shared by two faces, and a fanned cap closes the smaller gap left at every original vertex where 3 or more
	/// faces meet. This is a simplified stand-in for a true topological bevel (no per-edge angle limiting, no
	/// mitered vertex offsets) - the one case it can't close is an edge with only one face touching it (an open
	/// boundary, e.g. a bare GridGeneratorNode), which has no second face to bevel against and is left as a gap.
	/// Runs entirely in the NodraCore native library (see Native/NodraCore/Chamfer.cs) - there's no managed
	/// fallback, same as DecimateNode's optional package: without it, Process() passes geometry through unchanged
	/// and Warning explains why. </summary>
	[Serializable]
	public class ChamferNode : GeoNode
	{
		public override string Category => "Build";

		[Min(0f)] public float Distance = 0.05f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of chamfering.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.Chamfer(input, Distance);

			return input;
		}
	}
}
