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
	/// <summary> Scales every point's cross-section perpendicular to Axis, from full size at the Axis-min end of
	/// the input to Factor at the Axis-max end - a cone from a cylinder, a spike from a tube. Center shifts which
	/// world point the perpendicular scaling is measured from. Runs entirely in the NodraCore native library (see
	/// Native/NodraCore/Taper.cs) - there's no managed fallback, same as DecimateNode's optional package: without
	/// it, Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class TaperNode : GeoNode
	{
		public override string Category => "Deform";

		public Axis3D Axis = Axis3D.Y;
		public Vector3 Center;
		public float Factor = 0.5f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of tapering.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			var tapered = NodraNative.Taper(input, (int) Axis, Center, Factor);

			input.Points.Clear();
			input.Points.AddRange(tapered);

			return input;
		}
	}
}
