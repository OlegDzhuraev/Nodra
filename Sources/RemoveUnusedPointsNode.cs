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

namespace Nodra
{
	/// <summary> Drops every point no primitive references - the cleanup pass FaceFilterNode and SliceNode both
	/// leave for later rather than doing themselves (they only ever discard whole primitives, so figuring out which
	/// points that orphans isn't extra work worth repeating in every node that drops primitives). Delegates the
	/// actual point removal to GeoData.CompactPoints, since it alone can keep named per-point attributes in step
	/// with the points that survive - this node's own job is just deciding which ones do (via NodraNative.
	/// RemoveUnusedPointsUsed - see Native/NodraCore/RemoveUnusedPoints.cs), then remapping every remaining
	/// primitive's own point indices to match. No managed fallback (same pattern as DecimateNode's optional
	/// package): without it, Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class RemoveUnusedPointsNode : GeoNode
	{
		public override string Category => "Cleanup";

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of removing unused points.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			var used = NodraNative.RemoveUnusedPointsUsed(input);
			var newIndex = input.CompactPoints(used);

			foreach (var primitive in input.Primitives)
				for (var i = 0; i < primitive.Length; i++)
					primitive[i] = newIndex[primitive[i]];

			return input;
		}
	}
}
