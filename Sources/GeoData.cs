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
	/// <summary> Intermediate geometry passed between GeoNodes: a flat point cloud (position/normal/UV/vertex
	/// color) plus polygon primitives referencing it by index, with per-point float attributes nodes can read/write
	/// to pass extra data along the chain (density, scale, custom masks, etc). </summary>
	public class GeoData
	{
		/// <summary> Well-known per-point attribute SmoothByAngleNode tags a smoothing cluster's points with (0 =
		/// untagged) - GeoMeshBuilder re-averages Unity's per-index RecalculateNormals within each tagged group
		/// afterward, since that call can't see that two different indices share one position (a UV seam). </summary>
		public const string SmoothGroupAttribute = "SmoothGroup";

		public readonly List<Vector3> Points = new ();
		public readonly List<Vector3> Normals = new ();
		public readonly List<Vector2> Uvs = new ();
		public readonly List<Color> Colors = new (); // defaults to white - see AddPoint

		/// <summary> Each primitive is a polygon defined by point indices, walked in order (fan-triangulated on build). </summary>
		public readonly List<int[]> Primitives = new ();

		readonly Dictionary<string, List<float>> attributes = new ();

		public int PointCount => Points.Count;

		public int AddPoint(Vector3 position, Vector3 normal, Vector2 uv) => AddPoint(position, normal, uv, Color.white);

		public int AddPoint(Vector3 position, Vector3 normal, Vector2 uv, Color color)
		{
			Points.Add(position);
			Normals.Add(normal);
			Uvs.Add(uv);
			Colors.Add(color);
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

		/// <summary> Deep-enough copy for graph fan-out: a node whose output feeds more than one downstream node
		/// must hand each of them an independent GeoData, since several nodes (TransformNode, NoiseDisplaceNode,
		/// ExtrudeNode, MergeNode's base input...) mutate what they receive in place. </summary>
		public GeoData Clone()
		{
			var clone = new GeoData();

			clone.Points.AddRange(Points);
			clone.Normals.AddRange(Normals);
			clone.Uvs.AddRange(Uvs);
			clone.Colors.AddRange(Colors);

			foreach (var primitive in Primitives)
				clone.Primitives.Add((int[]) primitive.Clone());

			foreach (var pair in attributes)
				clone.attributes[pair.Key] = new List<float>(pair.Value);

			return clone;
		}
	}
}
