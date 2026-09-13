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
	/// <summary> Generates a subdivided XZ grid, like GridGeneratorNode, but pushes each point up by HeightMap's
	/// grayscale value at that point's UV, scaled by Height - a simple heightmap terrain, not a real terrain system
	/// (no LOD, no erosion, ...). HeightMap needs its Read/Write Enabled import setting on to be sampled; left off
	/// (Unity's default on import) or unassigned, the result is just a flat grid. </summary>
	[Serializable]
	public class HeightMapGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		public Texture2D HeightMap;
		[Min(0f)] public Vector2 Size = new (10f, 10f);
		[MinVector2Int(1)] public Vector2Int Resolution = new (50, 50);
		public float Height = 2f;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var columns = Mathf.Max(1, Resolution.x);
			var rows = Mathf.Max(1, Resolution.y);
			// [Min] only constrains the Inspector - see GridGeneratorNode for why a negative Size still needs
			// clamping here too. Height is left signed on purpose: it only offsets Y, so flipping it just turns
			// the terrain upside down instead of inverting any winding.
			var size = Vector2.Max(Vector2.zero, Size);
			var readable = HeightMap != null && HeightMap.isReadable;
			var startIndex = data.PointCount;

			for (var row = 0; row <= rows; row++)
			{
				for (var col = 0; col <= columns; col++)
				{
					var u = col / (float) columns;
					var v = row / (float) rows;
					var sample = readable ? HeightMap.GetPixelBilinear(u, v).grayscale : 0f;
					var position = new Vector3((u - 0.5f) * size.x, sample * Height, (v - 0.5f) * size.y);

					data.AddPoint(position, Vector3.up, new Vector2(u, v));
				}
			}

			// Same (i0, i2, i3, i1) winding as GridGeneratorNode - height only moves each point along Y, it
			// doesn't change which column/row tangent direction the primitive order is built from.
			for (var row = 0; row < rows; row++)
			{
				for (var col = 0; col < columns; col++)
				{
					var i0 = startIndex + row * (columns + 1) + col;
					var i1 = i0 + 1;
					var i2 = i0 + columns + 1;
					var i3 = i2 + 1;

					data.AddPrimitive(i0, i2, i3, i1);
				}
			}

			return data;
		}
	}
}
