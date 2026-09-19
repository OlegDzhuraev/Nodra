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
	/// <summary> Merges points within Distance of each other into one, transitively (a chain of near points all
	/// end up in the same cluster even if its two ends are farther apart than Distance), averaging position/
	/// normal/UV/color and remapping every primitive to the merged index. Cleans up the duplicate points a
	/// BooleanNode/MergeNode/CopyToPointsNode seam typically leaves behind. Runs entirely in the NodraCore native
	/// library (see Native/NodraCore/Weld.cs) - there's no managed fallback, same as DecimateNode's optional
	/// package: without it, Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class WeldNode : GeoNode
	{
		public override string Category => "Cleanup";

		[Min(0f)] public float Distance = 0.0001f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of welding.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			var native = NodraNative.Weld(input, Distance);

			input.RemapAttributes(native.Remap, native.Points.Count);

			input.Points.Clear();
			input.Points.AddRange(native.Points);
			input.Normals.Clear();
			input.Normals.AddRange(native.Normals);
			input.Uvs.Clear();
			input.Uvs.AddRange(native.Uvs);
			input.Colors.Clear();
			input.Colors.AddRange(native.Colors);
			input.Primitives.Clear();
			input.Primitives.AddRange(native.Primitives);

			return input;
		}
	}
}
