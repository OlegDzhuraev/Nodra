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
	/// normals rotate along with it instead of needing GeoMeshBuilder's recalculation to stay correct. </summary>
	[Serializable]
	public class TwistNode : GeoNode
	{
		public override string Category => "Deform";

		public Axis3D Axis = Axis3D.Y;
		public Vector3 Center;
		public float Angle = 180f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0)
				return input;

			var min = float.MaxValue;
			var max = float.MinValue;

			for (var i = 0; i < input.PointCount; i++)
			{
				var value = Get(input.Points[i] - Center, Axis);
				min = Mathf.Min(min, value);
				max = Mathf.Max(max, value);
			}

			var range = max - min;
			if (range <= 0f)
				return input;

			var axisDirection = Direction(Axis);

			for (var i = 0; i < input.PointCount; i++)
			{
				var local = input.Points[i] - Center;
				var t = (Get(local, Axis) - min) / range;
				var rotation = Quaternion.AngleAxis(t * Angle, axisDirection);

				input.Points[i] = Center + rotation * local;
				input.Normals[i] = rotation * input.Normals[i];
			}

			return input;
		}

		static float Get(Vector3 v, Axis3D axis) => axis switch { Axis3D.X => v.x, Axis3D.Y => v.y, _ => v.z };

		static Vector3 Direction(Axis3D axis) => axis switch
		{
			Axis3D.X => Vector3.right,
			Axis3D.Y => Vector3.up,
			_ => Vector3.forward,
		};
	}
}
