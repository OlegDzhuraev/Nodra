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
	/// <summary> Translates, rotates and scales every point of the input geometry. </summary>
	[Serializable]
	public class TransformNode : GeoNode
	{
		public Vector3 Translation;
		public Vector3 Rotation;
		public Vector3 Scale = Vector3.one;

		public override GeoData Process(GeoData input)
		{
			if (input == null)
				return null;

			var rotation = Quaternion.Euler(Rotation);
			var matrix = Matrix4x4.TRS(Translation, rotation, Scale);

			for (var i = 0; i < input.PointCount; i++)
			{
				input.Points[i] = matrix.MultiplyPoint3x4(input.Points[i]);
				input.Normals[i] = rotation * input.Normals[i];
			}

			return input;
		}
	}
}
