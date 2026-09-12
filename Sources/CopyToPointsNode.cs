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
	/// <summary> Stamps a source mesh at every point of the input geometry, optionally aligning to the point's
	/// normal and jittering rotation/scale, merging all copies into one output. Typically fed by a ScatterNode. </summary>
	[Serializable]
	public class CopyToPointsNode : GeoNode
	{
		public Mesh SourceMesh;
		public bool AlignToNormal = true;
		public float RandomYRotation = 360f;
		public Vector2 UniformScaleRange = new (1f, 1f);
		public int RandomSeed;

		public override GeoData Process(GeoData input)
		{
			var output = new GeoData();

			if (input == null || SourceMesh == null)
				return output;

			var sourceVertices = SourceMesh.vertices;
			var sourceNormals = SourceMesh.normals;
			var sourceUvs = SourceMesh.uv;
			var sourceTriangles = SourceMesh.triangles;
			var random = new System.Random(RandomSeed);

			for (var p = 0; p < input.PointCount; p++)
			{
				var position = input.Points[p];
				var normal = p < input.Normals.Count ? input.Normals[p] : Vector3.up;

				var alignment = AlignToNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
				var yaw = Quaternion.AngleAxis((float) random.NextDouble() * RandomYRotation, Vector3.up);
				var rotation = alignment * yaw;
				var scale = Mathf.Lerp(UniformScaleRange.x, UniformScaleRange.y, (float) random.NextDouble());

				var indexOffset = output.PointCount;

				for (var i = 0; i < sourceVertices.Length; i++)
				{
					var vertexNormal = i < sourceNormals.Length ? sourceNormals[i] : Vector3.up;
					var uv = i < sourceUvs.Length ? sourceUvs[i] : Vector2.zero;

					output.AddPoint(position + rotation * (sourceVertices[i] * scale), rotation * vertexNormal, uv);
				}

				for (var i = 0; i < sourceTriangles.Length; i += 3)
					output.AddPrimitive(indexOffset + sourceTriangles[i], indexOffset + sourceTriangles[i + 1], indexOffset + sourceTriangles[i + 2]);
			}

			return output;
		}
	}
}
