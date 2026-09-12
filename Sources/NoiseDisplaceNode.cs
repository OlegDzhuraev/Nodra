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
	/// <summary> Displaces every point by 2D Perlin noise sampled from its XZ position. GeoMeshBuilder
	/// recalculates normals after baking, so displacement doesn't
	/// need to keep normals in sync itself. </summary>
	[Serializable]
	public class NoiseDisplaceNode : GeoNode
	{
		public float Amplitude = 1f;
		public float Frequency = 0.2f;
		public Vector2 Offset;
		public bool AlongNormal = true;

		public override GeoData Process(GeoData input)
		{
			if (input == null)
				return null;

			for (var i = 0; i < input.PointCount; i++)
			{
				var point = input.Points[i];
				var sample = Mathf.PerlinNoise(point.x * Frequency + Offset.x, point.z * Frequency + Offset.y) * 2f - 1f;
				var direction = AlongNormal ? input.Normals[i] : Vector3.up;

				input.Points[i] = point + direction * (sample * Amplitude);
			}

			return input;
		}
	}
}
