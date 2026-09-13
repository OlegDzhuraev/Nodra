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
	/// <summary> Generates PointCount points evenly spaced from Start to End (a single point at Start when
	/// PointCount is 1) - like ScatterNode, this is points with no faces, meant to feed a following
	/// CopyToPointsNode (fences, columns, stepping stones, ...) rather than to be built into a mesh directly. Every
	/// point's normal is Vector3.up regardless of the line's own direction, so copies stay upright by default. </summary>
	[Serializable]
	public class LineGeneratorNode : GeoNode
	{
		public Vector3 Start = Vector3.zero;
		public Vector3 End = new (0f, 0f, 10f);
		[Min(1)] public int PointCount = 10;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();
			var pointCount = Mathf.Max(1, PointCount);

			for (var i = 0; i < pointCount; i++)
			{
				var t = pointCount > 1 ? i / (float) (pointCount - 1) : 0f;
				data.AddPoint(Vector3.Lerp(Start, End, t), Vector3.up, new Vector2(t, 0f));
			}

			return data;
		}
	}
}
