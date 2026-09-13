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
	/// world point the perpendicular scaling is measured from. </summary>
	[Serializable]
	public class TaperNode : GeoNode
	{
		public override string Category => "Deform";

		public Axis3D Axis = Axis3D.Y;
		public Vector3 Center;
		public float Factor = 0.5f;

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

			for (var i = 0; i < input.PointCount; i++)
			{
				var local = input.Points[i] - Center;
				var t = (Get(local, Axis) - min) / range;
				var scale = Mathf.Lerp(1f, Factor, t);

				input.Points[i] = Center + ScalePerpendicular(local, Axis, scale);
			}

			return input;
		}

		static float Get(Vector3 v, Axis3D axis) => axis switch { Axis3D.X => v.x, Axis3D.Y => v.y, _ => v.z };

		static Vector3 ScalePerpendicular(Vector3 v, Axis3D axis, float scale) => axis switch
		{
			Axis3D.X => new Vector3(v.x, v.y * scale, v.z * scale),
			Axis3D.Y => new Vector3(v.x * scale, v.y, v.z * scale),
			_ => new Vector3(v.x * scale, v.y * scale, v.z),
		};
	}
}
