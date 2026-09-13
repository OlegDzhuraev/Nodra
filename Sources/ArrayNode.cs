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
	/// <summary> Appends Count copies of the input geometry, copy 0 unmoved and each next one built from the
	/// cumulative transform for its index: rotated by Rotation * copy around Pivot, then moved by Offset * copy
	/// plus RadialOffset rotated along with it - the last term is what turns a plain rotating array into a ring,
	/// since a fixed local vector rotated by an increasing angle walks around a circle of that radius. Nothing is
	/// welded, so coincident points between copies stay separate (same as MergeNode). </summary>
	[Serializable]
	public class ArrayNode : GeoNode
	{
		public override string Category => "Build";

		[Min(1)] public int Count = 3;
		public Vector3 Offset = new (2f, 0f, 0f);
		public Vector3 Rotation;
		public Vector3 RadialOffset;
		public Vector3 Pivot;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0)
				return input;

			// Snapshotted before clearing - every copy (including the first) is built fresh from this, never from
			// a copy this node already appended.
			var sourcePoints = new List<Vector3>(input.Points);
			var sourceNormals = new List<Vector3>(input.Normals);
			var sourceUvs = new List<Vector2>(input.Uvs);
			var sourceColors = new List<Color>(input.Colors);
			var sourcePrimitives = new List<int[]>(input.Primitives);

			input.Points.Clear();
			input.Normals.Clear();
			input.Uvs.Clear();
			input.Colors.Clear();
			input.Primitives.Clear();

			var count = Mathf.Max(1, Count);

			for (var copy = 0; copy < count; copy++)
			{
				var rotation = Quaternion.Euler(Rotation * copy);
				var translation = Offset * copy + rotation * RadialOffset;
				var indexOffset = input.PointCount;

				for (var i = 0; i < sourcePoints.Count; i++)
				{
					var position = Pivot + rotation * (sourcePoints[i] - Pivot) + translation;
					input.AddPoint(position, rotation * sourceNormals[i], sourceUvs[i], sourceColors[i]);
				}

				foreach (var primitive in sourcePrimitives)
				{
					var shifted = new int[primitive.Length];
					for (var i = 0; i < primitive.Length; i++)
						shifted[i] = primitive[i] + indexOffset;

					input.AddPrimitive(shifted);
				}
			}

			return input;
		}
	}
}
