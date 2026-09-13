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
	/// into X, X into Y, Z into X), leaving the third axis untouched; Center shifts the bend's own origin. </summary>
	[Serializable]
	public class BendNode : GeoNode
	{
		public override string Category => "Deform";

		public Axis3D Axis = Axis3D.Y;
		public Vector3 Center;
		[Range(-360f, 360f)] public float Angle = 90f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0)
				return input;

			GetAxes(Axis, out var along, out var bend, out var flat);

			var min = float.MaxValue;
			var max = float.MinValue;

			for (var i = 0; i < input.PointCount; i++)
			{
				var value = Get(input.Points[i] - Center, along);
				min = Mathf.Min(min, value);
				max = Mathf.Max(max, value);
			}

			var range = max - min;
			var angleTotal = Angle * Mathf.Deg2Rad;

			if (range <= 0f || Mathf.Abs(angleTotal) < 0.0001f)
				return input;

			// The whole extent maps to an arc of this radius, so the arc's own length matches the original,
			// unbent extent - verified by hand: at t=0 a point on the axis line stays exactly where it was, and
			// bending a straight line by a right angle lands its far end at (radius, radius), as a quarter-circle
			// arc should.
			var radius = range / angleTotal;

			for (var i = 0; i < input.PointCount; i++)
			{
				var local = input.Points[i] - Center;
				var t = (Get(local, along) - min) / range;
				var theta = t * angleTotal;
				var localRadius = radius - Get(local, bend);

				var newAlong = min + localRadius * Mathf.Sin(theta);
				var newBend = radius - localRadius * Mathf.Cos(theta);

				var result = Vector3.zero;
				result = Set(result, along, newAlong);
				result = Set(result, bend, newBend);
				result = Set(result, flat, Get(local, flat));

				input.Points[i] = Center + result;
			}

			return input;
		}

		static void GetAxes(Axis3D axis, out Axis3D along, out Axis3D bend, out Axis3D flat)
		{
			switch (axis)
			{
				case Axis3D.X: along = Axis3D.X; bend = Axis3D.Y; flat = Axis3D.Z; break;
				case Axis3D.Y: along = Axis3D.Y; bend = Axis3D.X; flat = Axis3D.Z; break;
				default: along = Axis3D.Z; bend = Axis3D.X; flat = Axis3D.Y; break;
			}
		}

		static float Get(Vector3 v, Axis3D axis) => axis switch { Axis3D.X => v.x, Axis3D.Y => v.y, _ => v.z };

		static Vector3 Set(Vector3 v, Axis3D axis, float value)
		{
			switch (axis)
			{
				case Axis3D.X: v.x = value; break;
				case Axis3D.Y: v.y = value; break;
				default: v.z = value; break;
			}

			return v;
		}
	}
}
