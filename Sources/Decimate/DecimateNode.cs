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

// Nodra.Decimate.asmdef's versionDefines only sets NODRA_DECIMATE while com.whinarn.unitymeshsimplifier is
// actually installed - gated here rather than around the whole class/file: DecimateNode itself must always exist,
// under the same assembly-qualified name, or a graph saved with one in it shows "Missing types referenced from
// component" (or silently drops the node on the next save) the moment the package isn't there. Only the code that
// actually touches UnityMeshSimplifier types is conditional; without the package, Process() just warns and passes
// the input through unchanged instead.
#if NODRA_DECIMATE
using UnityMeshSimplifier;
#endif

namespace Nodra
{
	/// <summary> Reduces triangle count via UnityMeshSimplifier's quadric-error decimation - the one node in this
	/// package with an external dependency (see agents.md/README for the required package install). Quality is
	/// the fraction of the original triangle count to aim for; the result is always fan-triangulated into 3-point
	/// primitives. Without the package installed, this is a no-op that warns instead of decimating. </summary>
	[Serializable]
	public class DecimateNode : GeoNode
	{
		public override string Category => "Cleanup";

		[Range(0.01f, 1f)] public float Quality = 0.5f;

		public override GeoData Process(GeoData input)
		{
#if NODRA_DECIMATE
			if (input == null || input.Primitives.Count == 0)
				return input;

			var simplifier = new MeshSimplifier
			{
				Vertices = input.Points.ToArray(),
				Normals = input.Normals.ToArray(),
				Colors = input.Colors.ToArray(),
			};

			simplifier.AddSubMeshTriangles(Triangulate(input));
			simplifier.SetUVs(0, input.Uvs);
			simplifier.SimplifyMesh(Mathf.Clamp01(Quality));

			return ToGeoData(simplifier);
#else
			return input;
#endif
		}

		// Shown as a HelpBox in the node's own body by NodraNodeView - a console warning would just repeat on
		// every Generate() (spammy with Auto Generate on), where this stays visible without shouting each time.
		public override string Warning =>
#if NODRA_DECIMATE
			null;
#else
			"UnityMeshSimplifier isn't installed - this node passes geometry through unchanged instead of decimating.";
#endif

#if NODRA_DECIMATE
		// Same fan-triangulation GeoMeshBuilder bakes with - the simplifier only ever sees triangles, it doesn't
		// know about GeoData's own N-gon primitives.
		static int[] Triangulate(GeoData data)
		{
			var triangles = new List<int>();

			foreach (var primitive in data.Primitives)
				for (var i = 1; i < primitive.Length - 1; i++)
				{
					triangles.Add(primitive[0]);
					triangles.Add(primitive[i]);
					triangles.Add(primitive[i + 1]);
				}

			return triangles.ToArray();
		}

		// Every surviving triangle becomes its own 3-point primitive - the simplifier only ever outputs triangles,
		// same as GeoCsg.ToGeoData does for its own BSP output.
		static GeoData ToGeoData(MeshSimplifier simplifier)
		{
			var data = new GeoData();
			var vertices = simplifier.Vertices;
			var normals = simplifier.Normals;
			var colors = simplifier.Colors;
			var uvs = new List<Vector2>();
			simplifier.GetUVs(0, uvs);

			for (var i = 0; i < vertices.Length; i++)
			{
				var normal = i < normals.Length ? normals[i] : Vector3.up;
				var uv = i < uvs.Count ? uvs[i] : Vector2.zero;
				var color = colors != null && i < colors.Length ? colors[i] : Color.white;

				data.AddPoint(vertices[i], normal, uv, color);
			}

			var triangles = simplifier.GetSubMeshTriangles(0);
			for (var i = 0; i < triangles.Length; i += 3)
				data.AddPrimitive(triangles[i], triangles[i + 1], triangles[i + 2]);

			return data;
		}
#endif
	}
}
