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
using System.Collections.Generic;
using UnityEngine;

namespace Nodra
{
	/// <summary> Scatters a fixed number of points across the surface area of the input primitives, each carrying
	/// the interpolated normal of the triangle it landed on. The input's own points/primitives are discarded,
	/// since the result is meant to feed a following CopyToPointsNode, not to be built into a mesh directly. </summary>
	[Serializable]
	public class ScatterNode : GeoNode
	{
		[Min(1)] public int PointCount = 100;
		public int RandomSeed;

		public override GeoData Process(GeoData input)
		{
			var output = new GeoData();

			if (input == null || input.Primitives.Count == 0 || PointCount <= 0)
				return output;

			var triangles = Triangulate(input);
			var totalArea = 0f;
			foreach (var triangle in triangles)
				totalArea += triangle.Area;

			if (totalArea <= 0f)
				return output;

			var random = new System.Random(RandomSeed);

			for (var i = 0; i < PointCount; i++)
			{
				var triangle = PickTriangle(triangles, totalArea, random);
				var (position, normal) = RandomPointOnTriangle(input, triangle, random);

				output.AddPoint(position, normal, Vector2.zero);
			}

			return output;
		}

		static List<WeightedTriangle> Triangulate(GeoData data)
		{
			var triangles = new List<WeightedTriangle>();

			foreach (var primitive in data.Primitives)
			{
				for (var i = 1; i < primitive.Length - 1; i++)
				{
					var i0 = primitive[0];
					var i1 = primitive[i];
					var i2 = primitive[i + 1];
					var area = Vector3.Cross(data.Points[i1] - data.Points[i0], data.Points[i2] - data.Points[i0]).magnitude * 0.5f;

					triangles.Add(new WeightedTriangle(i0, i1, i2, area));
				}
			}

			return triangles;
		}

		static WeightedTriangle PickTriangle(List<WeightedTriangle> triangles, float totalArea, System.Random random)
		{
			var target = (float) random.NextDouble() * totalArea;
			var accumulated = 0f;

			foreach (var triangle in triangles)
			{
				accumulated += triangle.Area;
				if (accumulated >= target)
					return triangle;
			}

			return triangles[^1];
		}

		static (Vector3 position, Vector3 normal) RandomPointOnTriangle(GeoData data, WeightedTriangle triangle, System.Random random)
		{
			// Uniform barycentric sampling of a triangle - the sqrt on the first random value avoids clustering
			// points towards one corner.
			var r1 = Mathf.Sqrt((float) random.NextDouble());
			var r2 = (float) random.NextDouble();
			var a = 1f - r1;
			var b = r1 * (1f - r2);
			var c = r1 * r2;

			var position = data.Points[triangle.I0] * a + data.Points[triangle.I1] * b + data.Points[triangle.I2] * c;
			var normal = (data.Normals[triangle.I0] * a + data.Normals[triangle.I1] * b + data.Normals[triangle.I2] * c).normalized;

			return (position, normal);
		}

		readonly struct WeightedTriangle
		{
			public readonly int I0, I1, I2;
			public readonly float Area;

			public WeightedTriangle(int i0, int i1, int i2, float area)
			{
				I0 = i0;
				I1 = i1;
				I2 = i2;
				Area = area;
			}
		}
	}
}
