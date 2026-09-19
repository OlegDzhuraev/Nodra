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
using System.Runtime.InteropServices;
using UnityEngine;

namespace Nodra
{
	/// <summary> P/Invoke boundary into the NodraCore native library, built separately via NativeAOT from
	/// Native/NodraCore. IsAvailable is false only where the matching binary genuinely isn't present (wrong OS,
	/// not yet built for a platform, a stripped/corrupted copy).
	///
	/// Every buffer is pinned via GCHandle rather than `fixed` pointers, and every struct below uses IntPtr
	/// rather than typed pointers, so this file never needs AllowUnsafeCode on Nodra.asmdef - IntPtr and a typed
	/// pointer are both just pointer-sized fields under LayoutKind.Sequential, so this blits identically against
	/// the native side's own unsafe, pointer-typed mirror structs (Native/NodraCore/NativeExports.cs). </summary>
	public static class NodraNative
	{
		const string Library = "Nodra.Core";

		[DllImport(Library, EntryPoint = "nodra_core_ping")]
		static extern int Ping_Native(int value);

		static bool? available;

		public static bool IsAvailable => available ??= Probe();

		static bool Probe()
		{
			try
			{
				Ping_Native(0);
				return true;
			}
			catch (DllNotFoundException) { return false; }
			catch (EntryPointNotFoundException) { return false; }
			catch (BadImageFormatException) { return false; }
		}

		/// <summary> Smoke test only, proving the managed/native call boundary works - see Nodra/Test Native Ping
		/// (NodraNativeMenu.cs). </summary>
		public static int Ping(int value) => Ping_Native(value);

		[StructLayout(LayoutKind.Sequential)]
		struct WeldInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public float Distance;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct WeldOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public IntPtr Remap;
		}

		[DllImport(Library, EntryPoint = "nodra_core_weld")]
		static extern void Weld_Native(ref WeldInput input, ref WeldOutput output);

		public struct WeldResult
		{
			public List<Vector3> Points;
			public List<Vector3> Normals;
			public List<Vector2> Uvs;
			public List<Color> Colors;
			public List<int[]> Primitives;

			/// <summary> Old point index -> new (possibly shared) point index, length == the ORIGINAL point count -
			/// feed straight into GeoData.RemapAttributes so named per-point attributes merge the same way. </summary>
			public int[] Remap;
		}

