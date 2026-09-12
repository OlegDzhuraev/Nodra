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

using System.Collections.Generic;
using UnityEngine;

namespace Nodra
{
	/// <summary> Intermediate geometry passed between GeoNodes: a flat point cloud plus polygon primitives
	/// referencing it by index, with per-point float attributes
	/// nodes can read/write to pass extra data along the chain (density, scale, custom masks, etc). </summary>
	public class GeoData
	{
		public readonly List<Vector3> Points = new ();
		public readonly List<Vector3> Normals = new ();
		public readonly List<Vector2> Uvs = new ();

		/// <summary> Each primitive is a polygon defined by point indices, walked in order (fan-triangulated on build). </summary>
		public readonly List<int[]> Primitives = new ();

		readonly Dictionary<string, List<float>> attributes = new ();

		public int PointCount => Points.Count;

		public int AddPoint(Vector3 position, Vector3 normal, Vector2 uv)
		{
			Points.Add(position);
			Normals.Add(normal);
			Uvs.Add(uv);
			return Points.Count - 1;
		}

		public void AddPrimitive(params int[] pointIndices) => Primitives.Add(pointIndices);

		public void SetAttribute(string name, int pointIndex, float value)
		{
			if (!attributes.TryGetValue(name, out var values))
			{
				values = new List<float>(new float[Points.Count]);
				attributes[name] = values;
			}

			while (values.Count <= pointIndex)
				values.Add(0f);

			values[pointIndex] = value;
		}

		public float GetAttribute(string name, int pointIndex, float fallback = 0f)
		{
			if (attributes.TryGetValue(name, out var values) && pointIndex < values.Count)
				return values[pointIndex];

			return fallback;
		}

		public bool HasAttribute(string name) => attributes.ContainsKey(name);
	}
}
