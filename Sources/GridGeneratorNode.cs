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
	/// <summary> Generates a flat, subdivided quad grid on the XZ plane, centered on the origin. Usually the
	/// first node in a chain. </summary>
	[Serializable]
	public class GridGeneratorNode : GeoNode
	{
		public Vector2 Size = new (10f, 10f);
		public Vector2Int Resolution = new (10, 10);

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var columns = Mathf.Max(1, Resolution.x);
			var rows = Mathf.Max(1, Resolution.y);
			var startIndex = data.PointCount;

			for (var row = 0; row <= rows; row++)
			{
				for (var col = 0; col <= columns; col++)
				{
					var u = col / (float) columns;
					var v = row / (float) rows;
					var position = new Vector3((u - 0.5f) * Size.x, 0f, (v - 0.5f) * Size.y);

					data.AddPoint(position, Vector3.up, new Vector2(u, v));
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

					data.AddPrimitive(i0, i2, i3, i1);
				}
			}

			return data;
		}
	}
}
