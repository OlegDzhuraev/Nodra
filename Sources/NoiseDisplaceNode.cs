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
	/// <summary> Displaces every point by 2D noise sampled from its XZ position - Perlin for smooth rolling bumps,
	/// Voronoi for cellular/rocky facets. GeoMeshBuilder recalculates normals after baking, so displacement
	/// doesn't need to keep normals in sync itself. </summary>
	[Serializable]
	public class NoiseDisplaceNode : GeoNode
	{
		public override string Category => "Deform";

		public enum NoiseType { Perlin, Voronoi }

		public NoiseType Type = NoiseType.Perlin;
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
				var x = point.x * Frequency + Offset.x;
				var z = point.z * Frequency + Offset.y;
				var sample = (Type == NoiseType.Voronoi ? VoronoiNoise(x, z) : Mathf.PerlinNoise(x, z)) * 2f - 1f;
				var direction = AlongNormal ? input.Normals[i] : Vector3.up;

				input.Points[i] = point + direction * (sample * Amplitude);
			}

			return input;
		}

		// Cellular/Worley noise: distance from (x, z) to the nearest of one pseudo-random "feature point" per grid
		// cell, searched across the 3x3 neighborhood so a point near a cell edge still finds the true nearest one.
		// Clamped to [0, 1] - the distance to the nearest feature point rarely exceeds that in practice.
		static float VoronoiNoise(float x, float z)
		{
			var cellX = Mathf.FloorToInt(x);
			var cellZ = Mathf.FloorToInt(z);
			var localX = x - cellX;
			var localZ = z - cellZ;
			var minDistance = float.MaxValue;

			for (var dz = -1; dz <= 1; dz++)
			for (var dx = -1; dx <= 1; dx++)
			{
				var feature = Hash(cellX + dx, cellZ + dz);
				var offset = new Vector2(dx + feature.x - localX, dz + feature.y - localZ);
				minDistance = Mathf.Min(minDistance, offset.magnitude);
			}

			return Mathf.Clamp01(minDistance);
		}

		// Deterministic pseudo-random point within a grid cell - the standard sin/fract hash trick: fast, stable
		// for any integer cell coordinate, not meant to be cryptographically uniform.
		static Vector2 Hash(int cellX, int cellZ)
		{
			var x = Mathf.Sin(cellX * 127.1f + cellZ * 311.7f) * 43758.5453f;
			var y = Mathf.Sin(cellX * 269.5f + cellZ * 183.3f) * 43758.5453f;

			return new Vector2(x - Mathf.Floor(x), y - Mathf.Floor(y));
		}
	}
}
