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
	/// <summary> Keeps only primitives whose face normal is within MaxAngle of Direction, dropping the rest (Invert
	/// flips it - discard those instead, keep everything else) - cut the bottom off a sphere, remove upward-facing
	/// caps, carve a half-pipe without a full BooleanNode. Leftover points no primitive references anymore aren't
	/// removed; chain a WeldNode-adjacent cleanup pass first if that matters. </summary>
	[Serializable]
	public class FaceFilterNode : GeoNode
	{
		public override string Category => "Cleanup";

		public Vector3 Direction = Vector3.up;
		[Range(0f, 180f)] public float MaxAngle = 90f;
		public bool Invert;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			var direction = Direction.sqrMagnitude > 0f ? Direction.normalized : Vector3.up;
			var cosThreshold = Mathf.Cos(MaxAngle * Mathf.Deg2Rad);
			var kept = new List<int[]>();

			foreach (var primitive in input.Primitives)
			{
				var withinAngle = Vector3.Dot(ComputeFaceNormal(input, primitive), direction) >= cosThreshold;

				if (withinAngle != Invert)
					kept.Add(primitive);
			}

			input.Primitives.Clear();
			input.Primitives.AddRange(kept);

			return input;
		}

		/// <summary> Newell's method, matching ComputeFaceNormal in ExtrudeNode/ChamferNode/SmoothByAngleNode/AutoUVNode. </summary>
		static Vector3 ComputeFaceNormal(GeoData data, int[] primitive)
		{
			var normal = Vector3.zero;
			var count = primitive.Length;

			for (var i = 0; i < count; i++)
			{
				var current = data.Points[primitive[i]];
				var next = data.Points[primitive[(i + 1) % count]];

				normal.x += (current.y - next.y) * (current.z + next.z);
				normal.y += (current.z - next.z) * (current.x + next.x);
				normal.z += (current.x - next.x) * (current.y + next.y);
			}

			return normal.normalized;
		}
	}
}
