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
	/// <summary> Jitters every point's position (per axis, +/- PositionJitter) and, if AngleJitter > 0, tilts its
	/// normal by a random angle around a random axis - both seeded, so the same RandomSeed always reproduces the
	/// same jitter. Fills the one gap CopyToPointsNode's own jitter (RandomYRotation/UniformScaleRange) leaves:
	/// stamped copies always sit exactly on the scattered point and stay perfectly aligned to its normal (or exactly
	/// upright) - feeding this in between ScatterNode and CopyToPointsNode also scatters position and tilt, not just
	/// spin and scale. Works just as well on a full mesh's own vertices directly - independent per-point jitter with
	/// no spatial coherence (unlike NoiseDisplaceNode's Perlin/Voronoi) reads as a rougher, more "shattered" look.
	/// Runs entirely in the NodraCore native library (see Native/NodraCore/RandomTransform.cs) - there's no managed
	/// fallback, same as DecimateNode's optional package: without it, Process() passes geometry through unchanged
	/// and Warning explains why. </summary>
	[Serializable]
	public class RandomTransformNode : GeoNode
	{
		public override string Category => "Scatter/Copy";

		public Vector3 PositionJitter = Vector3.zero;
		[Range(0f, 180f)] public float AngleJitter;
		public int RandomSeed;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of jittering it.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || !NodraNative.IsAvailable)
				return input;

			var (points, normals) = NodraNative.RandomTransform(input, PositionJitter, AngleJitter, RandomSeed);

			input.Points.Clear();
			input.Points.AddRange(points);
			input.Normals.Clear();
			input.Normals.AddRange(normals);

			return input;
		}
	}
}
