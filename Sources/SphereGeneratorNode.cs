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
	/// <summary> Generates a UV sphere centered on the origin: latitude rings walked from pole to pole, each split
	/// into longitude segments. Usually the first node in a chain, like the other generators. Each pole is a ring
	/// of coincident points (needed for a clean UV seam), so every quad touching a pole fan-triangulates into one
	/// real triangle plus one harmless zero-area one - the standard, simplest way to build a UV sphere. </summary>
	[Serializable]
	public class SphereGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float Radius = 1f;

		/// <summary> x: segments around the equator (longitude); y: rings from pole to pole (latitude). </summary>
		[MinVector2Int(3, 2)] public Vector2Int Resolution = new (16, 8);

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var columns = Mathf.Max(3, Resolution.x);
			var rows = Mathf.Max(2, Resolution.y);
			// [Min] only constrains the Inspector - a negative Radius reflects every point through the origin,
			// which (unlike a 2D point reflection) reverses this shape's winding, so it's re-clamped here too.
			var radius = Mathf.Max(0f, Radius);
			var startIndex = data.PointCount;

			for (var row = 0; row <= rows; row++)
			{
				var v = row / (float) rows;
				var theta = v * Mathf.PI;
				var sinTheta = Mathf.Sin(theta);
				var cosTheta = Mathf.Cos(theta);
				var seamDirection = Vector3.zero;

				for (var col = 0; col <= columns; col++)
				{
					var u = col / (float) columns;
					Vector3 direction;

					// col == columns closes the ring back onto col == 0 - reusing its exact direction instead of
					// recomputing sin/cos(2*PI) (which drifts from sin/cos(0) by a float epsilon) keeps that seam's
					// two vertex rows bit-for-bit identical in position, which Mesh.RecalculateNormals needs to
					// weld them into one smooth normal instead of leaving a faceted seam down the sphere.
					if (col == columns)
					{
						direction = seamDirection;
					}
					else
					{
						var phi = u * Mathf.PI * 2f;
						direction = new Vector3(sinTheta * Mathf.Cos(phi), cosTheta, sinTheta * Mathf.Sin(phi));

						if (col == 0)
							seamDirection = direction;
					}

					data.AddPoint(direction * radius, direction, new Vector2(u, v));
				}
			}

			for (var row = 0; row < rows; row++)
			{
				for (var col = 0; col < columns; col++)
				{
					var i0 = startIndex + row * (columns + 1) + col;
					var i1 = i0 + 1;
					var i2 = i0 + columns + 1;
					var i3 = i2 + 1;

					// Winding is deliberately (i0, i1, i3, i2) here, not GridGeneratorNode's (i0, i2, i3, i1) - the
					// two parametrizations sweep their "row"/"column" tangents in opposite handedness relative to
					// their outward normal, so the same index pattern would face this shape inward.
					data.AddPrimitive(i0, i1, i3, i2);
				}
			}

			return data;
		}
	}
}
