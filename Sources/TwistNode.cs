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
	/// <summary> Rotates every point around Axis by an angle that grows from 0 at the Axis-min end of the input to
	/// Angle at the Axis-max end - a straight tube into a drill bit / rope twist. A pure per-point rotation, so
	/// normals rotate along with it instead of needing GeoMeshBuilder's recalculation to stay correct. Runs
	/// entirely in the NodraCore native library (see Native/NodraCore/Twist.cs) - there's no managed fallback,
	/// same as DecimateNode's optional package: without it, Process() passes geometry through unchanged and
	/// Warning explains why. </summary>
	[Serializable]
	public class TwistNode : GeoNode
	{
		public override string Category => "Deform";

		public Axis3D Axis = Axis3D.Y;
		public Vector3 Center;
		public float Angle = 180f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of twisting.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			var (points, normals) = NodraNative.Twist(input, (int) Axis, Center, Angle);

			input.Points.Clear();
			input.Points.AddRange(points);
			input.Normals.Clear();
			input.Normals.AddRange(normals);

			return input;
		}
	}
}
