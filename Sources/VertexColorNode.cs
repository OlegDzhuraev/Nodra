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
	/// <summary> Colors every point - Flat mode fills one Color everywhere; Gradient mode evaluates a Gradient by
	/// Height (the point's own position along Axis, normalized across the input's extent) or Slope (Vector3.up
	/// . normal, remapped from [-1, 1] to [0, 1] - flat faces at the gradient's 1 end, vertical ones at 0). Needs a
	/// shader that reads vertex color to show up; the debug `Nodra/Checker` shader in Extras/ multiplies it in. </summary>
	[Serializable]
	public class VertexColorNode : GeoNode
	{
		public override string Category => "Color & UV";

		public enum ColorMode { Flat, Gradient }
		public enum GradientSource { Height, Slope }

		public ColorMode Mode = ColorMode.Flat;
		public Color Color = Color.white;

		public GradientSource Source = GradientSource.Height;
		public Axis3D Axis = Axis3D.Y;
		public Gradient Gradient = DefaultGradient();

		public override GeoData Process(GeoData input)
		{
			if (input == null)
				return null;

			switch (Mode)
			{
				case ColorMode.Gradient when Source == GradientSource.Slope:
					ApplySlope(input);
					break;
				case ColorMode.Gradient:
					ApplyHeight(input);
					break;
				default:
					ApplyFlat(input);
					break;
			}

			return input;
		}

		void ApplyFlat(GeoData data)
		{
			for (var i = 0; i < data.PointCount; i++)
				data.Colors[i] = Color;
		}

		void ApplySlope(GeoData data)
		{
			for (var i = 0; i < data.PointCount; i++)
			{
				var t = Vector3.Dot(data.Normals[i], Vector3.up) * 0.5f + 0.5f;
				data.Colors[i] = Gradient.Evaluate(t);
			}
		}

		void ApplyHeight(GeoData data)
		{
			var min = float.MaxValue;
			var max = float.MinValue;

			for (var i = 0; i < data.PointCount; i++)
			{
				var value = Get(data.Points[i], Axis);
				min = Mathf.Min(min, value);
				max = Mathf.Max(max, value);
			}

			var range = max - min;

			for (var i = 0; i < data.PointCount; i++)
			{
				var t = range > 0f ? (Get(data.Points[i], Axis) - min) / range : 0f;
				data.Colors[i] = Gradient.Evaluate(t);
			}
		}

		static float Get(Vector3 v, Axis3D axis) => axis switch { Axis3D.X => v.x, Axis3D.Y => v.y, _ => v.z };

		// A default Gradient field would otherwise start out fully black until someone opens the Inspector and
		// edits it - this gives Gradient mode a sane black-to-white look out of the box.
		static Gradient DefaultGradient()
		{
			var gradient = new Gradient();
			gradient.SetKeys(
				new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
				new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

			return gradient;
		}
	}
}
