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
	/// <summary> Curves the input into an arc of total Angle over its own extent along Axis - a straight tube into
	/// a pipe bend, a flat grid into a half-pipe. Bends into the next axis in the X -> Y -> Z -> X cycle (Y bends
	/// into X, X into Y, Z into X), leaving the third axis untouched; Center shifts the bend's own origin. Runs
	/// entirely in the NodraCore native library (see Native/NodraCore/Bend.cs) - there's no managed fallback,
	/// same as DecimateNode's optional package: without it, Process() passes geometry through unchanged and
	/// Warning explains why. </summary>
	[Serializable]
	public class BendNode : GeoNode
	{
		public override string Category => "Deform";

		public Axis3D Axis = Axis3D.Y;
		public Vector3 Center;
		[Range(-360f, 360f)] public float Angle = 90f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of bending.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			var bent = NodraNative.Bend(input, (int) Axis, Center, Angle);

			input.Points.Clear();
			input.Points.AddRange(bent);

			return input;
		}
	}
}