		/// <summary> WeldNode's algorithm - see Native/NodraCore/Weld.cs. Every output list is sized to at most
		/// data.PointCount/data.Primitives.Count: welding only ever merges points together or drops whole
		/// degenerate primitives, never grows either count, so that's always a safe upper bound. </summary>
		public static WeldResult Weld(GeoData data, float distance)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];
			var uvs = new float[pointCount * 2];
			var colors = new float[pointCount * 4];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;

				var color = data.Colors[i];
				colors[i * 4 + 0] = color.r;
				colors[i * 4 + 1] = color.g;
				colors[i * 4 + 2] = color.b;
				colors[i * 4 + 3] = color.a;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outPoints = new float[pointCount * 3];
			var outNormals = new float[pointCount * 3];
			var outUvs = new float[pointCount * 2];
			var outColors = new float[pointCount * 4];
			var outPrimitiveIndices = new int[totalIndices];
			var outPrimitiveLengths = new int[primitiveCount];
			var outRemap = new int[pointCount];

			var handles = new List<GCHandle>(11);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new WeldInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					Distance = distance,
				};

				var output = new WeldOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					Uvs = Pin(outUvs),
					Colors = Pin(outColors),
					PrimitiveIndices = Pin(outPrimitiveIndices),
					PrimitiveLengths = Pin(outPrimitiveLengths),
					Remap = Pin(outRemap),
				};

				Weld_Native(ref input, ref output);

				var result = new WeldResult
				{
					Points = new List<Vector3>(output.PointCount),
					Normals = new List<Vector3>(output.PointCount),
					Uvs = new List<Vector2>(output.PointCount),
					Colors = new List<Color>(output.PointCount),
					Primitives = new List<int[]>(output.PrimitiveCount),
					Remap = outRemap,
				};

				for (var i = 0; i < output.PointCount; i++)
				{
					result.Points.Add(new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]));
					result.Normals.Add(new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]));
					result.Uvs.Add(new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]));
					result.Colors.Add(new Color(outColors[i * 4 + 0], outColors[i * 4 + 1], outColors[i * 4 + 2], outColors[i * 4 + 3]));
				}

				var readCursor = 0;
				for (var i = 0; i < output.PrimitiveCount; i++)
				{
					var length = outPrimitiveLengths[i];
					var primitive = new int[length];
					Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
					result.Primitives.Add(primitive);
					readCursor += length;
				}

				return result;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct SmoothByAngleInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public float AngleThresholdDegrees;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct SmoothByAngleOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int NewPointCount;
			public IntPtr SmoothGroups;
		}

		[DllImport(Library, EntryPoint = "nodra_core_smooth_by_angle")]
		static extern void SmoothByAngle_Native(ref SmoothByAngleInput input, ref SmoothByAngleOutput output);

		public struct SmoothByAngleResult
		{
			public List<Vector3> Points;
			public List<Vector3> Normals;
			public List<Vector2> Uvs;
			public List<Color> Colors;

			/// <summary> Per point (index-aligned with Points/Normals/Uvs/Colors above) SmoothGroup value - 0
			/// means "never touched", feed straight into GeoData.SetAttribute(GeoData.SmoothGroupAttribute, ...). </summary>
			public float[] SmoothGroups;
		}

		/// <summary> SmoothByAngleNode's algorithm - see Native/NodraCore/SmoothByAngle.cs. Unlike Weld, points
		/// only ever GROW (every duplicated corner is a brand new point), so every output buffer is sized to
		/// data.PointCount + (sum of every primitive's length) - the worst case where every corner duplicates.
		/// Primitives are mutated IN PLACE since their shape/length never changes, only some corners' index
		/// values. </summary>
		public static SmoothByAngleResult SmoothByAngle(GeoData data, float angleThresholdDegrees)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var maxPointCount = pointCount + totalIndices;

			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];
			var uvs = new float[pointCount * 2];
			var colors = new float[pointCount * 4];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;

				var color = data.Colors[i];
				colors[i * 4 + 0] = color.r;
				colors[i * 4 + 1] = color.g;
				colors[i * 4 + 2] = color.b;
				colors[i * 4 + 3] = color.a;
			}

			// Mutated in place by the native call - doubles as both the input and the output buffer, since
			// SmoothByAngle only ever rewrites corner VALUES, never a primitive's own length or count.
			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outPoints = new float[maxPointCount * 3];
			var outNormals = new float[maxPointCount * 3];
			var outUvs = new float[maxPointCount * 2];
			var outColors = new float[maxPointCount * 4];
			var outSmoothGroups = new float[maxPointCount];

			var handles = new List<GCHandle>(11);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new SmoothByAngleInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					AngleThresholdDegrees = angleThresholdDegrees,
				};

				var output = new SmoothByAngleOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					Uvs = Pin(outUvs),
					Colors = Pin(outColors),
					SmoothGroups = Pin(outSmoothGroups),
				};

				SmoothByAngle_Native(ref input, ref output);

				var result = new SmoothByAngleResult
				{
					Points = new List<Vector3>(output.NewPointCount),
					Normals = new List<Vector3>(output.NewPointCount),
					Uvs = new List<Vector2>(output.NewPointCount),
					Colors = new List<Color>(output.NewPointCount),
					SmoothGroups = new float[output.NewPointCount],
				};

				for (var i = 0; i < output.NewPointCount; i++)
				{
					result.Points.Add(new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]));
					result.Normals.Add(new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]));
					result.Uvs.Add(new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]));
					result.Colors.Add(new Color(outColors[i * 4 + 0], outColors[i * 4 + 1], outColors[i * 4 + 2], outColors[i * 4 + 3]));
					result.SmoothGroups[i] = outSmoothGroups[i];
				}

				// Primitives were mutated in place - scatter the (possibly rewritten) flattened indices back into
				// data's own Primitives arrays, same shapes/lengths as before.
				var readCursor = 0;
				for (var i = 0; i < primitiveCount; i++)
				{
					var primitive = data.Primitives[i];
					Array.Copy(primitiveIndices, readCursor, primitive, 0, primitive.Length);
					readCursor += primitive.Length;
				}

				return result;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}
		}

		// Shared by every generator wrapper below - Points/Normals/Uvs/PrimitiveIndices is the one output shape
		// all of them need (matches NativeExports.cs's own GeneratorOutput on the native side field-for-field).
		[StructLayout(LayoutKind.Sequential)]
		struct GeneratorOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr PrimitiveIndices;
		}

		// Allocates Points/Normals/Uvs (sized to pointCount) + a PrimitiveIndices buffer (sized to indexCount),
		// pins all four, invokes callNative with the populated GeneratorOutput, and returns the plain managed
		// arrays read back out of it - every *Generator method below just supplies its own native call and then
		// walks primitiveIndices itself (lengths differ per generator - quads for most, a triangle fan for
		// CylinderGenerator's caps - so there's no one-size-fits-all way to turn it back into GeoData.Primitives
		// entries here).
		static (float[] points, float[] normals, float[] uvs, int[] primitiveIndices) RunGenerator(
			int pointCount, int indexCount, Action<GeneratorOutput> callNative)
		{
			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];
			var uvs = new float[pointCount * 2];
			var primitiveIndices = new int[indexCount];

			var handles = new List<GCHandle>(4);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var output = new GeneratorOutput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					PrimitiveIndices = Pin(primitiveIndices),
				};

				callNative(output);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return (points, normals, uvs, primitiveIndices);
		}

		// Appends pointCount points (read from the flat buffers RunGenerator returned) onto data via AddPoint,
		// same as every managed generator original's own `input ?? new GeoData()` + running point-count offset -
		// a generator is normally the first node in a chain, but nothing stops a graph from feeding one geometry
		// anyway. Returns the PointCount data had BEFORE appending, since every primitive index read out of
		// primitiveIndices is 0-based and needs that same offset added before calling data.AddPrimitive.
		static int AppendGeneratedPoints(GeoData data, int pointCount, float[] points, float[] normals, float[] uvs)
		{
			var startIndex = data.PointCount;

			for (var i = 0; i < pointCount; i++)
				data.AddPoint(
					new Vector3(points[i * 3 + 0], points[i * 3 + 1], points[i * 3 + 2]),
					new Vector3(normals[i * 3 + 0], normals[i * 3 + 1], normals[i * 3 + 2]),
					new Vector2(uvs[i * 2 + 0], uvs[i * 2 + 1]));

			return startIndex;
		}

		[DllImport(Library, EntryPoint = "nodra_core_box_generator")]
		static extern void BoxGenerator_Native(float sizeX, float sizeY, float sizeZ, ref GeneratorOutput output);

		// Mirrors Native/NodraCore/BoxGenerator.cs's own PointCount/PrimitiveCount constants - NOT a reference to
		// that type, which lives only in the separate native project and is never compiled into (or referenced
		// by) this Unity assembly; Nodra.asmdef's own "zero dependencies" rule means the two sides can only ever
		// agree by both hardcoding the same fixed shape, same as every clamp/formula duplicated elsewhere in
		// this file.
		const int BoxPointCount = 24;
		const int BoxPrimitiveCount = 6;

		/// <summary> BoxGeneratorNode's algorithm - see Native/NodraCore/BoxGenerator.cs. Fixed output shape
		/// (24 points, 6 quads) regardless of Size. </summary>
		public static void BoxGenerator(GeoData data, Vector3 size)
		{
			const int pointCount = BoxPointCount;
			const int primitiveCount = BoxPrimitiveCount;

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, primitiveCount * 4,
				output => BoxGenerator_Native(size.x, size.y, size.z, ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);
			AppendQuadPrimitives(data, primitiveCount, primitiveIndices, startIndex);
		}

		[DllImport(Library, EntryPoint = "nodra_core_sphere_generator")]
		static extern void SphereGenerator_Native(float radius, int columns, int rows, ref GeneratorOutput output);

		/// <summary> SphereGeneratorNode's algorithm - see Native/NodraCore/SphereGenerator.cs.
		/// columnsRequested/rowsRequested are clamped here (Mathf.Max(3, ...)/Mathf.Max(2, ...)) purely to size
		/// the output buffers before the call - SphereGenerator.Run applies the exact same clamp internally, so
		/// the two have to agree. </summary>
		public static void SphereGenerator(GeoData data, float radius, int columnsRequested, int rowsRequested)
		{
			var columns = Mathf.Max(3, columnsRequested);
			var rows = Mathf.Max(2, rowsRequested);
			var pointCount = (rows + 1) * (columns + 1);
			var primitiveCount = rows * columns;

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, primitiveCount * 4,
				output => SphereGenerator_Native(radius, columnsRequested, rowsRequested, ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);
			AppendQuadPrimitives(data, primitiveCount, primitiveIndices, startIndex);
		}

		[DllImport(Library, EntryPoint = "nodra_core_grid_generator")]
		static extern void GridGenerator_Native(float sizeX, float sizeY, int columns, int rows, ref GeneratorOutput output);

		/// <summary> GridGeneratorNode's algorithm - see Native/NodraCore/GridGenerator.cs. Same
		/// Resolution-clamped sizing story as SphereGenerator above (Mathf.Max(1, ...) here, matching
		/// GridGenerator.Run's own clamp). </summary>
		public static void GridGenerator(GeoData data, Vector2 size, int columnsRequested, int rowsRequested)
		{
			var columns = Mathf.Max(1, columnsRequested);
			var rows = Mathf.Max(1, rowsRequested);
			var pointCount = (rows + 1) * (columns + 1);
			var primitiveCount = rows * columns;

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, primitiveCount * 4,
				output => GridGenerator_Native(size.x, size.y, columnsRequested, rowsRequested, ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);
			AppendQuadPrimitives(data, primitiveCount, primitiveIndices, startIndex);
		}

		[DllImport(Library, EntryPoint = "nodra_core_torus_generator")]
		static extern void TorusGenerator_Native(float majorRadius, float minorRadius, int majorSegments, int minorSegments, ref GeneratorOutput output);

		/// <summary> TorusGeneratorNode's algorithm - see Native/NodraCore/TorusGenerator.cs. Same
		/// Resolution-clamped sizing story as SphereGenerator above (Mathf.Max(3, ...) for both segment counts) -
		/// both loops wrap fully closed, so there's no cap/degenerate-end branch to also mirror, unlike
		/// CylinderGenerator below. </summary>
		public static void TorusGenerator(GeoData data, float majorRadius, float minorRadius, int majorSegmentsRequested, int minorSegmentsRequested)
		{
			var majorSegments = Mathf.Max(3, majorSegmentsRequested);
			var minorSegments = Mathf.Max(3, minorSegmentsRequested);
			var pointCount = (minorSegments + 1) * (majorSegments + 1);
			var primitiveCount = minorSegments * majorSegments;

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, primitiveCount * 4,
				output => TorusGenerator_Native(majorRadius, minorRadius, majorSegmentsRequested, minorSegmentsRequested, ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);
			AppendQuadPrimitives(data, primitiveCount, primitiveIndices, startIndex);
		}

		// Shared tail for every generator above whose primitives are ALL 4-index quads (every one of them except
		// CylinderGenerator, which mixes in 3-index cap-fan triangles) - reads primitiveCount consecutive groups
		// of 4 straight out of the flattened buffer.
		static void AppendQuadPrimitives(GeoData data, int primitiveCount, int[] primitiveIndices, int startIndex)
		{
			for (var p = 0; p < primitiveCount; p++)
			{
				var indices = new int[4];
				for (var j = 0; j < 4; j++)
					indices[j] = startIndex + primitiveIndices[p * 4 + j];

				data.AddPrimitive(indices);
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_cylinder_generator")]
		static extern void CylinderGenerator_Native(float radiusBottom, float radiusTop, float height, int segments,
			int heightSegments, byte capBottom, byte capTop, ref GeneratorOutput output);

		/// <summary> CylinderGeneratorNode's algorithm - see Native/NodraCore/CylinderGenerator.cs. Point/
		/// primitive counts depend on HeightSegments AND on whether each cap actually gets added (CapBottom/
		/// CapTop AND that end's own radius > 0) - both checks are replicated here verbatim, matching
		/// CylinderGenerator.Run's own, purely to size the output buffers before the call. Unlike the quad-only
		/// generators above, primitives here are NOT uniform: side quads (4 indices) come first, then up to two
		/// triangle fans (3 indices each) for the caps - CylinderGenerator.Run always emits them in that exact
		/// order, so this reads them back the same way instead of needing a PrimitiveLengths array on the wire. </summary>
		public static void CylinderGenerator(GeoData data, float radiusBottom, float radiusTop, float height,
			int segmentsRequested, int heightSegmentsRequested, bool capBottom, bool capTop)
		{
			var segments = Mathf.Max(3, segmentsRequested);
			var heightSegments = Mathf.Max(1, heightSegmentsRequested);
			var hasBottomCap = capBottom && Mathf.Max(0f, radiusBottom) > 0f;
			var hasTopCap = capTop && Mathf.Max(0f, radiusTop) > 0f;

			var ringPointCount = (heightSegments + 1) * (segments + 1);
			var capPointCount = segments + 1 + 1; // a fan's own ring plus its center point
			var pointCount = ringPointCount + (hasBottomCap ? capPointCount : 0) + (hasTopCap ? capPointCount : 0);

			var sidePrimitiveCount = heightSegments * segments;
			var indexCount = sidePrimitiveCount * 4 + (hasBottomCap ? segments * 3 : 0) + (hasTopCap ? segments * 3 : 0);

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, indexCount,
				output => CylinderGenerator_Native(radiusBottom, radiusTop, height, segmentsRequested, heightSegmentsRequested,
					(byte) (capBottom ? 1 : 0), (byte) (capTop ? 1 : 0), ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);

			var readCursor = 0;

			for (var p = 0; p < sidePrimitiveCount; p++)
			{
				var indices = new int[4];
				for (var j = 0; j < 4; j++)
					indices[j] = startIndex + primitiveIndices[readCursor++];

				data.AddPrimitive(indices);
			}

			for (var cap = 0; cap < 2; cap++)
			{
				if (cap == 0 ? !hasBottomCap : !hasTopCap)
					continue;

				for (var p = 0; p < segments; p++)
				{
					var indices = new int[3];
					for (var j = 0; j < 3; j++)
						indices[j] = startIndex + primitiveIndices[readCursor++];

					data.AddPrimitive(indices);
				}
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_noise_displace")]
		static extern void NoiseDisplace_Native(IntPtr points, IntPtr normals, int pointCount, int noiseType,
			float amplitude, float frequency, float offsetX, float offsetY, byte alongNormal, IntPtr outPoints);

		/// <summary> NoiseDisplaceNode's algorithm - Voronoi is a faithful, bit-identical port (see Native/
		/// NodraCore/NoiseDisplace.cs); Perlin is NOT - UnityEngine.Mathf.PerlinNoise runs inside Unity's own
		/// closed-source native engine and can't be reproduced outside it, so this uses a different, from-scratch
		/// gradient noise instead (see that file's own comment). Points only ever move along their own normal (or
		/// +Y) - Normals/Uvs/Colors/Primitives never change, so this returns just the new positions rather than
		/// mutating the whole GeoData. noiseType is NoiseDisplaceNode.NoiseType cast to int rather than referenced
		/// directly, keeping this file free of any dependency on a specific node's own nested types. </summary>
		public static Vector3[] NoiseDisplace(GeoData data, int noiseType, float amplitude, float frequency,
			float offsetX, float offsetY, bool alongNormal)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;
			}

			var outPoints = new float[pointCount * 3];
			var handles = new List<GCHandle>(3);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				NoiseDisplace_Native(Pin(points), Pin(normals), pointCount, noiseType, amplitude, frequency,
					offsetX, offsetY, (byte) (alongNormal ? 1 : 0), Pin(outPoints));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var result = new Vector3[pointCount];
			for (var i = 0; i < pointCount; i++)
				result[i] = new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]);

			return result;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ArrayModifierInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public int Count;
			public float OffsetX, OffsetY, OffsetZ;
			public float RotationX, RotationY, RotationZ;
			public float RadialOffsetX, RadialOffsetY, RadialOffsetZ;
			public float PivotX, PivotY, PivotZ;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ArrayModifierOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public IntPtr PrimitiveIndices;
		}

		[DllImport(Library, EntryPoint = "nodra_core_array")]
		static extern void ArrayModifier_Native(ref ArrayModifierInput input, ref ArrayModifierOutput output);

		/// <summary> ArrayNode's algorithm - see Native/NodraCore/ArrayModifier.cs. Fully deterministic sizing
		/// (sourceCount * Count points/uvs/colors, sourcePrimitiveCount * Count primitives, each copy's
		/// primitives the exact same shapes as data's own, just index-shifted). </summary>
		public static void ArrayModifier(GeoData data, int countRequested, Vector3 offset, Vector3 rotationEulerDegrees, Vector3 radialOffset, Vector3 pivot)
		{
			var sourceCount = data.PointCount;
			var count = Mathf.Max(1, countRequested);
			var sourcePrimitiveCount = data.Primitives.Count;
			var sourceTotalIndices = 0;
			foreach (var primitive in data.Primitives)
				sourceTotalIndices += primitive.Length;

			var points = new float[sourceCount * 3];
			var normals = new float[sourceCount * 3];
			var uvs = new float[sourceCount * 2];
			var colors = new float[sourceCount * 4];

			for (var i = 0; i < sourceCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;

				var color = data.Colors[i];
				colors[i * 4 + 0] = color.r;
				colors[i * 4 + 1] = color.g;
				colors[i * 4 + 2] = color.b;
				colors[i * 4 + 3] = color.a;
			}

			var primitiveIndices = new int[sourceTotalIndices];
			var primitiveLengths = new int[sourcePrimitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < sourcePrimitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var newPointCount = sourceCount * count;
			var newTotalIndices = sourceTotalIndices * count;

			var outPoints = new float[newPointCount * 3];
			var outNormals = new float[newPointCount * 3];
			var outUvs = new float[newPointCount * 2];
			var outColors = new float[newPointCount * 4];
			var outPrimitiveIndices = new int[newTotalIndices];

			var handles = new List<GCHandle>(9);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new ArrayModifierInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = sourceCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = sourcePrimitiveCount,
					Count = countRequested,
					OffsetX = offset.x, OffsetY = offset.y, OffsetZ = offset.z,
					RotationX = rotationEulerDegrees.x, RotationY = rotationEulerDegrees.y, RotationZ = rotationEulerDegrees.z,
					RadialOffsetX = radialOffset.x, RadialOffsetY = radialOffset.y, RadialOffsetZ = radialOffset.z,
					PivotX = pivot.x, PivotY = pivot.y, PivotZ = pivot.z,
				};

				var output = new ArrayModifierOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					Uvs = Pin(outUvs),
					Colors = Pin(outColors),
					PrimitiveIndices = Pin(outPrimitiveIndices),
				};

				ArrayModifier_Native(ref input, ref output);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			data.Points.Clear();
			data.Normals.Clear();
			data.Uvs.Clear();
			data.Colors.Clear();
			data.Primitives.Clear();

			for (var i = 0; i < newPointCount; i++)
				data.AddPoint(
					new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]),
					new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]),
					new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]),
					new Color(outColors[i * 4 + 0], outColors[i * 4 + 1], outColors[i * 4 + 2], outColors[i * 4 + 3]));

			// Sparse named attributes are Unity-side-only bookkeeping the native call has no concept of (same as
			// every other wrapper here) - each copy's Nth point is a plain positional/rotated copy of the
			// SOURCE's own Nth point, so it copies that same source index's attributes, matching ArrayNode.
			// Process's own CopyAttributes(newIndex, i) call exactly.
			for (var i = 0; i < newPointCount; i++)
				data.CopyAttributes(i, i % sourceCount);

			var readCursor = 0;
			for (var copy = 0; copy < count; copy++)
			for (var p = 0; p < sourcePrimitiveCount; p++)
			{
				var length = primitiveLengths[p];
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
				data.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct AutoUVInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public int Projection;
			public float Scale;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct AutoUVOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public int NewPointCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_auto_uv")]
		static extern void AutoUV_Native(ref AutoUVInput input, ref AutoUVOutput output);

		/// <summary> AutoUVNode's algorithm - see Native/NodraCore/AutoUV.cs. Like SmoothByAngle, points only
		/// ever GROW (a wrapped projection's seam correction duplicates a corner), so every output buffer is
		/// sized to data.PointCount + (sum of every primitive's length). Primitives are mutated IN PLACE since
		/// their shape/length never changes, only some corners' index values (which may now point past the
		/// original point range into a newly duplicated one). </summary>
		public static void AutoUV(GeoData data, int projection, float scale)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var maxPointCount = pointCount + totalIndices;

			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;
			}

			// Mutated in place by the native call - AutoUV only ever rewrites corner VALUES, never a primitive's
			// own length or count.
			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outPoints = new float[maxPointCount * 3];
			var outNormals = new float[maxPointCount * 3];
			var outUvs = new float[maxPointCount * 2];

			var handles = new List<GCHandle>(8);
			int newPointCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new AutoUVInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					Projection = projection,
					Scale = scale,
				};

				var output = new AutoUVOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					Uvs = Pin(outUvs),
				};

				AutoUV_Native(ref input, ref output);
				newPointCount = output.NewPointCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			data.Points.Clear();
			data.Normals.Clear();
			data.Uvs.Clear();

			for (var i = 0; i < newPointCount; i++)
			{
				data.Points.Add(new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]));
				data.Normals.Add(new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]));
				data.Uvs.Add(new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]));
			}

			// Colors/attributes are untouched for every pre-existing point; a newly duplicated corner (beyond the
			// original PointCount) gets a plain default white color and no attribute values of its own, matching
			// AutoUVNode.ApplyWrapped's own 3-arg AddPoint call exactly (see AutoUV.cs's own comment on why).
			while (data.Colors.Count < newPointCount)
				data.Colors.Add(Color.white);

			// Primitives were mutated in place - scatter the (possibly rewritten) flattened indices back into
			// data's own Primitives arrays, same shapes/lengths as before.
			var readCursor = 0;
			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				Array.Copy(primitiveIndices, readCursor, primitive, 0, primitive.Length);
				readCursor += primitive.Length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_bend")]
		static extern void Bend_Native(IntPtr points, int pointCount, int axis, float centerX, float centerY, float centerZ,
			float angleDegrees, IntPtr outPoints);

		/// <summary> BendNode's algorithm - see Native/NodraCore/Bend.cs. Points only ever move within the plane
		/// defined by Axis - Normals/Uvs/Colors/Primitives never change (GeoMeshBuilder recalculates normals
		/// after baking), so this returns just the new positions rather than mutating the whole GeoData. axis is
		/// BendNode.Axis3D cast to int rather than referenced directly, keeping this file free of any dependency
		/// on a specific node's own nested types. </summary>
		public static Vector3[] Bend(GeoData data, int axis, Vector3 center, float angleDegrees)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var outPoints = new float[pointCount * 3];
			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				Bend_Native(Pin(points), pointCount, axis, center.x, center.y, center.z, angleDegrees, Pin(outPoints));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var result = new Vector3[pointCount];
			for (var i = 0; i < pointCount; i++)
				result[i] = new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]);

			return result;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct CapHolesInput
		{
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct CapHolesOutput
		{
			public IntPtr NewPrimitiveIndices;
			public IntPtr NewPrimitiveLengths;
			public int NewPrimitiveCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_cap_holes")]
		static extern void CapHoles_Native(ref CapHolesInput input, ref CapHolesOutput output);

		/// <summary> CapHolesNode's algorithm - see Native/NodraCore/CapHoles.cs. Purely additive - existing
		/// points/primitives never change, only new cap primitives get appended - so every output buffer is
		/// sized to data's own total index count, a safe (if loose) upper bound: every new cap primitive's
		/// corners come from data's own boundary edges, of which there can never be more than that. </summary>
		public static void CapHoles(GeoData data)
		{
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outPrimitiveIndices = new int[totalIndices];
			var outPrimitiveLengths = new int[totalIndices];

			var handles = new List<GCHandle>(5);
			int newPrimitiveCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new CapHolesInput
				{
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
				};

				var output = new CapHolesOutput
				{
					NewPrimitiveIndices = Pin(outPrimitiveIndices),
					NewPrimitiveLengths = Pin(outPrimitiveLengths),
				};

				CapHoles_Native(ref input, ref output);
				newPrimitiveCount = output.NewPrimitiveCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var readCursor = 0;
			for (var i = 0; i < newPrimitiveCount; i++)
			{
				var length = outPrimitiveLengths[i];
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
				data.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_circle_generator")]
		static extern void CircleGenerator_Native(float radius, int segments, byte fill, ref GeneratorOutput output);

		/// <summary> CircleGeneratorNode's algorithm - see Native/NodraCore/CircleGenerator.cs. Point/primitive
		/// counts depend on Segments (Mathf.Max(3, ...), same clamp story as every Resolution-driven generator
		/// above) AND on Fill - a bare ring has no primitives at all. </summary>
		public static void CircleGenerator(GeoData data, float radius, int segmentsRequested, bool fill)
		{
			var segments = Mathf.Max(3, segmentsRequested);
			var pointCount = (fill ? 1 : 0) + (segments + 1);
			var primitiveCount = fill ? segments : 0;

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, primitiveCount * 3,
				output => CircleGenerator_Native(radius, segmentsRequested, (byte) (fill ? 1 : 0), ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);
			AppendTrianglePrimitives(data, primitiveCount, primitiveIndices, startIndex);
		}

		// Shared tail for a generator whose primitives are all 3-index triangles (only CircleGenerator so far,
		// unlike AppendQuadPrimitives' four generators) - reads primitiveCount consecutive groups of 3.
		static void AppendTrianglePrimitives(GeoData data, int primitiveCount, int[] primitiveIndices, int startIndex)
		{
			for (var p = 0; p < primitiveCount; p++)
			{
				var indices = new int[3];
				for (var j = 0; j < 3; j++)
					indices[j] = startIndex + primitiveIndices[p * 3 + j];

				data.AddPrimitive(indices);
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct FaceFilterInput
		{
			public IntPtr Points;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public float DirectionX, DirectionY, DirectionZ;
			public float MaxAngleDegrees;
			public byte Invert;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct FaceFilterOutput
		{
			public IntPtr KeptPrimitiveIndices;
			public IntPtr KeptPrimitiveLengths;
			public int KeptPrimitiveCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_face_filter")]
		static extern void FaceFilter_Native(ref FaceFilterInput input, ref FaceFilterOutput output);

		/// <summary> FaceFilterNode's algorithm - see Native/NodraCore/FaceFilter.cs. Points never change - only
		/// which primitives survive - so every output buffer is sized to data's own current counts: filtering
		/// only ever drops whole primitives, never adds or grows one, so that's always a safe upper bound (same
		/// reasoning as Weld's own output sizing). </summary>
		public static void FaceFilter(GeoData data, Vector3 direction, float maxAngleDegrees, bool invert)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outPrimitiveIndices = new int[totalIndices];
			var outPrimitiveLengths = new int[primitiveCount];

			var handles = new List<GCHandle>(6);
			int keptCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new FaceFilterInput
				{
					Points = Pin(points),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					DirectionX = direction.x, DirectionY = direction.y, DirectionZ = direction.z,
					MaxAngleDegrees = maxAngleDegrees,
					Invert = (byte) (invert ? 1 : 0),
				};

				var output = new FaceFilterOutput
				{
					KeptPrimitiveIndices = Pin(outPrimitiveIndices),
					KeptPrimitiveLengths = Pin(outPrimitiveLengths),
				};

				FaceFilter_Native(ref input, ref output);
				keptCount = output.KeptPrimitiveCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			data.Primitives.Clear();

			var readCursor = 0;
			for (var i = 0; i < keptCount; i++)
			{
				var length = outPrimitiveLengths[i];
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
				data.Primitives.Add(primitive);
				readCursor += length;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ExtrudeInput
		{
			public IntPtr Points;
			public IntPtr Uvs;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public float Distance;
			public byte CapNewFace;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ExtrudeOutput
		{
			public IntPtr TopPoints;
			public IntPtr TopNormals;
			public IntPtr TopUvs;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_extrude")]
		static extern void Extrude_Native(ref ExtrudeInput input, ref ExtrudeOutput output);

		/// <summary> ExtrudeNode's algorithm - see Native/NodraCore/Extrude.cs. TopPoints/TopNormals/TopUvs are
		/// always exactly data.PointCount - one offset copy per original point. Primitives (cap copies plus
		/// boundary walls) are NOT deterministically sized, so every output buffer here allocates a safe (loose)
		/// upper bound instead: at most primitiveCount + totalIndices new primitives, at most 4*totalIndices
		/// total corners across them - see Extrude.cs's own comment for the derivation. Native's own Primitives
		/// already reference the combined [0, 2*originalPointCount) index space Extrude.cs documents - since
		/// TopPoints get appended directly after data's own pre-existing points below, that space lines up with
		/// data's real indices with no remapping needed at all. </summary>
		public static void Extrude(GeoData data, float distance, bool capNewFace)
		{
			var originalPointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[originalPointCount * 3];
			var uvs = new float[originalPointCount * 2];

			for (var i = 0; i < originalPointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outTopPoints = new float[originalPointCount * 3];
			var outTopNormals = new float[originalPointCount * 3];
			var outTopUvs = new float[originalPointCount * 2];

			var maxPrimitiveCount = primitiveCount + totalIndices;
			var maxTotalCornerCount = totalIndices + 4 * totalIndices;
			var outPrimitiveIndices = new int[maxTotalCornerCount];
			var outPrimitiveLengths = new int[maxPrimitiveCount];

			var handles = new List<GCHandle>(9);
			int newPrimitiveCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new ExtrudeInput
				{
					Points = Pin(points),
					Uvs = Pin(uvs),
					PointCount = originalPointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					Distance = distance,
					CapNewFace = (byte) (capNewFace ? 1 : 0),
				};

				var output = new ExtrudeOutput
				{
					TopPoints = Pin(outTopPoints),
					TopNormals = Pin(outTopNormals),
					TopUvs = Pin(outTopUvs),
					PrimitiveIndices = Pin(outPrimitiveIndices),
					PrimitiveLengths = Pin(outPrimitiveLengths),
				};

				Extrude_Native(ref input, ref output);
				newPrimitiveCount = output.PrimitiveCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < originalPointCount; i++)
			{
				var topIndex = data.AddPoint(
					new Vector3(outTopPoints[i * 3 + 0], outTopPoints[i * 3 + 1], outTopPoints[i * 3 + 2]),
					new Vector3(outTopNormals[i * 3 + 0], outTopNormals[i * 3 + 1], outTopNormals[i * 3 + 2]),
					new Vector2(outTopUvs[i * 2 + 0], outTopUvs[i * 2 + 1]));
				data.CopyAttributes(topIndex, i);
			}

			// The original primitives were consumed by the extrusion (their footprint is now either the new
			// cap - a remapped copy at the top points - or bridged by boundary walls), so they don't survive
			// at their old position - same "clear before re-adding" contract as every other wrapper that fully
			// replaces Primitives (e.g. ArrayModifier/Subdivide above).
			data.Primitives.Clear();

			var readCursor = 0;
			for (var i = 0; i < newPrimitiveCount; i++)
			{
				var length = outPrimitiveLengths[i];
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
				data.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_delete_points_random")]
		static extern void DeletePointsRandom_Native(int pointCount, float chance, int randomSeed, IntPtr keepFlags);

		/// <summary> DeletePointsNode's Random mode - see Native/NodraCore/DeletePoints.cs (Attribute mode never
		/// needed native at all). A seeded System.Random is safe to reproduce outside Unity here, unlike
		/// Mathf.PerlinNoise - see that file's own doc comment. </summary>
		public static bool[] DeletePointsRandom(int pointCount, float chance, int randomSeed)
		{
			var keepFlags = new byte[pointCount];
			var handle = GCHandle.Alloc(keepFlags, GCHandleType.Pinned);

			try
			{
				DeletePointsRandom_Native(pointCount, chance, randomSeed, handle.AddrOfPinnedObject());
			}
			finally
			{
				handle.Free();
			}

			var keep = new bool[pointCount];
			for (var i = 0; i < pointCount; i++)
				keep[i] = keepFlags[i] != 0;

			return keep;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct CopyToPointsInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr ScaleMultipliers;
			public int PointCount;
			public IntPtr SourceVertices;
			public IntPtr SourceNormals;
			public IntPtr SourceUvs;
			public int SourceVertexCount;
			public IntPtr SourceTriangles;
			public int SourceTriangleCount;
			public byte AlignToNormal;
			public float RandomYRotationDegreesMax;
			public float UniformScaleMin;
			public float UniformScaleMax;
			public int RandomSeed;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct CopyToPointsOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr PrimitiveIndices;
		}

		[DllImport(Library, EntryPoint = "nodra_core_copy_to_points")]
		static extern void CopyToPoints_Native(ref CopyToPointsInput input, ref CopyToPointsOutput output);

		/// <summary> CopyToPointsNode's algorithm - see Native/NodraCore/CopyToPoints.cs. Reads mesh's own
		/// vertices/normals/uvs/triangles here (a UnityEngine.Mesh can't cross the native boundary), padding a
		/// short/missing normal or UV channel with Vector3.up/Vector2.zero so Native/NodraCore/CopyToPoints.cs can
		/// assume every source array is already exactly mesh.vertexCount long. scaleAttributeName (Unity-side-only,
		/// same as every sparse-attribute case elsewhere in this file) is resolved into a plain per-point
		/// multiplier array before the call rather than crossing the wire as a name. Fully deterministic sizing
		/// (data.PointCount * mesh.vertexCount points, data.PointCount * (mesh.triangles.Length/3) triangles).
		/// Builds and returns a brand-new GeoData, matching CopyToPointsNode.Process's own `new GeoData()` (it
		/// doesn't mutate data). </summary>
		public static GeoData CopyToPoints(GeoData data, Mesh mesh, bool alignToNormal, float randomYRotationMax,
			float uniformScaleMin, float uniformScaleMax, string scaleAttributeName, int randomSeed)
		{
			var output = new GeoData();

			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];
			var scaleMultipliers = new float[pointCount];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = i < data.Normals.Count ? data.Normals[i] : Vector3.up;
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				scaleMultipliers[i] = string.IsNullOrEmpty(scaleAttributeName) ? 1f : data.GetAttribute(scaleAttributeName, i, 1f);
			}

			var sourceVerticesRaw = mesh.vertices;
			var sourceNormalsRaw = mesh.normals;
			var sourceUvsRaw = mesh.uv;
			var sourceTrianglesRaw = mesh.triangles;
			var sourceVertexCount = sourceVerticesRaw.Length;

			var sourceVertices = new float[sourceVertexCount * 3];
			var sourceNormals = new float[sourceVertexCount * 3];
			var sourceUvs = new float[sourceVertexCount * 2];

			for (var i = 0; i < sourceVertexCount; i++)
			{
				var vertex = sourceVerticesRaw[i];
				sourceVertices[i * 3 + 0] = vertex.x;
				sourceVertices[i * 3 + 1] = vertex.y;
				sourceVertices[i * 3 + 2] = vertex.z;

				var vertexNormal = i < sourceNormalsRaw.Length ? sourceNormalsRaw[i] : Vector3.up;
				sourceNormals[i * 3 + 0] = vertexNormal.x;
				sourceNormals[i * 3 + 1] = vertexNormal.y;
				sourceNormals[i * 3 + 2] = vertexNormal.z;

				var uv = i < sourceUvsRaw.Length ? sourceUvsRaw[i] : Vector2.zero;
				sourceUvs[i * 2 + 0] = uv.x;
				sourceUvs[i * 2 + 1] = uv.y;
			}

			var sourceTriangleCount = sourceTrianglesRaw.Length / 3;
			var outPointCount = pointCount * sourceVertexCount;
			var outPrimitiveCount = pointCount * sourceTriangleCount;

			var outPoints = new float[outPointCount * 3];
			var outNormals = new float[outPointCount * 3];
			var outUvs = new float[outPointCount * 2];
			var outPrimitiveIndices = new int[outPrimitiveCount * 3];

			var handles = new List<GCHandle>(10);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new CopyToPointsInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					ScaleMultipliers = Pin(scaleMultipliers),
					PointCount = pointCount,
					SourceVertices = Pin(sourceVertices),
					SourceNormals = Pin(sourceNormals),
					SourceUvs = Pin(sourceUvs),
					SourceVertexCount = sourceVertexCount,
					SourceTriangles = Pin(sourceTrianglesRaw),
					SourceTriangleCount = sourceTriangleCount,
					AlignToNormal = (byte) (alignToNormal ? 1 : 0),
					RandomYRotationDegreesMax = randomYRotationMax,
					UniformScaleMin = uniformScaleMin,
					UniformScaleMax = uniformScaleMax,
					RandomSeed = randomSeed,
				};

				var outputNative = new CopyToPointsOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					Uvs = Pin(outUvs),
					PrimitiveIndices = Pin(outPrimitiveIndices),
				};

				CopyToPoints_Native(ref input, ref outputNative);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < outPointCount; i++)
				output.AddPoint(
					new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]),
					new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]),
					new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]));

			for (var p = 0; p < outPrimitiveCount; p++)
				output.AddPrimitive(outPrimitiveIndices[p * 3 + 0], outPrimitiveIndices[p * 3 + 1], outPrimitiveIndices[p * 3 + 2]);

			return output;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ChamferInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public float Distance;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ChamferOutput
		{
			public IntPtr InsetPoints;
			public IntPtr InsetNormals;
			public IntPtr InsetUvs;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_chamfer")]
		static extern void Chamfer_Native(ref ChamferInput input, ref ChamferOutput output);

		/// <summary> ChamferNode's algorithm - see Native/NodraCore/Chamfer.cs. InsetPoints/InsetNormals/
		/// InsetUvs are always exactly data's own total index count - one inset corner per original primitive
		/// corner, deterministic. Primitives (bridge facets, vertex-fan caps, then each primitive's own inset
		/// copy) are NOT deterministically sized, so every output buffer here allocates a safe (loose) upper
		/// bound instead: at most 2*totalIndices + primitiveCount new primitives, at most 4*totalIndices total
		/// corners across them - see Chamfer.cs's own comment for the derivation. </summary>
		public static void Chamfer(GeoData data, float distance)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];
			var uvs = new float[pointCount * 2];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outInsetPoints = new float[totalIndices * 3];
			var outInsetNormals = new float[totalIndices * 3];
			var outInsetUvs = new float[totalIndices * 2];

			var maxPrimitiveCount = 2 * totalIndices + primitiveCount;
			var maxTotalCornerCount = 4 * totalIndices;
			var outPrimitiveIndices = new int[maxTotalCornerCount];
			var outPrimitiveLengths = new int[maxPrimitiveCount];

			var handles = new List<GCHandle>(11);
			int newPrimitiveCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new ChamferInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					Distance = distance,
				};

				var output = new ChamferOutput
				{
					InsetPoints = Pin(outInsetPoints),
					InsetNormals = Pin(outInsetNormals),
					InsetUvs = Pin(outInsetUvs),
					PrimitiveIndices = Pin(outPrimitiveIndices),
					PrimitiveLengths = Pin(outPrimitiveLengths),
				};

				Chamfer_Native(ref input, ref output);
				newPrimitiveCount = output.PrimitiveCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var insetStartIndex = data.PointCount;

			for (var i = 0; i < totalIndices; i++)
				data.AddPoint(
					new Vector3(outInsetPoints[i * 3 + 0], outInsetPoints[i * 3 + 1], outInsetPoints[i * 3 + 2]),
					new Vector3(outInsetNormals[i * 3 + 0], outInsetNormals[i * 3 + 1], outInsetNormals[i * 3 + 2]),
					new Vector2(outInsetUvs[i * 2 + 0], outInsetUvs[i * 2 + 1]));

			// Sparse named attributes have no native-side concept (same as every other wrapper here) - each inset
			// point was generated in the exact same primitive-major, slot-minor order the SOURCE primitives are
			// walked in below (still intact - cleared only after this loop), so replaying that order recovers
			// which original point index each inset point came from, matching ChamferNode.BuildInsetFace's own
			// CopyAttributes(insetIndices[slot], originalIndex) call exactly.
			var insetCursor = insetStartIndex;
			for (var p = 0; p < primitiveCount; p++)
			{
				var primitive = data.Primitives[p];
				for (var slot = 0; slot < primitive.Length; slot++)
				{
					data.CopyAttributes(insetCursor, primitive[slot]);
					insetCursor++;
				}
			}

			data.Primitives.Clear();

			var readCursor = 0;
			for (var i = 0; i < newPrimitiveCount; i++)
			{
				var length = outPrimitiveLengths[i];
				var primitive = new int[length];
				for (var j = 0; j < length; j++)
					primitive[j] = insetStartIndex + outPrimitiveIndices[readCursor + j];

				data.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_icosphere_generator")]
		static extern void IcoSphereGenerator_Native(float radius, int subdivisions, ref GeneratorOutput output);

		/// <summary> IcoSphereGeneratorNode's algorithm - see Native/NodraCore/IcoSphereGenerator.cs. Point/
		/// primitive counts depend on Subdivisions via the exact edge-count recurrence a closed triangle mesh
		/// always follows (unique edges = 3*PrimitiveCount/2, and subdividing adds exactly one new point per
		/// unique edge) - not just a Mathf.Max clamp, since each level's own output feeds the next level's
		/// count. </summary>
		public static void IcoSphereGenerator(GeoData data, float radius, int subdivisionsRequested)
		{
			var subdivisions = Mathf.Max(0, subdivisionsRequested);
			var pointCount = 12;
			var primitiveCount = 20;

			for (var i = 0; i < subdivisions; i++)
			{
				pointCount += (primitiveCount * 3) / 2;
				primitiveCount *= 4;
			}

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, primitiveCount * 3,
				output => IcoSphereGenerator_Native(radius, subdivisionsRequested, ref output));

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);
			AppendTrianglePrimitives(data, primitiveCount, primitiveIndices, startIndex);
		}

		[StructLayout(LayoutKind.Sequential)]
		struct MirrorInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public int Axis;
			public float Offset;
			public float WeldDistance;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct MirrorOutput
		{
			public IntPtr NewPoints;
			public IntPtr NewNormals;
			public IntPtr NewUvs;
			public IntPtr NewColors;
			public int NewPointCount;
			public IntPtr Remap;
			public IntPtr MirroredPrimitiveIndices;
			public IntPtr MirroredPrimitiveLengths;
		}

		[DllImport(Library, EntryPoint = "nodra_core_mirror")]
		static extern void Mirror_Native(ref MirrorInput input, ref MirrorOutput output);

		/// <summary> MirrorNode's algorithm - see Native/NodraCore/Mirror.cs. NewPoints/NewNormals/NewUvs/
		/// NewColors are NOT deterministically sized (depends how many points already sit on the mirror plane
		/// within WeldDistance), so every output buffer here allocates a safe upper bound (sourcePointCount)
		/// instead and reads back NewPointCount. Remap and MirroredPrimitiveIndices/Lengths ARE deterministic
		/// (exactly sourcePointCount and the input's own primitive shapes respectively). Remap's own values
		/// already sit in the combined index space Mirror.cs documents (untouched original index, or
		/// sourcePointCount + k meaning the k-th NewPoints entry) - appending NewPoints straight after data's own
		/// pre-existing points below lines that space up with data's real indices automatically, same trick
		/// Extrude uses. </summary>
		public static void Mirror(GeoData data, int axis, float offset, float weldDistance)
		{
			var sourcePointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[sourcePointCount * 3];
			var normals = new float[sourcePointCount * 3];
			var uvs = new float[sourcePointCount * 2];
			var colors = new float[sourcePointCount * 4];

			for (var i = 0; i < sourcePointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;

				var color = data.Colors[i];
				colors[i * 4 + 0] = color.r;
				colors[i * 4 + 1] = color.g;
				colors[i * 4 + 2] = color.b;
				colors[i * 4 + 3] = color.a;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outNewPoints = new float[sourcePointCount * 3];
			var outNewNormals = new float[sourcePointCount * 3];
			var outNewUvs = new float[sourcePointCount * 2];
			var outNewColors = new float[sourcePointCount * 4];
			var outRemap = new int[sourcePointCount];
			var outMirroredIndices = new int[totalIndices];
			var outMirroredLengths = new int[primitiveCount];

			var handles = new List<GCHandle>(13);
			int newPointCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new MirrorInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = sourcePointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					Axis = axis,
					Offset = offset,
					WeldDistance = weldDistance,
				};

				var output = new MirrorOutput
				{
					NewPoints = Pin(outNewPoints),
					NewNormals = Pin(outNewNormals),
					NewUvs = Pin(outNewUvs),
					NewColors = Pin(outNewColors),
					Remap = Pin(outRemap),
					MirroredPrimitiveIndices = Pin(outMirroredIndices),
					MirroredPrimitiveLengths = Pin(outMirroredLengths),
				};

				Mirror_Native(ref input, ref output);
				newPointCount = output.NewPointCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			// Recovers, for each new point, which ORIGINAL point it derives from - Remap only records the other
			// direction (old -> new), and CopyAttributes needs old -> new to carry sparse named attributes over.
			var sourceOfNewPoint = new int[newPointCount];
			for (var i = 0; i < sourcePointCount; i++)
				if (outRemap[i] != i)
					sourceOfNewPoint[outRemap[i] - sourcePointCount] = i;

			for (var i = 0; i < newPointCount; i++)
			{
				var newIndex = data.AddPoint(
					new Vector3(outNewPoints[i * 3 + 0], outNewPoints[i * 3 + 1], outNewPoints[i * 3 + 2]),
					new Vector3(outNewNormals[i * 3 + 0], outNewNormals[i * 3 + 1], outNewNormals[i * 3 + 2]),
					new Vector2(outNewUvs[i * 2 + 0], outNewUvs[i * 2 + 1]),
					new Color(outNewColors[i * 4 + 0], outNewColors[i * 4 + 1], outNewColors[i * 4 + 2], outNewColors[i * 4 + 3]));
				data.CopyAttributes(newIndex, sourceOfNewPoint[i]);
			}

			var readCursor = 0;
			for (var i = 0; i < primitiveCount; i++)
			{
				var length = outMirroredLengths[i];
				var primitive = new int[length];
				Array.Copy(outMirroredIndices, readCursor, primitive, 0, length);
				data.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_relax")]
		static extern void Relax_Native(IntPtr points, int pointCount, IntPtr primitiveIndices, IntPtr primitiveLengths, int primitiveCount,
			float factor, int iterations, byte preserveBoundary, IntPtr outPoints);

		/// <summary> RelaxNode's algorithm - see Native/NodraCore/Relax.cs. Points only ever move - Normals/Uvs/
		/// Colors/Primitives never change (GeoMeshBuilder recalculates normals after baking) - so this returns
		/// just the relaxed positions rather than mutating the whole GeoData. </summary>
		public static void Relax(GeoData data, float factor, int iterationsRequested, bool preserveBoundary)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outPoints = new float[pointCount * 3];
			var handles = new List<GCHandle>(4);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				Relax_Native(Pin(points), pointCount, Pin(primitiveIndices), Pin(primitiveLengths), primitiveCount,
					factor, iterationsRequested, (byte) (preserveBoundary ? 1 : 0), Pin(outPoints));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < pointCount; i++)
				data.Points[i] = new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]);
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ScatterInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public int ScatterPointCount;
			public int RandomSeed;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct ScatterOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr TriangleI0;
			public IntPtr TriangleI1;
			public IntPtr TriangleI2;
			public IntPtr WeightA;
			public IntPtr WeightB;
			public IntPtr WeightC;
		}

		[DllImport(Library, EntryPoint = "nodra_core_scatter")]
		static extern void Scatter_Native(ref ScatterInput input, ref ScatterOutput output);

		/// <summary> ScatterNode's algorithm - see Native/NodraCore/Scatter.cs. Fully deterministic sizing (every
		/// output array is exactly the requested PointCount long). Sparse named attributes have no native-side
		/// concept, so this reads back each scattered point's own source triangle/barycentric weights and calls
		/// GeoData.BlendAttributesFrom itself. Builds and returns a brand-new GeoData, matching
		/// ScatterNode.Process's own `new GeoData()` (it doesn't mutate data). </summary>
		public static GeoData Scatter(GeoData data, int scatterPointCountRequested, int randomSeed)
		{
			var output = new GeoData();

			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var scatterPointCount = Mathf.Max(0, scatterPointCountRequested);
			var outPoints = new float[scatterPointCount * 3];
			var outNormals = new float[scatterPointCount * 3];
			var outTriangleI0 = new int[scatterPointCount];
			var outTriangleI1 = new int[scatterPointCount];
			var outTriangleI2 = new int[scatterPointCount];
			var outWeightA = new float[scatterPointCount];
			var outWeightB = new float[scatterPointCount];
			var outWeightC = new float[scatterPointCount];

			var handles = new List<GCHandle>(13);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new ScatterInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					ScatterPointCount = scatterPointCountRequested,
					RandomSeed = randomSeed,
				};

				var outputNative = new ScatterOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					TriangleI0 = Pin(outTriangleI0),
					TriangleI1 = Pin(outTriangleI1),
					TriangleI2 = Pin(outTriangleI2),
					WeightA = Pin(outWeightA),
					WeightB = Pin(outWeightB),
					WeightC = Pin(outWeightC),
				};

				Scatter_Native(ref input, ref outputNative);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < scatterPointCount; i++)
			{
				var index = output.AddPoint(
					new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]),
					new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]),
					Vector2.zero);

				output.BlendAttributesFrom(data, index,
					(outTriangleI0[i], outWeightA[i]), (outTriangleI1[i], outWeightB[i]), (outTriangleI2[i], outWeightC[i]));
			}

			return output;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct SubdivideInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public int Iterations;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct SubdivideOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int NewPointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public IntPtr BlendSourceIndices;
			public IntPtr BlendSourceLengths;
			public int BlendSourceCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_subdivide")]
		static extern void Subdivide_Native(ref SubdivideInput input, ref SubdivideOutput output);

		/// <summary> SubdivideNode's algorithm - see Native/NodraCore/Subdivide.cs. Points are NOT
		/// deterministically sized (depends how many shared-by-index edges get their midpoint reused rather than
		/// duplicated), so Points/Normals/Uvs/Colors allocate a safe upper bound and read back NewPointCount.
		/// Primitives ARE fully deterministic (always uniform quads) via the exact same recurrence Subdivide.Run
		/// itself follows: each iteration's new primitive count equals the PREVIOUS iteration's own total corner
		/// count - reported back anyway as PrimitiveCount as a cheap consistency check. BlendSourceIndices/
		/// Lengths (one entry per NEW point - 2 for a midpoint, N for a centroid) size the same worst-case way
		/// Points does; every new point gets GeoData.BlendAttributes(index, sources) called on it directly - a
		/// uniform average, the exact same operation for both a 2-source midpoint and an N-source centroid. </summary>
		public static void Subdivide(GeoData data, int iterationsRequested)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];
			var uvs = new float[pointCount * 2];
			var colors = new float[pointCount * 4];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;

				var color = data.Colors[i];
				colors[i * 4 + 0] = color.r;
				colors[i * 4 + 1] = color.g;
				colors[i * 4 + 2] = color.b;
				colors[i * 4 + 3] = color.a;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var iterations = Mathf.Max(1, iterationsRequested);
			var currentPrimitiveCount = primitiveCount;
			var currentTotalIndices = totalIndices;
			var maxNewPointCount = 0;
			var maxBlendSourceEntries = 0;

			for (var i = 0; i < iterations; i++)
			{
				maxNewPointCount += currentPrimitiveCount + currentTotalIndices;
				maxBlendSourceEntries += currentTotalIndices + 2 * currentTotalIndices;

				currentPrimitiveCount = currentTotalIndices;
				currentTotalIndices *= 4;
			}

			var maxPointCount = pointCount + maxNewPointCount;
			var finalPrimitiveCount = currentPrimitiveCount;
			var finalTotalCornerCount = currentTotalIndices;

			var outPoints = new float[maxPointCount * 3];
			var outNormals = new float[maxPointCount * 3];
			var outUvs = new float[maxPointCount * 2];
			var outColors = new float[maxPointCount * 4];
			var outPrimitiveIndices = new int[finalTotalCornerCount];
			var outPrimitiveLengths = new int[finalPrimitiveCount];
			var outBlendSourceIndices = new int[maxBlendSourceEntries];
			var outBlendSourceLengths = new int[maxNewPointCount];

			var handles = new List<GCHandle>(15);
			int newPointCount;
			int newPrimitiveCount;
			int blendSourceCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new SubdivideInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					Iterations = iterationsRequested,
				};

				var output = new SubdivideOutput
				{
					Points = Pin(outPoints),
					Normals = Pin(outNormals),
					Uvs = Pin(outUvs),
					Colors = Pin(outColors),
					PrimitiveIndices = Pin(outPrimitiveIndices),
					PrimitiveLengths = Pin(outPrimitiveLengths),
					BlendSourceIndices = Pin(outBlendSourceIndices),
					BlendSourceLengths = Pin(outBlendSourceLengths),
				};

				Subdivide_Native(ref input, ref output);
				newPointCount = output.NewPointCount;
				newPrimitiveCount = output.PrimitiveCount;
				blendSourceCount = output.BlendSourceCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			data.Points.Clear();
			data.Normals.Clear();
			data.Uvs.Clear();
			data.Colors.Clear();

			for (var i = 0; i < newPointCount; i++)
			{
				data.Points.Add(new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]));
				data.Normals.Add(new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]));
				data.Uvs.Add(new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]));
				data.Colors.Add(new Color(outColors[i * 4 + 0], outColors[i * 4 + 1], outColors[i * 4 + 2], outColors[i * 4 + 3]));
			}

			var blendReadCursor = 0;
			for (var i = 0; i < blendSourceCount; i++)
			{
				var length = outBlendSourceLengths[i];
				var sources = new int[length];
				Array.Copy(outBlendSourceIndices, blendReadCursor, sources, 0, length);
				data.BlendAttributes(pointCount + i, sources);
				blendReadCursor += length;
			}

			data.Primitives.Clear();

			var primitiveReadCursor = 0;
			for (var i = 0; i < newPrimitiveCount; i++)
			{
				var length = outPrimitiveLengths[i];
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, primitiveReadCursor, primitive, 0, length);
				data.AddPrimitive(primitive);
				primitiveReadCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_spline_generator")]
		static extern void SplineGenerator_Native(IntPtr controlPoints, int controlPointCount, int pointCount, byte closed, IntPtr outPoints);

		/// <summary> SplineGeneratorNode's algorithm - see Native/NodraCore/SplineGenerator.cs. Output size is
		/// exactly Mathf.Max(2, PointCount), points-only (normal always Vector3.up, uv.x the same [0, 1]
		/// parametrization the position sample already used) - Unity computes both directly rather than crossing
		/// the wire for them. </summary>
		public static void SplineGenerator(GeoData data, List<Vector3> controlPoints, int pointCountRequested, bool closed)
		{
			var controlPointCount = controlPoints.Count;
			var controlPointsFlat = new float[controlPointCount * 3];

			for (var i = 0; i < controlPointCount; i++)
			{
				var point = controlPoints[i];
				controlPointsFlat[i * 3 + 0] = point.x;
				controlPointsFlat[i * 3 + 1] = point.y;
				controlPointsFlat[i * 3 + 2] = point.z;
			}

			var pointCount = Mathf.Max(2, pointCountRequested);
			var outPoints = new float[pointCount * 3];

			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				SplineGenerator_Native(Pin(controlPointsFlat), controlPointCount, pointCountRequested, (byte) (closed ? 1 : 0), Pin(outPoints));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < pointCount; i++)
			{
				var t = closed ? i / (float) pointCount : i / (float) (pointCount - 1);
				data.AddPoint(new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]), Vector3.up, new Vector2(t, 0f));
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_vertex_color_slope")]
		static extern void VertexColorSlope_Native(IntPtr normals, int pointCount, IntPtr outT);

		/// <summary> VertexColorNode's Slope factor - see Native/NodraCore/VertexColor.cs. Gradient.Evaluate
		/// itself stays on the Unity side (a Gradient is a Unity-only asset, and evaluating one involves Unity's
		/// own color-space handling) - this only returns the per-point [0, 1] blend factor to feed into it. </summary>
		public static float[] VertexColorSlopeT(GeoData data)
		{
			var pointCount = data.PointCount;
			var normals = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;
			}

			var outT = new float[pointCount];
			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				VertexColorSlope_Native(Pin(normals), pointCount, Pin(outT));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return outT;
		}

		[DllImport(Library, EntryPoint = "nodra_core_vertex_color_height")]
		static extern void VertexColorHeight_Native(IntPtr points, int pointCount, int axis, IntPtr outT);

		/// <summary> VertexColorNode's Height factor - see Native/NodraCore/VertexColor.cs. Same Gradient.Evaluate
		/// split as VertexColorSlopeT above. </summary>
		public static float[] VertexColorHeightT(GeoData data, int axis)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var outT = new float[pointCount];
			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				VertexColorHeight_Native(Pin(points), pointCount, axis, Pin(outT));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return outT;
		}

		[DllImport(Library, EntryPoint = "nodra_core_vertex_color_bounds")]
		static extern void VertexColorBounds_Native(IntPtr points, int pointCount, int axis,
			float boundsMinX, float boundsMinY, float boundsMinZ, float boundsMaxX, float boundsMaxY, float boundsMaxZ,
			byte clampOutsideBounds, IntPtr outT, IntPtr outApplies);

		/// <summary> VertexColorNode's Bounds factor - see Native/NodraCore/VertexColor.cs. Same Gradient.Evaluate
		/// split as VertexColorSlopeT above; Applies is false for a point ClampOutsideBounds should leave
		/// untouched entirely (outside Bounds and not being clamped to its nearest end). </summary>
		public static (float[] t, bool[] applies) VertexColorBoundsT(GeoData data, int axis, Bounds bounds, bool clampOutsideBounds)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var outT = new float[pointCount];
			var outAppliesBytes = new byte[pointCount];
			var handles = new List<GCHandle>(3);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				VertexColorBounds_Native(Pin(points), pointCount, axis,
					bounds.min.x, bounds.min.y, bounds.min.z, bounds.max.x, bounds.max.y, bounds.max.z,
					(byte) (clampOutsideBounds ? 1 : 0), Pin(outT), Pin(outAppliesBytes));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var applies = new bool[pointCount];
			for (var i = 0; i < pointCount; i++)
				applies[i] = outAppliesBytes[i] != 0;

			return (outT, applies);
		}

		[StructLayout(LayoutKind.Sequential)]
		struct TubeInput
		{
			public IntPtr Path;
			public int PathPointCount;
			public int Sides;
			public float Radius;
			public float TwistDegrees;
			public byte Closed;
			public byte CapStart;
			public byte CapEnd;
		}

		[DllImport(Library, EntryPoint = "nodra_core_tube")]
		static extern void Tube_Native(ref TubeInput input, ref GeneratorOutput output);

		/// <summary> TubeNode's algorithm - see Native/NodraCore/Tube.cs. Every count here is fully deterministic
		/// from path.Count/Sides/Closed/CapStart/CapEnd alone, so every buffer below is sized exactly, not a
		/// worst-case bound. Primitives are NOT uniform when a cap is requested - side quads first, then up to two
		/// triangle fans for the caps - read back the same way CylinderGenerator's own mixed shapes are. Which
		/// PATH point each output point derives from (for CopyAttributes) is recovered by replaying Tube.cs's own
		/// fixed layout (every ring is exactly Sides+1 points sourced from that ring's path index, every cap is
		/// 1+(Sides+1) points all sourced from path index 0 or Count-1) rather than needing it reported back over
		/// the wire. </summary>
		public static void Tube(GeoData data, List<Vector3> path, int sidesRequested, float radius, float twistDegrees, bool closed, bool capStart, bool capEnd)
		{
			var sides = Mathf.Max(3, sidesRequested);
			var pathPointCount = path.Count;

			var ringPointCount = sides + 1;
			var totalRingPoints = pathPointCount * ringPointCount;
			var hasStartCap = !closed && capStart;
			var hasEndCap = !closed && capEnd;
			var capPointCount = 1 + ringPointCount;
			var pointCount = totalRingPoints + (hasStartCap ? capPointCount : 0) + (hasEndCap ? capPointCount : 0);

			var ringCount = closed ? pathPointCount : pathPointCount - 1;
			var sideQuadCount = ringCount * sides;
			var capTriangleCount = (hasStartCap ? sides : 0) + (hasEndCap ? sides : 0);
			var indexCount = sideQuadCount * 4 + capTriangleCount * 3;

			var pathFlat = new float[pathPointCount * 3];

			for (var i = 0; i < pathPointCount; i++)
			{
				var point = path[i];
				pathFlat[i * 3 + 0] = point.x;
				pathFlat[i * 3 + 1] = point.y;
				pathFlat[i * 3 + 2] = point.z;
			}

			var (points, normals, uvs, primitiveIndices) = RunGenerator(pointCount, indexCount, output =>
			{
				var pathHandle = GCHandle.Alloc(pathFlat, GCHandleType.Pinned);

				try
				{
					var input = new TubeInput
					{
						Path = pathHandle.AddrOfPinnedObject(),
						PathPointCount = pathPointCount,
						Sides = sidesRequested,
						Radius = radius,
						TwistDegrees = twistDegrees,
						Closed = (byte) (closed ? 1 : 0),
						CapStart = (byte) (capStart ? 1 : 0),
						CapEnd = (byte) (capEnd ? 1 : 0),
					};

					Tube_Native(ref input, ref output);
				}
				finally
				{
					pathHandle.Free();
				}
			});

			var startIndex = AppendGeneratedPoints(data, pointCount, points, normals, uvs);

			for (var i = 0; i < pathPointCount; i++)
				for (var slot = 0; slot < ringPointCount; slot++)
					data.CopyAttributes(startIndex + i * ringPointCount + slot, i);

			var cursor = startIndex + totalRingPoints;

			if (hasStartCap)
			{
				for (var slot = 0; slot < capPointCount; slot++)
					data.CopyAttributes(cursor + slot, 0);

				cursor += capPointCount;
			}

			if (hasEndCap)
				for (var slot = 0; slot < capPointCount; slot++)
					data.CopyAttributes(cursor + slot, pathPointCount - 1);

			var readCursor = 0;

			for (var p = 0; p < sideQuadCount; p++)
			{
				var indices = new int[4];
				for (var j = 0; j < 4; j++)
					indices[j] = startIndex + primitiveIndices[readCursor++];

				data.AddPrimitive(indices);
			}

			for (var cap = 0; cap < 2; cap++)
			{
				if (cap == 0 ? !hasStartCap : !hasEndCap)
					continue;

				for (var p = 0; p < sides; p++)
				{
					var indices = new int[3];
					for (var j = 0; j < 3; j++)
						indices[j] = startIndex + primitiveIndices[readCursor++];

					data.AddPrimitive(indices);
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct CsgMeshInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
		}

		// Points/Normals/Uvs/PrimitiveIndices/PrimitiveLengths here are NOT pinned managed arrays like every other
		// *Output struct in this file - GeoCsg's own output size can't be known before the call (a BSP split can
		// grow or shrink the polygon count arbitrarily), so the native side allocates these itself and this only
		// ever holds raw native pointers, read back via Marshal.Copy (see Csg() below) - the only "safe" way to
		// move bytes out of unmanaged memory without AllowUnsafeCode on Nodra.asmdef. CsgFree_Native must be
		// called exactly once per successful native call to release them.
		[StructLayout(LayoutKind.Sequential)]
		struct CsgOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public byte TimedOut;
		}

		[DllImport(Library, EntryPoint = "nodra_core_csg")]
		static extern void Csg_Native(int operation, ref CsgMeshInput a, ref CsgMeshInput b, ref CsgOutput output);

		[DllImport(Library, EntryPoint = "nodra_core_csg_free")]
		static extern void CsgFree_Native(ref CsgOutput output);

		/// <summary> BooleanNode's algorithm - see Native/NodraCore/GeoCsg.cs for the actual BSP-tree CSG. operation
		/// is BooleanNode.BooleanOperation cast to int, matching Native/NodraCore/GeoCsg.cs's own Operation enum
		/// order (Union, Subtract, Intersect). TimedOut on the wire (GeoCsg.CheckTimeout's own TimeoutException,
		/// which can't cross the P/Invoke boundary as a real exception) is re-thrown as a genuine TimeoutException
		/// here, so ProceduralMeshGenerator.Generate()'s existing `catch (TimeoutException)` still catches it
		/// exactly as it did before this moved to native. </summary>
		public static GeoData Csg(GeoData a, GeoData b, int operation)
		{
			var handles = new List<GCHandle>(14);

			IntPtr Pin(Array array)
			{
				var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
				handles.Add(handle);
				return handle.AddrOfPinnedObject();
			}

			CsgMeshInput BuildInput(GeoData data)
			{
				var pointCount = data.PointCount;
				var points = new float[pointCount * 3];
				var normals = new float[pointCount * 3];
				var uvs = new float[pointCount * 2];

				for (var i = 0; i < pointCount; i++)
				{
					var point = data.Points[i];
					points[i * 3 + 0] = point.x;
					points[i * 3 + 1] = point.y;
					points[i * 3 + 2] = point.z;

					var normal = data.Normals[i];
					normals[i * 3 + 0] = normal.x;
					normals[i * 3 + 1] = normal.y;
					normals[i * 3 + 2] = normal.z;

					var uv = data.Uvs[i];
					uvs[i * 2 + 0] = uv.x;
					uvs[i * 2 + 1] = uv.y;
				}

				var primitiveCount = data.Primitives.Count;
				var totalIndices = 0;
				foreach (var primitive in data.Primitives)
					totalIndices += primitive.Length;

				var primitiveIndices = new int[totalIndices];
				var primitiveLengths = new int[primitiveCount];
				var writeCursor = 0;

				for (var i = 0; i < primitiveCount; i++)
				{
					var primitive = data.Primitives[i];
					primitiveLengths[i] = primitive.Length;
					Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
					writeCursor += primitive.Length;
				}

				return new CsgMeshInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
				};
			}

			var inputA = BuildInput(a);
			var inputB = BuildInput(b);
			var output = new CsgOutput();

			try
			{
				Csg_Native(operation, ref inputA, ref inputB, ref output);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			if (output.TimedOut != 0)
				throw new TimeoutException("Nodra: Boolean operation took longer than 60s and was aborted - the input is likely too heavy or has too many separate pieces for this BSP-tree CSG.");

			var result = new GeoData();

			try
			{
				var pointCount = output.PointCount;
				var primitiveCount = output.PrimitiveCount;

				var points = new float[pointCount * 3];
				var normals = new float[pointCount * 3];
				var uvs = new float[pointCount * 2];
				Marshal.Copy(output.Points, points, 0, points.Length);
				Marshal.Copy(output.Normals, normals, 0, normals.Length);
				Marshal.Copy(output.Uvs, uvs, 0, uvs.Length);

				var primitiveLengths = new int[primitiveCount];
				Marshal.Copy(output.PrimitiveLengths, primitiveLengths, 0, primitiveCount);

				var totalIndices = 0;
				foreach (var length in primitiveLengths)
					totalIndices += length;

				var primitiveIndices = new int[totalIndices];
				Marshal.Copy(output.PrimitiveIndices, primitiveIndices, 0, totalIndices);

				for (var i = 0; i < pointCount; i++)
					result.AddPoint(
						new Vector3(points[i * 3 + 0], points[i * 3 + 1], points[i * 3 + 2]),
						new Vector3(normals[i * 3 + 0], normals[i * 3 + 1], normals[i * 3 + 2]),
						new Vector2(uvs[i * 2 + 0], uvs[i * 2 + 1]));

				var readCursor = 0;
				for (var i = 0; i < primitiveCount; i++)
				{
					var length = primitiveLengths[i];
					var primitive = new int[length];
					Array.Copy(primitiveIndices, readCursor, primitive, 0, length);
					result.AddPrimitive(primitive);
					readCursor += length;
				}
			}
			finally
			{
				CsgFree_Native(ref output);
			}

			return result;
		}

		[DllImport(Library, EntryPoint = "nodra_core_uv_transform")]
		static extern void UVTransform_Native(IntPtr uvs, int pointCount, float rotationDegrees,
			float tilingX, float tilingY, float offsetX, float offsetY, IntPtr outUvs);

		/// <summary> UVTransformNode's algorithm - see Native/NodraCore/UVTransform.cs. Uvs-only in and out -
		/// point/primitive counts and every other array are untouched. </summary>
		public static void UVTransform(GeoData data, float rotationDegrees, Vector2 tiling, Vector2 offset)
		{
			var pointCount = data.PointCount;
			var uvs = new float[pointCount * 2];

			for (var i = 0; i < pointCount; i++)
			{
				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;
			}

			var outUvs = new float[pointCount * 2];
			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				UVTransform_Native(Pin(uvs), pointCount, rotationDegrees, tiling.x, tiling.y, offset.x, offset.y, Pin(outUvs));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < pointCount; i++)
				data.Uvs[i] = new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]);
		}

		[DllImport(Library, EntryPoint = "nodra_core_taper")]
		static extern void Taper_Native(IntPtr points, int pointCount, int axis,
			float centerX, float centerY, float centerZ, float factor, IntPtr outPoints);

		/// <summary> TaperNode's algorithm - see Native/NodraCore/Taper.cs. Points-only in and out, same shape as
		/// Bend above. </summary>
		public static Vector3[] Taper(GeoData data, int axis, Vector3 center, float factor)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var outPoints = new float[pointCount * 3];
			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				Taper_Native(Pin(points), pointCount, axis, center.x, center.y, center.z, factor, Pin(outPoints));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var result = new Vector3[pointCount];
			for (var i = 0; i < pointCount; i++)
				result[i] = new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]);

			return result;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct MergeMeshInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct MergeOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public IntPtr PrimitiveIndices;
		}

		[DllImport(Library, EntryPoint = "nodra_core_merge")]
		static extern void Merge_Native(ref MergeMeshInput target, ref MergeMeshInput source, ref MergeOutput output);

		/// <summary> MergeNode's algorithm - see Native/NodraCore/Merge.cs. Output point/primitive counts are
		/// exactly target's own plus source's - always known before the call, unlike GeoCsg's unpredictable BSP
		/// output - so every buffer here is pre-sized rather than native-allocated. Output primitive LENGTHS are
		/// never reported back on the wire either: concatenation can't change them, so they're exactly target's
		/// own lengths followed by source's, which the Unity side already has. Mutates target in place (like
		/// Bend/Taper/CapHoles above) rather than returning a new GeoData, matching MergeNode.Process's own
		/// original Append(target, source) semantics. </summary>
		public static void Merge(GeoData target, GeoData source)
		{
			var handles = new List<GCHandle>(20);

			IntPtr Pin(Array array)
			{
				var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
				handles.Add(handle);
				return handle.AddrOfPinnedObject();
			}

			MergeMeshInput BuildInput(GeoData data)
			{
				var pointCount = data.PointCount;
				var points = new float[pointCount * 3];
				var normals = new float[pointCount * 3];
				var uvs = new float[pointCount * 2];
				var colors = new float[pointCount * 4];

				for (var i = 0; i < pointCount; i++)
				{
					var point = data.Points[i];
					points[i * 3 + 0] = point.x;
					points[i * 3 + 1] = point.y;
					points[i * 3 + 2] = point.z;

					var normal = data.Normals[i];
					normals[i * 3 + 0] = normal.x;
					normals[i * 3 + 1] = normal.y;
					normals[i * 3 + 2] = normal.z;

					var uv = data.Uvs[i];
					uvs[i * 2 + 0] = uv.x;
					uvs[i * 2 + 1] = uv.y;

					var color = data.Colors[i];
					colors[i * 4 + 0] = color.r;
					colors[i * 4 + 1] = color.g;
					colors[i * 4 + 2] = color.b;
					colors[i * 4 + 3] = color.a;
				}

				var primitiveCount = data.Primitives.Count;
				var totalIndices = 0;
				foreach (var primitive in data.Primitives)
					totalIndices += primitive.Length;

				var primitiveIndices = new int[totalIndices];
				var primitiveLengths = new int[primitiveCount];
				var writeCursor = 0;

				for (var i = 0; i < primitiveCount; i++)
				{
					var primitive = data.Primitives[i];
					primitiveLengths[i] = primitive.Length;
					Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
					writeCursor += primitive.Length;
				}

				return new MergeMeshInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = pointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
				};
			}

			var targetInput = BuildInput(target);
			var sourceInput = BuildInput(source);

			var outputPointCount = target.PointCount + source.PointCount;
			var outputTotalIndices = 0;
			foreach (var primitive in target.Primitives)
				outputTotalIndices += primitive.Length;
			foreach (var primitive in source.Primitives)
				outputTotalIndices += primitive.Length;

			var outPoints = new float[outputPointCount * 3];
			var outNormals = new float[outputPointCount * 3];
			var outUvs = new float[outputPointCount * 2];
			var outColors = new float[outputPointCount * 4];
			var outPrimitiveIndices = new int[outputTotalIndices];

			var output = new MergeOutput
			{
				Points = Pin(outPoints),
				Normals = Pin(outNormals),
				Uvs = Pin(outUvs),
				Colors = Pin(outColors),
				PrimitiveIndices = Pin(outPrimitiveIndices),
			};

			// Captured from the still-intact managed lists before target's own get cleared below - Merge_Native
			// never reports lengths back (concatenation can't change them), so this is the only place they're
			// available afterward.
			var primitiveLengths = new List<int>(target.Primitives.Count + source.Primitives.Count);
			foreach (var primitive in target.Primitives)
				primitiveLengths.Add(primitive.Length);
			foreach (var primitive in source.Primitives)
				primitiveLengths.Add(primitive.Length);

			try
			{
				Merge_Native(ref targetInput, ref sourceInput, ref output);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			target.Points.Clear();
			target.Normals.Clear();
			target.Uvs.Clear();
			target.Colors.Clear();
			target.Primitives.Clear();

			for (var i = 0; i < outputPointCount; i++)
				target.AddPoint(
					new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]),
					new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]),
					new Vector2(outUvs[i * 2 + 0], outUvs[i * 2 + 1]),
					new Color(outColors[i * 4 + 0], outColors[i * 4 + 1], outColors[i * 4 + 2], outColors[i * 4 + 3]));

			var readCursor = 0;
			foreach (var length in primitiveLengths)
			{
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
				target.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_flip_normals")]
		static extern void FlipNormals_Native(IntPtr normals, int pointCount,
			IntPtr primitiveIndices, IntPtr primitiveLengths, int primitiveCount,
			IntPtr outNormals, IntPtr outPrimitiveIndices);

		/// <summary> FlipNormalsNode's algorithm - see Native/NodraCore/FlipNormals.cs. Every primitive's own
		/// length is unchanged by a reversal, so - like Merge above - only the corner VALUES come back on the
		/// wire, not lengths. </summary>
		public static void FlipNormals(GeoData data)
		{
			var pointCount = data.PointCount;
			var normals = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;
			}

			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outNormals = new float[pointCount * 3];
			var outPrimitiveIndices = new int[totalIndices];
			var handles = new List<GCHandle>(6);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				FlipNormals_Native(Pin(normals), pointCount, Pin(primitiveIndices), Pin(primitiveLengths), primitiveCount,
					Pin(outNormals), Pin(outPrimitiveIndices));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < pointCount; i++)
				data.Normals[i] = new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]);

			var readCursor = 0;
			for (var i = 0; i < primitiveCount; i++)
			{
				var length = primitiveLengths[i];
				var reversed = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, reversed, 0, length);
				data.Primitives[i] = reversed;
				readCursor += length;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct LineGeneratorOutput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
		}

		[DllImport(Library, EntryPoint = "nodra_core_line_generator")]
		static extern void LineGenerator_Native(float startX, float startY, float startZ,
			float endX, float endY, float endZ, int pointCountRequested, ref LineGeneratorOutput output);

		/// <summary> LineGeneratorNode's algorithm - see Native/NodraCore/LineGenerator.cs. A point cloud, no
		/// primitives at all - like every other generator's own wrapper, this just calls AppendGeneratedPoints
		/// once the native call fills the flat output buffers. </summary>
		public static void LineGenerator(GeoData data, Vector3 start, Vector3 end, int pointCountRequested)
		{
			var pointCount = Mathf.Max(1, pointCountRequested);
			var outPoints = new float[pointCount * 3];
			var outNormals = new float[pointCount * 3];
			var outUvs = new float[pointCount * 2];

			var handles = new List<GCHandle>(3);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var output = new LineGeneratorOutput { Points = Pin(outPoints), Normals = Pin(outNormals), Uvs = Pin(outUvs) };
				LineGenerator_Native(start.x, start.y, start.z, end.x, end.y, end.z, pointCountRequested, ref output);
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			AppendGeneratedPoints(data, pointCount, outPoints, outNormals, outUvs);
		}

		[DllImport(Library, EntryPoint = "nodra_core_twist")]
		static extern void Twist_Native(IntPtr points, IntPtr normals, int pointCount, int axis,
			float centerX, float centerY, float centerZ, float angleDegrees, IntPtr outPoints, IntPtr outNormals);

		/// <summary> TwistNode's algorithm - see Native/NodraCore/Twist.cs. Points AND normals in and out - unlike
		/// Bend/Taper above, which only ever touch points. </summary>
		public static (Vector3[] points, Vector3[] normals) Twist(GeoData data, int axis, Vector3 center, float angleDegrees)
		{
			var pointCount = data.PointCount;
			var (points, normals, outPoints, outNormals, handles) = BuildPointNormalBuffers(data);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				Twist_Native(Pin(points), Pin(normals), pointCount, axis, center.x, center.y, center.z, angleDegrees, Pin(outPoints), Pin(outNormals));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return ReadPointNormalBuffers(pointCount, outPoints, outNormals);
		}

		[DllImport(Library, EntryPoint = "nodra_core_transform")]
		static extern void Transform_Native(IntPtr points, IntPtr normals, int pointCount,
			float translationX, float translationY, float translationZ,
			float eulerX, float eulerY, float eulerZ,
			float scaleX, float scaleY, float scaleZ,
			IntPtr outPoints, IntPtr outNormals);

		/// <summary> TransformNode's algorithm - see Native/NodraCore/Transform.cs. Points AND normals in and out,
		/// same shape as Twist above. Matrix4x4.TRS's own scale-then-rotate-then-translate composition is
		/// standard/unambiguous; Quaternion.Euler's own internal composition order is the one genuine fidelity
		/// risk - see Transform.cs's own doc comment and the "Test Native Transform" Editor menu item, which
		/// checks a combined rotation against real UnityEngine.Quaternion.Euler since that's the only place the
		/// real answer is available to compare against at all. </summary>
		public static (Vector3[] points, Vector3[] normals) Transform(GeoData data, Vector3 translation, Vector3 eulerDegrees, Vector3 scale)
		{
			var pointCount = data.PointCount;
			var (points, normals, outPoints, outNormals, handles) = BuildPointNormalBuffers(data);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				Transform_Native(Pin(points), Pin(normals), pointCount,
					translation.x, translation.y, translation.z,
					eulerDegrees.x, eulerDegrees.y, eulerDegrees.z,
					scale.x, scale.y, scale.z,
					Pin(outPoints), Pin(outNormals));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return ReadPointNormalBuffers(pointCount, outPoints, outNormals);
		}

		[DllImport(Library, EntryPoint = "nodra_core_random_transform")]
		static extern void RandomTransform_Native(IntPtr points, IntPtr normals, int pointCount,
			float jitterX, float jitterY, float jitterZ, float angleJitterDegrees, int randomSeed,
			IntPtr outPoints, IntPtr outNormals);

		/// <summary> RandomTransformNode's algorithm - see Native/NodraCore/RandomTransform.cs. Points AND normals
		/// in and out, same shape as Twist/Transform above. A seeded System.Random is safe to reproduce outside
		/// Unity - see Scatter.cs's own doc comment. </summary>
		public static (Vector3[] points, Vector3[] normals) RandomTransform(GeoData data, Vector3 positionJitter, float angleJitterDegrees, int randomSeed)
		{
			var pointCount = data.PointCount;
			var (points, normals, outPoints, outNormals, handles) = BuildPointNormalBuffers(data);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				RandomTransform_Native(Pin(points), Pin(normals), pointCount,
					positionJitter.x, positionJitter.y, positionJitter.z, angleJitterDegrees, randomSeed,
					Pin(outPoints), Pin(outNormals));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return ReadPointNormalBuffers(pointCount, outPoints, outNormals);
		}

		// Shared flatten/allocate step for the three Points+Normals-in, Points+Normals-out wrappers above (Twist/
		// Transform/RandomTransform) - each only differs in which extra scalars ride alongside these two arrays.
		static (float[] points, float[] normals, float[] outPoints, float[] outNormals, List<GCHandle> handles) BuildPointNormalBuffers(GeoData data)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];
			var normals = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;
			}

			return (points, normals, new float[pointCount * 3], new float[pointCount * 3], new List<GCHandle>(4));
		}

		static (Vector3[] points, Vector3[] normals) ReadPointNormalBuffers(int pointCount, float[] outPoints, float[] outNormals)
		{
			var resultPoints = new Vector3[pointCount];
			var resultNormals = new Vector3[pointCount];

			for (var i = 0; i < pointCount; i++)
			{
				resultPoints[i] = new Vector3(outPoints[i * 3 + 0], outPoints[i * 3 + 1], outPoints[i * 3 + 2]);
				resultNormals[i] = new Vector3(outNormals[i * 3 + 0], outNormals[i * 3 + 1], outNormals[i * 3 + 2]);
			}

			return (resultPoints, resultNormals);
		}

		[StructLayout(LayoutKind.Sequential)]
		struct SliceInput
		{
			public IntPtr Points;
			public IntPtr Normals;
			public IntPtr Uvs;
			public IntPtr Colors;
			public int PointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
			public float CenterX, CenterY, CenterZ;
			public float NormalX, NormalY, NormalZ;
		}

		[StructLayout(LayoutKind.Sequential)]
		struct SliceOutput
		{
			public IntPtr NewPoints;
			public IntPtr NewNormals;
			public IntPtr NewUvs;
			public IntPtr NewColors;
			public IntPtr CutFrom;
			public IntPtr CutTo;
			public IntPtr CutT;
			public int NewPointCount;
			public IntPtr PrimitiveIndices;
			public IntPtr PrimitiveLengths;
			public int PrimitiveCount;
		}

		[DllImport(Library, EntryPoint = "nodra_core_slice")]
		static extern void Slice_Native(ref SliceInput input, ref SliceOutput output);

		/// <summary> SliceNode's algorithm - see Native/NodraCore/Slice.cs. New cut points aren't deterministically
		/// sized, so - like Chamfer/Extrude above - every output buffer allocates a safe (loose) upper bound
		/// (totalIndices new points, data's own current PrimitiveCount for surviving primitives - clipping against
		/// one plane only ever keeps or drops a primitive whole, never splits it into more than one - and
		/// 2*totalIndices total corners across them) and reads back the actual counts. NewPoints append straight
		/// after data's own pre-existing points below, so Slice.cs's own combined index space (originalPointCount
		/// + k) lines up with data's real indices automatically, same trick Extrude/Mirror use - no remapping
		/// needed. CutFrom/CutTo/CutT feed straight into GeoData.BlendAttributes for each new point, since native
		/// has no concept of GeoData's own sparse named attributes (same as every wrapper here). </summary>
		public static void Slice(GeoData data, Vector3 center, Vector3 planeNormal)
		{
			var originalPointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var points = new float[originalPointCount * 3];
			var normals = new float[originalPointCount * 3];
			var uvs = new float[originalPointCount * 2];
			var colors = new float[originalPointCount * 4];

			for (var i = 0; i < originalPointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;

				var normal = data.Normals[i];
				normals[i * 3 + 0] = normal.x;
				normals[i * 3 + 1] = normal.y;
				normals[i * 3 + 2] = normal.z;

				var uv = data.Uvs[i];
				uvs[i * 2 + 0] = uv.x;
				uvs[i * 2 + 1] = uv.y;

				var color = data.Colors[i];
				colors[i * 4 + 0] = color.r;
				colors[i * 4 + 1] = color.g;
				colors[i * 4 + 2] = color.b;
				colors[i * 4 + 3] = color.a;
			}

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outNewPoints = new float[totalIndices * 3];
			var outNewNormals = new float[totalIndices * 3];
			var outNewUvs = new float[totalIndices * 2];
			var outNewColors = new float[totalIndices * 4];
			var outCutFrom = new int[totalIndices];
			var outCutTo = new int[totalIndices];
			var outCutT = new float[totalIndices];

			var maxOutTotalCorners = 2 * totalIndices;
			var outPrimitiveIndices = new int[maxOutTotalCorners];
			var outPrimitiveLengths = new int[primitiveCount];

			var handles = new List<GCHandle>(16);
			int newPointCount;
			int outPrimitiveCount;

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				var input = new SliceInput
				{
					Points = Pin(points),
					Normals = Pin(normals),
					Uvs = Pin(uvs),
					Colors = Pin(colors),
					PointCount = originalPointCount,
					PrimitiveIndices = Pin(primitiveIndices),
					PrimitiveLengths = Pin(primitiveLengths),
					PrimitiveCount = primitiveCount,
					CenterX = center.x,
					CenterY = center.y,
					CenterZ = center.z,
					NormalX = planeNormal.x,
					NormalY = planeNormal.y,
					NormalZ = planeNormal.z,
				};

				var output = new SliceOutput
				{
					NewPoints = Pin(outNewPoints),
					NewNormals = Pin(outNewNormals),
					NewUvs = Pin(outNewUvs),
					NewColors = Pin(outNewColors),
					CutFrom = Pin(outCutFrom),
					CutTo = Pin(outCutTo),
					CutT = Pin(outCutT),
					PrimitiveIndices = Pin(outPrimitiveIndices),
					PrimitiveLengths = Pin(outPrimitiveLengths),
				};

				Slice_Native(ref input, ref output);
				newPointCount = output.NewPointCount;
				outPrimitiveCount = output.PrimitiveCount;
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			for (var i = 0; i < newPointCount; i++)
			{
				var newIndex = data.AddPoint(
					new Vector3(outNewPoints[i * 3 + 0], outNewPoints[i * 3 + 1], outNewPoints[i * 3 + 2]),
					new Vector3(outNewNormals[i * 3 + 0], outNewNormals[i * 3 + 1], outNewNormals[i * 3 + 2]),
					new Vector2(outNewUvs[i * 2 + 0], outNewUvs[i * 2 + 1]),
					new Color(outNewColors[i * 4 + 0], outNewColors[i * 4 + 1], outNewColors[i * 4 + 2], outNewColors[i * 4 + 3]));

				data.BlendAttributes(newIndex, outCutFrom[i], outCutTo[i], outCutT[i]);
			}

			data.Primitives.Clear();

			var readCursor = 0;
			for (var i = 0; i < outPrimitiveCount; i++)
			{
				var length = outPrimitiveLengths[i];
				var primitive = new int[length];
				Array.Copy(outPrimitiveIndices, readCursor, primitive, 0, length);
				data.AddPrimitive(primitive);
				readCursor += length;
			}
		}

		[DllImport(Library, EntryPoint = "nodra_core_set_attribute_noise")]
		static extern void SetAttributeNoise_Native(IntPtr points, int pointCount, float frequency, float offsetX, float offsetY, IntPtr outT);

		/// <summary> SetAttributeNode's Noise mode - see Native/NodraCore/SetAttribute.cs. Same
		/// Gradient.Evaluate/Mathf.PerlinNoise split as VertexColorSlopeT above: only the raw [0, 1] factor
		/// crosses the boundary, Remap/Blend and GeoData.SetAttribute stay Unity-side (see SetAttributeNode.cs's
		/// own Write/Combine). </summary>
		public static float[] SetAttributeNoiseT(GeoData data, float frequency, Vector2 offset)
		{
			var pointCount = data.PointCount;
			var points = new float[pointCount * 3];

			for (var i = 0; i < pointCount; i++)
			{
				var point = data.Points[i];
				points[i * 3 + 0] = point.x;
				points[i * 3 + 1] = point.y;
				points[i * 3 + 2] = point.z;
			}

			var outT = new float[pointCount];
			var handles = new List<GCHandle>(2);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				SetAttributeNoise_Native(Pin(points), pointCount, frequency, offset.x, offset.y, Pin(outT));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			return outT;
		}

		[DllImport(Library, EntryPoint = "nodra_core_set_attribute_random")]
		static extern void SetAttributeRandom_Native(int pointCount, int randomSeed, IntPtr outT);

		/// <summary> SetAttributeNode's Random mode - see Native/NodraCore/SetAttribute.cs. A seeded System.Random
		/// is safe to reproduce outside Unity - see Scatter.cs's own doc comment. </summary>
		public static float[] SetAttributeRandomT(int pointCount, int randomSeed)
		{
			var outT = new float[pointCount];
			var handle = GCHandle.Alloc(outT, GCHandleType.Pinned);

			try
			{
				SetAttributeRandom_Native(pointCount, randomSeed, handle.AddrOfPinnedObject());
			}
			finally
			{
				handle.Free();
			}

			return outT;
		}

		[DllImport(Library, EntryPoint = "nodra_core_remove_unused_points")]
		static extern void RemoveUnusedPoints_Native(int pointCount, IntPtr primitiveIndices, IntPtr primitiveLengths, int primitiveCount, IntPtr outUsed);

		/// <summary> RemoveUnusedPointsNode's algorithm - see Native/NodraCore/RemoveUnusedPoints.cs. Only the
		/// `used` flags cross the boundary - GeoData.CompactPoints (the actual point removal, keeping named
		/// per-point attributes in step) and the primitive index remap both stay Unity-side regardless, since
		/// native has no concept of GeoData's own sparse attribute dictionary. </summary>
		public static bool[] RemoveUnusedPointsUsed(GeoData data)
		{
			var pointCount = data.PointCount;
			var primitiveCount = data.Primitives.Count;
			var totalIndices = 0;
			foreach (var primitive in data.Primitives)
				totalIndices += primitive.Length;

			var primitiveIndices = new int[totalIndices];
			var primitiveLengths = new int[primitiveCount];
			var writeCursor = 0;

			for (var i = 0; i < primitiveCount; i++)
			{
				var primitive = data.Primitives[i];
				primitiveLengths[i] = primitive.Length;
				Array.Copy(primitive, 0, primitiveIndices, writeCursor, primitive.Length);
				writeCursor += primitive.Length;
			}

			var outUsed = new byte[pointCount];
			var handles = new List<GCHandle>(3);

			try
			{
				IntPtr Pin(Array array)
				{
					var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
					handles.Add(handle);
					return handle.AddrOfPinnedObject();
				}

				RemoveUnusedPoints_Native(pointCount, Pin(primitiveIndices), Pin(primitiveLengths), primitiveCount, Pin(outUsed));
			}
			finally
			{
				foreach (var handle in handles)
					handle.Free();
			}

			var used = new bool[pointCount];
			for (var i = 0; i < pointCount; i++)
				used[i] = outUsed[i] != 0;

			return used;
		}
	}
}
