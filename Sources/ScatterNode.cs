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
	/// <summary> Scatters a fixed number of points across the surface area of the input primitives, each carrying
	/// the interpolated normal of the triangle it landed on - and, via GeoData.BlendAttributesFrom, the same
	/// barycentric interpolation of whatever named attributes the input carries (a Density painted with
	/// SetAttributeNode, say, surviving onto the scattered points it influenced). The input's own points/
	/// primitives are discarded, since the result is meant to feed a following CopyToPointsNode, not to be built
	/// into a mesh directly. Runs entirely in the NodraCore native library (see Native/NodraCore/Scatter.cs) -
	/// there's no managed fallback, same as DecimateNode's optional package: without it, Process() produces no
	/// geometry at all and Warning explains why. </summary>
	[Serializable]
	public class ScatterNode : GeoNode
	{
		public override string Category => "Scatter/Copy";

		[Min(1)] public int PointCount = 100;
		public int RandomSeed;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry until it is.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || PointCount <= 0 || !NodraNative.IsAvailable)
				return new GeoData();

			return NodraNative.Scatter(input, PointCount, RandomSeed);
		}
	}
}
