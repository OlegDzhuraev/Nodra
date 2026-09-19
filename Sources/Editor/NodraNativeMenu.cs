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
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nodra
{
	/// <summary> Manual smoke test for the NodraNative P/Invoke boundary - there's no automated test runner
	/// exercising native plugin loading (or Unity-side struct marshaling) here, so this is the fastest way to
	/// confirm a freshly built/placed NodraCore binary actually loads and answers correctly before trusting any
	/// real node to it. Native/NodraCore.Tests covers the underlying algorithm itself in isolation, in plain
	/// `dotnet run` - this only exists to catch a mismatch in the marshaling glue around it. </summary>
	static class NodraNativeMenu
	{
		[MenuItem("Nodra/Test Native Ping")]
		static void TestPing()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			EditorUtility.DisplayDialog("Nodra Native", $"nodra_core_ping(41) = {NodraNative.Ping(41)}", "OK");
		}

		[MenuItem("Nodra/Test Native Weld")]
		static void TestWeld()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Two points 0.00005 apart (well within distance 0.0001, so they must merge) plus one far outlier
			// that must survive untouched - same shape of case Native/NodraCore.Tests/Program.cs already checks
			// against the portable algorithm directly; this only exercises the marshaling on top of it.
			var data = new GeoData();
			data.AddPoint(new Vector3(0f, 0f, 0f), Vector3.up, Vector2.zero, Color.white);
			data.AddPoint(new Vector3(0f, 0f, 0.00005f), Vector3.up, Vector2.one, Color.black);
			data.AddPoint(new Vector3(5f, 0f, 0f), Vector3.up, Vector2.zero, Color.white);

			var result = NodraNative.Weld(data, 0.0001f);

			var ok = result.Points.Count == 2 && result.Remap[0] == result.Remap[1] && result.Remap[1] != result.Remap[2];
			EditorUtility.DisplayDialog("Nodra Native",
				$"Weld(3 points, 2 near-duplicates) -> {result.Points.Count} points, remap [{string.Join(",", result.Remap)}]\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Smooth By Angle")]
		static void TestSmoothByAngle()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Two triangles sharing an edge by INDEX, folded back on themselves (near-opposite face normals) -
			// well outside a 90-degree threshold, so both shared corners (0 and 1) must duplicate into new points
			// that keep face B's own flat normal - same case Native/NodraCore.Tests/Program.cs already checks
			// against the portable algorithm directly; this only exercises the growing-buffer marshaling on top.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(0.5f, 1, 0), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(0.5f, -1, 0), Vector3.forward, Vector2.zero);
			data.AddPrimitive(0, 1, 2);
			data.AddPrimitive(0, 1, 3);

			var result = NodraNative.SmoothByAngle(data, 90f);

			var ok = result.Points.Count == 6 && Vector3.Dot(result.Normals[0], result.Normals[4]) < 0f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"SmoothByAngle(sharp hinge) -> {result.Points.Count} points (expected 6)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Box Generator")]
		static void TestBoxGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.BoxGenerator(data, new Vector3(2f, 4f, 6f));

			var ok = data.PointCount == 24 && data.Primitives.Count == 6 && data.GetBounds().size == new Vector3(2f, 4f, 6f);
			EditorUtility.DisplayDialog("Nodra Native",
				$"BoxGenerator(2,4,6) -> {data.PointCount} points, {data.Primitives.Count} quads, bounds size {data.GetBounds().size}\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Sphere Generator")]
		static void TestSphereGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.SphereGenerator(data, 2f, 12, 6);

			var ok = data.PointCount == 7 * 13 && data.Primitives.Count == 6 * 12;
			EditorUtility.DisplayDialog("Nodra Native",
				$"SphereGenerator(r=2, 12x6) -> {data.PointCount} points (expected {7 * 13}), {data.Primitives.Count} quads (expected {6 * 12})\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Grid Generator")]
		static void TestGridGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.GridGenerator(data, new Vector2(4f, 6f), 4, 3);

			var ok = data.PointCount == 5 * 4 && data.Primitives.Count == 4 * 3 && data.GetBounds().size == new Vector3(4f, 0f, 6f);
			EditorUtility.DisplayDialog("Nodra Native",
				$"GridGenerator(4x6, 4x3) -> {data.PointCount} points (expected {5 * 4}), {data.Primitives.Count} quads (expected {4 * 3}), bounds size {data.GetBounds().size}\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Torus Generator")]
		static void TestTorusGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.TorusGenerator(data, 2f, 0.5f, 12, 8);

			var ok = data.PointCount == 9 * 13 && data.Primitives.Count == 8 * 12;
			EditorUtility.DisplayDialog("Nodra Native",
				$"TorusGenerator(major=2, minor=0.5, 12x8) -> {data.PointCount} points (expected {9 * 13}), {data.Primitives.Count} quads (expected {8 * 12})\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Cylinder Generator")]
		static void TestCylinderGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			// RadiusTop 0 (a cone) with both caps requested - CapTop must be skipped (nothing to cap at a
			// zero-radius end), same check CylinderGenerator.cs's own Run applies.
			NodraNative.CylinderGenerator(data, 1f, 0f, 2f, 6, 1, true, true);

			var expectedPoints = 2 * 7 + (1 + 7); // two rings + one bottom cap fan (center + ring), no top cap
			var expectedPrimitives = 6 + 6; // 6 side quads + 6 bottom cap triangles
			var ok = data.PointCount == expectedPoints && data.Primitives.Count == expectedPrimitives;
			EditorUtility.DisplayDialog("Nodra Native",
				$"CylinderGenerator(cone, both caps requested) -> {data.PointCount} points (expected {expectedPoints}), {data.Primitives.Count} primitives (expected {expectedPrimitives})\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Noise Displace")]
		static void TestNoiseDisplace()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			data.AddPoint(new Vector3(1.3f, 0f, 2.7f), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(-4.1f, 0f, 0.6f), Vector3.up, Vector2.zero);

			var zeroAmplitude = NodraNative.NoiseDisplace(data, (int) NoiseDisplaceNode.NoiseType.Perlin, 0f, 0.3f, 0f, 0f, true);
			var displaced = NodraNative.NoiseDisplace(data, (int) NoiseDisplaceNode.NoiseType.Perlin, 2f, 0.3f, 0f, 0f, true);

			var ok = zeroAmplitude[0] == data.Points[0] && zeroAmplitude[1] == data.Points[1]
				&& (displaced[0] != data.Points[0] || displaced[1] != data.Points[1]);
			EditorUtility.DisplayDialog("Nodra Native",
				$"NoiseDisplace(Perlin) -> amplitude 0 unchanged: {zeroAmplitude[0]}, {zeroAmplitude[1]}; amplitude 2: {displaced[0]}, {displaced[1]}\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Array")]
		static void TestArray()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			data.AddPoint(new Vector3(0f, 0f, 0f), Vector3.up, Vector2.zero, Color.white);
			data.AddPoint(new Vector3(1f, 0f, 0f), Vector3.up, Vector2.one, Color.black);
			data.AddPrimitive(0, 1);

			// 3 copies translated by (2,0,0) each - copy 0 stays put, copy 2's points land at (4,0,0)/(5,0,0), same
			// shape of case Native/NodraCore.Tests/Program.cs already checks directly.
			NodraNative.ArrayModifier(data, 3, new Vector3(2f, 0f, 0f), Vector3.zero, Vector3.zero, Vector3.zero);

			var ok = data.PointCount == 6 && data.Primitives.Count == 3
				&& data.Points[0] == new Vector3(0f, 0f, 0f) && data.Points[1] == new Vector3(1f, 0f, 0f)
				&& data.Points[4] == new Vector3(4f, 0f, 0f) && data.Points[5] == new Vector3(5f, 0f, 0f)
				&& data.Primitives[2][0] == 4 && data.Primitives[2][1] == 5;
			EditorUtility.DisplayDialog("Nodra Native",
				$"ArrayModifier(2 points, Count=3, Offset=(2,0,0)) -> {data.PointCount} points (expected 6), {data.Primitives.Count} primitives (expected 3)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Auto UV")]
		static void TestAutoUV()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A Z-facing quad - Triplanar must project it straight onto its own XY plane at scale 1, same case
			// Native/NodraCore.Tests/Program.cs already checks against the portable algorithm directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0f, 0f, 0f), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(2f, 0f, 0f), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(2f, 3f, 0f), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(0f, 3f, 0f), Vector3.forward, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);

			NodraNative.AutoUV(data, (int) UVProjection.Triplanar, 1f);

			var ok = data.PointCount == 4 && data.Uvs[2] == new Vector2(2f, 3f);
			EditorUtility.DisplayDialog("Nodra Native",
				$"AutoUV(Triplanar, Z-facing quad) -> corner 2 UV {data.Uvs[2]} (expected (2,3))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Bend")]
		static void TestBend()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A 10-unit line along Y, bent 90 degrees - the far end must land at (radius, radius, 0), same case
			// Native/NodraCore.Tests/Program.cs already checks directly (see BendNode.cs's own comment for the
			// hand-verified derivation).
			var data = new GeoData();
			data.AddPoint(new Vector3(0f, 0f, 0f), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0f, 10f, 0f), Vector3.up, Vector2.zero);

			var radius = 10f / (Mathf.PI / 2f);
			var bent = NodraNative.Bend(data, (int) Axis3D.Y, Vector3.zero, 90f);

			var ok = Vector3.Distance(bent[0], Vector3.zero) < 0.001f && Vector3.Distance(bent[1], new Vector3(radius, radius, 0f)) < 0.01f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Bend(10-unit line, Axis=Y, Angle=90) -> far end {bent[1]} (expected ~({radius:F3}, {radius:F3}, 0))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Cap Holes")]
		static void TestCapHoles()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single open quad has no neighbors, so every one of its own edges is a boundary edge - CapHoles
			// must fill it with one reversed-winding cap, same case Native/NodraCore.Tests/Program.cs already
			// checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0f, 0f, 0f), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1f, 0f, 0f), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1f, 0f, 1f), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0f, 0f, 1f), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);

			NodraNative.CapHoles(data);

			var ok = data.Primitives.Count == 2 && data.Primitives[1][0] == 3 && data.Primitives[1][1] == 2
				&& data.Primitives[1][2] == 1 && data.Primitives[1][3] == 0;
			EditorUtility.DisplayDialog("Nodra Native",
				$"CapHoles(1 open quad) -> {data.Primitives.Count} primitives (expected 2), cap [{string.Join(",", data.Primitives.Count > 1 ? data.Primitives[1] : Array.Empty<int>())}] (expected [3,2,1,0])\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Chamfer")]
		static void TestChamfer()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Two quads sharing one edge by POSITION only (separate point indices per quad, same as any generator
			// giving each face its own points) - hand-derived expected bridge/inset indices, same case Native/
			// NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 1, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 1, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 1, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(2, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(2, 1, 1), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);
			data.AddPrimitive(4, 5, 6, 7);

			NodraNative.Chamfer(data, 0.1f);

			var ok = data.PointCount == 8 + 8 && data.Primitives.Count == 3
				&& data.Primitives[0][0] == 8 + 1 && data.Primitives[1][0] == 8 + 0 && data.Primitives[2][0] == 8 + 4;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Chamfer(2 bridged quads) -> {data.PointCount} points (expected 16), {data.Primitives.Count} primitives (expected 3: 1 bridge + 2 insets)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Circle Generator")]
		static void TestCircleGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.CircleGenerator(data, 2f, 8, true);

			var ok = data.PointCount == 1 + 9 && data.Primitives.Count == 8 && data.Primitives.All(p => p.Length == 3);
			EditorUtility.DisplayDialog("Nodra Native",
				$"CircleGenerator(r=2, 8 segments, Fill) -> {data.PointCount} points (expected 10), {data.Primitives.Count} triangles (expected 8)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Copy To Points")]
		static void TestCopyToPoints()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			data.AddPoint(new Vector3(5, 0, 0), Vector3.up, Vector2.zero);

			var mesh = new Mesh
			{
				vertices = new[] { new Vector3(1, 0, 0) },
				normals = new[] { Vector3.forward },
				uv = new[] { new Vector2(0.5f, 0.5f) },
				triangles = Array.Empty<int>(),
			};

			var result = NodraNative.CopyToPoints(data, mesh, true, 0f, 1f, 1f, "", 0);
			UnityEngine.Object.DestroyImmediate(mesh);

			var ok = result.PointCount == 1 && Vector3.Distance(result.Points[0], new Vector3(6, 0, 0)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"CopyToPoints(1 point, 1-vertex mesh, Up-aligned identity) -> {result.PointCount} point at {(result.PointCount > 0 ? result.Points[0] : default)} (expected (6,0,0))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Delete Points Random")]
		static void TestDeletePointsRandom()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var keepAll = NodraNative.DeletePointsRandom(20, 0f, 1);
			var keepNone = NodraNative.DeletePointsRandom(20, 1f, 1);

			var ok = keepAll.All(k => k) && keepNone.All(k => !k);
			EditorUtility.DisplayDialog("Nodra Native",
				$"DeletePointsRandom(Chance=0) keeps all: {keepAll.All(k => k)}; (Chance=1) keeps none: {keepNone.All(k => !k)}\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Extrude")]
		static void TestExtrude()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single +Y-facing quad with no neighbors - every edge is a boundary edge, so Extrude should add 1
			// cap + 4 walls, same case Native/NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);

			NodraNative.Extrude(data, 2f, true);

			var ok = data.PointCount == 8 && data.Primitives.Count == 5
				&& Vector3.Distance(data.Points[4], new Vector3(0, 2, 0)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Extrude(1 quad, Distance=2, CapNewFace) -> {data.PointCount} points (expected 8), {data.Primitives.Count} primitives (expected 5: 1 cap + 4 walls)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Face Filter")]
		static void TestFaceFilter()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// quadUp has a +Y face normal, quadSide has +X - only quadUp should survive within 45 degrees of Up,
			// same case Native/NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(5, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(5, 1, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(5, 1, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(5, 0, 1), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);
			data.AddPrimitive(4, 5, 6, 7);

			NodraNative.FaceFilter(data, Vector3.up, 45f, false);

			var ok = data.Primitives.Count == 1 && data.Primitives[0].SequenceEqual(new[] { 0, 1, 2, 3 });
			EditorUtility.DisplayDialog("Nodra Native",
				$"FaceFilter(Up, MaxAngle=45) -> {data.Primitives.Count} surviving primitive (expected 1, quadUp)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Ico Sphere Generator")]
		static void TestIcoSphereGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.IcoSphereGenerator(data, 2f, 0);

			var ok = data.PointCount == 12 && data.Primitives.Count == 20 && data.Primitives.All(p => p.Length == 3);
			EditorUtility.DisplayDialog("Nodra Native",
				$"IcoSphereGenerator(r=2, Subdivisions=0) -> {data.PointCount} points (expected 12), {data.Primitives.Count} triangles (expected 20)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Mirror")]
		static void TestMirror()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Point 1 sits exactly on the X=0 mirror plane - it must be reused, not duplicated, same case Native/
			// NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(2, 0, 0), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2);

			NodraNative.Mirror(data, (int) Axis3D.X, 0f, 0.0001f);

			var ok = data.PointCount == 5 && data.Points[3] == new Vector3(-1, 0, 0) && data.Points[4] == new Vector3(-2, 0, 0)
				&& data.Primitives.Count == 2 && data.Primitives[1].SequenceEqual(new[] { 4, 1, 3 });
			EditorUtility.DisplayDialog("Nodra Native",
				$"Mirror(Axis=X, point 1 on-plane) -> {data.PointCount} points (expected 5), mirrored primitive [{string.Join(",", data.Primitives.Count > 1 ? data.Primitives[1] : Array.Empty<int>())}] (expected [4,1,3])\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Relax")]
		static void TestRelax()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single quad - every point has exactly 2 (diagonal) neighbors - at Factor=1 every point must land
			// exactly on its own neighbor average (1,1,0), same case Native/NodraCore.Tests/Program.cs already
			// checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(2, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(2, 2, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 2, 0), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);

			NodraNative.Relax(data, 1f, 1, false);

			var ok = data.Points.All(p => Vector3.Distance(p, new Vector3(1, 1, 0)) < 0.0001f);
			EditorUtility.DisplayDialog("Nodra Native",
				$"Relax(Factor=1, single quad) -> point 0 at {data.Points[0]} (expected (1,1,0))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Scatter")]
		static void TestScatter()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(4, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 4, 0), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2);

			var result = NodraNative.Scatter(data, 50, 1);

			var ok = result.PointCount == 50;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Scatter(1 triangle, PointCount=50) -> {result.PointCount} points (expected 50)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Subdivide")]
		static void TestSubdivide()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single quad splits into 4 quads around a shared centroid, same case Native/NodraCore.Tests/
			// Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(2, 0, 0), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(2, 2, 0), Vector3.forward, Vector2.zero);
			data.AddPoint(new Vector3(0, 2, 0), Vector3.forward, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);

			NodraNative.Subdivide(data, 1);

			var ok = data.PointCount == 9 && data.Primitives.Count == 4 && data.Primitives.All(p => p.Length == 4)
				&& Vector3.Distance(data.Points[8], new Vector3(1, 1, 0)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Subdivide(1 quad, Iterations=1) -> {data.PointCount} points (expected 9), {data.Primitives.Count} quads (expected 4)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Spline Generator")]
		static void TestSplineGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// 2 control points - the Catmull-Rom sample must still land exactly on both endpoints, same case
			// Native/NodraCore.Tests/Program.cs already checks directly.
			var controlPoints = new List<Vector3> { new(0, 0, 0), new(10, 0, 0) };
			var data = new GeoData();

			NodraNative.SplineGenerator(data, controlPoints, 5, false);

			var ok = data.PointCount == 5 && Vector3.Distance(data.Points[0], new Vector3(0, 0, 0)) < 0.0001f
				&& Vector3.Distance(data.Points[4], new Vector3(10, 0, 0)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"SplineGenerator(2 control points, PointCount=5) -> {data.PointCount} points, first {data.Points[0]}, last {data.Points[data.PointCount - 1]} (expected (0,0,0) and (10,0,0))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Vertex Color")]
		static void TestVertexColor()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 5, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 10, 0), Vector3.up, Vector2.zero);

			var heightT = NodraNative.VertexColorHeightT(data, (int) Axis3D.Y);
			var slopeT = NodraNative.VertexColorSlopeT(data);

			var ok = Mathf.Approximately(heightT[0], 0f) && Mathf.Approximately(heightT[1], 0.5f) && Mathf.Approximately(heightT[2], 1f)
				&& Mathf.Approximately(slopeT[0], 1f);
			EditorUtility.DisplayDialog("Nodra Native",
				$"VertexColor Height -> [{heightT[0]}, {heightT[1]}, {heightT[2]}] (expected [0, 0.5, 1]), Slope[0] -> {slopeT[0]} (expected 1)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Tube")]
		static void TestTube()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A straight 2-point path, no caps - 2 rings of Sides+1 points each and Sides side quads, same case
			// Native/NodraCore.Tests/Program.cs already checks directly.
			var path = new List<Vector3> { new(0, 0, 0), new(0, 10, 0) };
			var data = new GeoData();

			NodraNative.Tube(data, path, 4, 1f, 0f, false, false, false);

			var ok = data.PointCount == 2 * 5 && data.Primitives.Count == 4 && data.Primitives.All(p => p.Length == 4);
			EditorUtility.DisplayDialog("Nodra Native",
				$"Tube(straight 2-point path, Sides=4, no caps) -> {data.PointCount} points (expected 10), {data.Primitives.Count} quads (expected 4)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Boolean CSG")]
		static void TestBooleanCsg()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A small box strictly inside a bigger one, no shared faces - Union must keep exactly the bigger
			// box's own outer bounds (the small one is fully absorbed) and Intersect must keep exactly the
			// smaller box's own bounds (it's entirely contained), same case Native/NodraCore.Tests/Program.cs
			// already checks directly. Exact triangle counts aren't asserted - a BSP tree classifies against
			// infinite planes, not finite polygon extent, so the bigger box's own faces still get split by the
			// smaller box's planes even though the two never actually touch.
			var big = new GeoData();
			NodraNative.BoxGenerator(big, new Vector3(4, 4, 4));

			var small = new GeoData();
			NodraNative.BoxGenerator(small, new Vector3(2, 2, 2));

			var union = NodraNative.Csg(big, small, (int) BooleanOperation.Union);
			var intersect = NodraNative.Csg(big, small, (int) BooleanOperation.Intersect);

			var unionSize = union.GetBounds().size;
			var intersectSize = intersect.GetBounds().size;

			var ok = union.PointCount > 0 && intersect.PointCount > 0
				&& Vector3.Distance(unionSize, new Vector3(4, 4, 4)) < 0.001f
				&& Vector3.Distance(intersectSize, new Vector3(2, 2, 2)) < 0.001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Boolean CSG(small box inside a bigger one) -> Union bounds {unionSize} (expected (4,4,4)), Intersect bounds {intersectSize} (expected (2,2,2))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native UV Transform")]
		static void TestUVTransform()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single UV at (1, 0.5) rotated 90 degrees around the UV center (0.5, 0.5) lands exactly on
			// (0.5, 1), same case Native/NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(Vector3.zero, Vector3.up, new Vector2(1f, 0.5f));

			NodraNative.UVTransform(data, 90f, Vector2.one, Vector2.zero);

			var ok = Vector2.Distance(data.Uvs[0], new Vector2(0.5f, 1f)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"UVTransform(Rotation=90) on uv (1, 0.5) -> {data.Uvs[0]} (expected (0.5, 1))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Taper")]
		static void TestTaper()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Two points spanning Y=0..2 - at Factor=0.5, the Y=0 (min) end must stay untouched and the Y=2
			// (max) end must have its perpendicular (X) extent halved, same case Native/NodraCore.Tests/
			// Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 2, 0), Vector3.up, Vector2.zero);

			var tapered = NodraNative.Taper(data, (int) Axis3D.Y, Vector3.zero, 0.5f);
			data.Points.Clear();
			data.Points.AddRange(tapered);

			var ok = Vector3.Distance(data.Points[0], new Vector3(1, 0, 0)) < 0.0001f
				&& Vector3.Distance(data.Points[1], new Vector3(0.5f, 2, 0)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Taper(Axis=Y, Factor=0.5) -> min-end {data.Points[0]} (expected (1,0,0)), max-end {data.Points[1]} (expected (0.5,2,0))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Merge")]
		static void TestMerge()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single-point target merged with a single-point source - source's own point must land at index 1
			// and its (empty) primitive index space starts at offset 1, same case Native/NodraCore.Tests/
			// Program.cs already checks directly.
			var target = new GeoData();
			target.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			target.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			target.AddPrimitive(0, 1);

			var source = new GeoData();
			source.AddPoint(new Vector3(2, 0, 0), Vector3.up, Vector2.zero);
			source.AddPoint(new Vector3(3, 0, 0), Vector3.up, Vector2.zero);
			source.AddPrimitive(0, 1);

			NodraNative.Merge(target, source);

			var ok = target.PointCount == 4 && target.Primitives.Count == 2
				&& Vector3.Distance(target.Points[2], new Vector3(2, 0, 0)) < 0.0001f
				&& target.Primitives[1].SequenceEqual(new[] { 2, 3 });
			EditorUtility.DisplayDialog("Nodra Native",
				$"Merge(2-point target, 2-point source) -> {target.PointCount} points (expected 4), {target.Primitives.Count} primitives (expected 2)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Flip Normals")]
		static void TestFlipNormals()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A single up-facing triangle - flipping must reverse its winding to (0, 2, 1) and negate its normal
			// to down, same case Native/NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 0, 1), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2);

			NodraNative.FlipNormals(data);

			var ok = data.Primitives[0].SequenceEqual(new[] { 2, 1, 0 }) && Vector3.Distance(data.Normals[0], Vector3.down) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"FlipNormals(1 up-facing triangle) -> primitive [{string.Join(",", data.Primitives[0])}] (expected [2,1,0]), normal {data.Normals[0]} (expected down)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Line Generator")]
		static void TestLineGenerator()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			NodraNative.LineGenerator(data, Vector3.zero, new Vector3(0, 0, 10), 3);

			var ok = data.PointCount == 3
				&& Vector3.Distance(data.Points[1], new Vector3(0, 0, 5)) < 0.0001f
				&& Vector3.Distance(data.Normals[0], Vector3.up) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"LineGenerator(Start=0, End=(0,0,10), PointCount=3) -> {data.PointCount} points (expected 3), midpoint {data.Points[1]} (expected (0,0,5))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Twist")]
		static void TestTwist()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Two points spanning Y=0..2 - at Angle=90 around Y, the Y=0 (min) end must stay untouched and the
			// Y=2 (max) end's local (1,2,0) must rotate the full 90 degrees, same case Native/NodraCore.Tests/
			// Program.cs already checks directly (hand-derived via Rodrigues' rotation formula there).
			var data = new GeoData();
			data.AddPoint(new Vector3(1, 0, 0), Vector3.right, Vector2.zero);
			data.AddPoint(new Vector3(1, 2, 0), Vector3.right, Vector2.zero);

			var (points, normals) = NodraNative.Twist(data, (int) Axis3D.Y, Vector3.zero, 90f);

			var ok = Vector3.Distance(points[0], new Vector3(1, 0, 0)) < 0.0001f
				&& Vector3.Distance(points[1], new Vector3(0, 2, -1)) < 0.0001f
				&& Vector3.Distance(normals[1], new Vector3(0, 0, -1)) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Twist(Axis=Y, Angle=90) -> min-end {points[0]} (expected (1,0,0)), max-end {points[1]} (expected (0,2,-1))\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Transform")]
		static void TestTransform()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// A COMBINED (non-single-axis) rotation - Quaternion.Euler's own internal composition order (see
			// Transform.cs's own doc comment) can only be verified against the real thing from inside Unity, since
			// NodraCore.Tests has no UnityEngine.Quaternion to compare against at all. This is that verification:
			// it runs the native transform of a plain unit vector and compares it directly against the real
			// Quaternion.Euler * vector for the exact same angles.
			var eulerDegrees = new Vector3(30f, 45f, 60f);
			var testVector = Vector3.forward;

			var data = new GeoData();
			data.AddPoint(testVector, testVector, Vector2.zero);

			var (points, normals) = NodraNative.Transform(data, Vector3.zero, eulerDegrees, Vector3.one);
			var expected = Quaternion.Euler(eulerDegrees) * testVector;

			var ok = Vector3.Distance(points[0], expected) < 0.001f && Vector3.Distance(normals[0], expected) < 0.001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Transform(Rotation=(30,45,60)) on (0,0,1) -> {points[0]} (expected {expected}, from real Quaternion.Euler)\n\n" +
				(ok
					? "Looks correct - Euler composition order matches Unity's own."
					: "UNEXPECTED - Quaternion.Euler's internal composition order (see Transform.cs's own doc comment) doesn't match Unity's real behavior here - marshaling or formula mismatch."),
				"OK");
		}

		[MenuItem("Nodra/Test Native Random Transform")]
		static void TestRandomTransform()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 1, 1), Vector3.up, Vector2.zero);

			var (pointsA, normalsA) = NodraNative.RandomTransform(data, new Vector3(1, 1, 1), 45f, 7);
			var (pointsB, normalsB) = NodraNative.RandomTransform(data, new Vector3(1, 1, 1), 45f, 7);

			var ok = Vector3.Distance(pointsA[0], pointsB[0]) < 0.0001f && Vector3.Distance(pointsA[1], pointsB[1]) < 0.0001f
				&& Vector3.Distance(normalsA[0], normalsB[0]) < 0.0001f && Vector3.Distance(normalsA[1], normalsB[1]) < 0.0001f;
			EditorUtility.DisplayDialog("Nodra Native",
				$"RandomTransform(seed=7) called twice -> identical both times: {ok}\n\n{(ok ? "Looks correct - deterministic for a given seed." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Slice")]
		static void TestSlice()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Same quad + cut plane as Native/NodraCore.Tests/Program.cs's own "Slice clips a quad..." check -
			// keeping the +X half of a unit quad straddling x=0.5 leaves 2 original + 2 new points, one 4-corner
			// primitive.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(0, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 1), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 1, 2, 3);

			NodraNative.Slice(data, new Vector3(0.5f, 0, 0), Vector3.right);

			var ok = data.PointCount == 6 && data.Primitives.Count == 1 && data.Primitives[0].Length == 4;
			EditorUtility.DisplayDialog("Nodra Native",
				$"Slice(Center=(0.5,0,0), Normal=+X) on 1 quad -> {data.PointCount} points (expected 6), {data.Primitives.Count} primitive (expected 1, 4-corner)\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Set Attribute")]
		static void TestSetAttribute()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// Random mode is deterministic for a given seed - same case Native/NodraCore.Tests/Program.cs's own
			// "SetAttribute.RandomT..." check, but exercised through the real node (Remap/Write/GetAttribute
			// included) rather than the wrapper alone.
			var data = new GeoData();
			data.AddPoint(Vector3.zero, Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 1, 1), Vector3.up, Vector2.zero);

			var node = new SetAttributeNode { AttributeName = "Density", Mode = SetAttributeNode.ValueMode.Random, RandomSeed = 7, Remap = new Vector2(0f, 10f) };
			node.Process(new[] { data });

			var value0 = data.GetAttribute("Density", 0);
			var value1 = data.GetAttribute("Density", 1);

			var ok = value0 >= 0f && value0 <= 10f && value1 >= 0f && value1 <= 10f && !Mathf.Approximately(value0, value1);
			EditorUtility.DisplayDialog("Nodra Native",
				$"SetAttribute(Mode=Random, seed=7, Remap=(0,10)) -> point0={value0:0.00}, point1={value1:0.00}\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}

		[MenuItem("Nodra/Test Native Remove Unused Points")]
		static void TestRemoveUnusedPoints()
		{
			if (!NodraNative.IsAvailable)
			{
				EditorUtility.DisplayDialog("Nodra Native", "NodraCore native library isn't available for this platform/build - check Sources/Plugins and the Plugin Inspector import settings.", "OK");
				return;
			}

			// 3 points, only 0 and 2 referenced by a primitive - point 1 should be dropped and the primitive's
			// own indices remapped to match, same case Native/NodraCore.Tests/Program.cs already checks directly.
			var data = new GeoData();
			data.AddPoint(new Vector3(0, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(1, 0, 0), Vector3.up, Vector2.zero);
			data.AddPoint(new Vector3(2, 0, 0), Vector3.up, Vector2.zero);
			data.AddPrimitive(0, 2);

			new RemoveUnusedPointsNode().Process(data);

			var ok = data.PointCount == 2 && Vector3.Distance(data.Points[1], new Vector3(2, 0, 0)) < 0.0001f && data.Primitives[0].SequenceEqual(new[] { 0, 1 });
			EditorUtility.DisplayDialog("Nodra Native",
				$"RemoveUnusedPoints(3 points, 1 unused) -> {data.PointCount} points (expected 2), primitive [{string.Join(",", data.Primitives[0])}] (expected [0,1])\n\n{(ok ? "Looks correct." : "UNEXPECTED - marshaling or algorithm mismatch.")}",
				"OK");
		}
	}
}
