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
	/// <summary> Like LineGeneratorNode, but PointCount points resampled along a Catmull-Rom curve through
	/// ControlPoints instead of a straight line - points only, no faces, meant to feed CopyToPointsNode. Every
	/// point's normal is Vector3.up regardless of the curve's own direction, same as LineGeneratorNode and for the
	/// same reason: copies stay upright by default. </summary>
	[Serializable]
	public class SplineGeneratorNode : GeoNode
	{
		public List<Vector3> ControlPoints = new ()
		{
			new Vector3(0f, 0f, 0f),
			new Vector3(2f, 0f, 4f),
			new Vector3(6f, 0f, 4f),
			new Vector3(8f, 0f, 0f),
		};

		[Min(2)] public int PointCount = 32;

		/// <summary> Adds one more segment looping from the last control point back to the first, instead of
		/// stopping there. </summary>
		public bool Closed;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (ControlPoints == null || ControlPoints.Count < 2)
				return data;

			var pointCount = Mathf.Max(2, PointCount);
			var segments = Closed ? ControlPoints.Count : ControlPoints.Count - 1;

			for (var i = 0; i < pointCount; i++)
			{
				var t = Closed ? i / (float) pointCount : i / (float) (pointCount - 1);
				data.AddPoint(Sample(t * segments), Vector3.up, new Vector2(t, 0f));
			}

			return data;
		}

		Vector3 Sample(float u)
		{
			var segments = Closed ? ControlPoints.Count : ControlPoints.Count - 1;
			var segment = Mathf.Clamp(Mathf.FloorToInt(u), 0, segments - 1);
			var t = u - segment;

			return CatmullRom(ControlPointAt(segment - 1), ControlPointAt(segment), ControlPointAt(segment + 1), ControlPointAt(segment + 2), t);
		}

		// Open curves clamp to the first/last control point instead of extrapolating past them - the standard
		// way to handle Catmull-Rom needing a point on each side of every segment, including the two end ones.
		Vector3 ControlPointAt(int index)
		{
			var count = ControlPoints.Count;
			return Closed ? ControlPoints[((index % count) + count) % count] : ControlPoints[Mathf.Clamp(index, 0, count - 1)];
		}

		static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
		{
			var t2 = t * t;
			var t3 = t2 * t;

			return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
		}
	}
}
