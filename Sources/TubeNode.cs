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
	/// <summary> Sweeps a regular polygon cross-section (Sides 3+ for a beam, higher for a round tube) along the
	/// input's points, connecting consecutive rings into a tube - discards whatever primitives the input had and
	/// replaces its points entirely. Meant to sit after LineGeneratorNode/SplineGeneratorNode (or any other
	/// points-only source); a rotation-minimizing frame is carried along the path so the cross-section doesn't
	/// twist through turns. </summary>
	[Serializable]
	public class TubeNode : GeoNode
	{
		public override string Category => "Build";

		[Min(3)] public int Sides = 8;
		[Min(0f)] public float Radius = 0.25f;
		public float Twist;

		/// <summary> Loops the last ring back onto the first instead of capping the ends - the path itself isn't
		/// required to repeat its first point (SplineGeneratorNode's own Closed doesn't). </summary>
		public bool Closed;

		public bool CapStart = true;
		public bool CapEnd = true;

		public override string GetInputPortName(int index) => "Path";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount < 2)
				return input;

			var path = new List<Vector3>(input.Points);
			input.Points.Clear();
			input.Normals.Clear();
			input.Uvs.Clear();
			input.Colors.Clear();
			input.Primitives.Clear();

			var sides = Mathf.Max(3, Sides);
			// [Min] only constrains the Inspector - re-clamped here too, since a negative Radius would reflect
			// the cross-section through the path itself rather than just shrinking it.
			var radius = Mathf.Max(0f, Radius);
			var cos = BuildAngleTable(sides, out var sin);
			var tangents = BuildTangents(path, Closed);
			var frames = BuildFrames(path, tangents, Closed);
			var v = BuildArcLengthRatios(path, Closed);

			var ringStarts = new int[path.Count];

			for (var i = 0; i < path.Count; i++)
			{
				var frame = RotateFrame(frames[i], tangents[i], Twist);
				ringStarts[i] = input.PointCount;
				AddRing(input, cos, sin, path[i], frame.right, frame.up, v[i], radius);
			}

			// Winding verified by hand against a straight +Y path (tangent (0,1,0), right (1,0,0), up =
			// Cross(tangent, right) = (0,0,-1)): (i0, i1, i3, i2) is the order whose Cross points radially
			// outward - the reverse of CylinderGeneratorNode's side wall, because this frame's "up" ends up
			// left-handed relative to the (cos, height, sin) basis Cylinder builds its own ring in.
			var ringCount = Closed ? path.Count : path.Count - 1;

			for (var i = 0; i < ringCount; i++)
			{
				var next = (i + 1) % path.Count;

				for (var s = 0; s < sides; s++)
				{
					var i0 = ringStarts[i] + s;
					var i1 = i0 + 1;
					var i2 = ringStarts[next] + s;
					var i3 = i2 + 1;

					input.AddPrimitive(i0, i1, i3, i2);
				}
			}

			if (!Closed)
			{
				// Twist-rotated the same way the ring at this same path point was above - the raw (untwisted)
				// frame would fan the cap from a different orientation than the ring boundary it's supposed to
				// close, leaving a seam/kink between the cap and the twisted side wall instead of a flat closure.
				if (CapStart)
				{
					var startFrame = RotateFrame(frames[0], tangents[0], Twist);
					AddCap(input, cos, sin, path[0], startFrame.right, startFrame.up, -tangents[0], flip: true, radius);
				}

				if (CapEnd)
				{
					var last = path.Count - 1;
					var endFrame = RotateFrame(frames[last], tangents[last], Twist);
					AddCap(input, cos, sin, path[last], endFrame.right, endFrame.up, tangents[last], flip: false, radius);
				}
			}

			return input;
		}

		static void AddRing(GeoData data, float[] cos, float[] sin, Vector3 center, Vector3 right, Vector3 up, float v, float radius)
		{
			var sides = cos.Length - 1;

			for (var s = 0; s <= sides; s++)
			{
				var offset = right * cos[s] + up * sin[s];
				var uv = new Vector2(s / (float) sides, v);

				data.AddPoint(center + offset * radius, offset, uv);
			}
		}

		// A fan from a center point, matching CylinderGeneratorNode.AddCap - flip true faces back along -tangent
		// (the start), flip false faces forward along +tangent (the end), verified against the same straight-path
		// example the side wall winding above was checked against.
		static void AddCap(GeoData data, float[] cos, float[] sin, Vector3 center, Vector3 right, Vector3 up, Vector3 normal, bool flip, float radius)
		{
			var sides = cos.Length - 1;
			var centerIndex = data.AddPoint(center, normal, new Vector2(0.5f, 0.5f));
			var ringStart = data.PointCount;

			for (var s = 0; s <= sides; s++)
			{
				var offset = right * cos[s] + up * sin[s];
				var uv = new Vector2(cos[s] * 0.5f + 0.5f, sin[s] * 0.5f + 0.5f);

				data.AddPoint(center + offset * radius, normal, uv);
			}

			for (var s = 0; s < sides; s++)
			{
				var a = ringStart + s;
				var b = a + 1;

				data.AddPrimitive(flip ? new[] { centerIndex, b, a } : new[] { centerIndex, a, b });
			}
		}

		// Central difference for interior points (smooths out sharp per-segment direction changes), forward/backward
		// difference at the open ends where there's only one neighboring segment to look at.
		static Vector3[] BuildTangents(List<Vector3> path, bool closed)
		{
			var count = path.Count;
			var tangents = new Vector3[count];

			for (var i = 0; i < count; i++)
			{
				var prev = path[closed ? (i - 1 + count) % count : Mathf.Max(i - 1, 0)];
				var next = path[closed ? (i + 1) % count : Mathf.Min(i + 1, count - 1)];
				var direction = next - prev;

				tangents[i] = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.forward;
			}

			return tangents;
		}

		// Rotation-minimizing frames (double reflection method, Wang/Jüttler/Zheng/Liu) - unlike re-deriving a
		// "right" vector fresh at every point from some fixed world axis, this carries the previous ring's frame
		// forward with the least twist possible, so a straight path stays untwisted and a curved one doesn't
		// suddenly flip where the path direction crosses whatever axis a fresh derivation would have used.
		static (Vector3 right, Vector3 up)[] BuildFrames(List<Vector3> path, Vector3[] tangents, bool closed)
		{
			var count = path.Count;
			var frames = new (Vector3 right, Vector3 up)[count];
			frames[0] = InitialFrame(tangents[0]);

			for (var i = 1; i < count; i++)
				frames[i] = NextFrame(path[i - 1], frames[i - 1], tangents[i - 1], path[i], tangents[i]);

			// A closed loop's frame drifts by whatever twist the walk above accumulated getting all the way
			// around - linearly unwound back across every ring so the seam where the last ring meets the first
			// doesn't snap into a sudden twist.
			if (closed && count > 2)
			{
				var closing = NextFrame(path[count - 1], frames[count - 1], tangents[count - 1], path[0], tangents[0]);
				var drift = Vector3.SignedAngle(closing.right, frames[0].right, tangents[0]);

				for (var i = 1; i < count; i++)
					frames[i] = RotateFrame(frames[i], tangents[i], drift * i / (count - 1));
			}

			return frames;
		}

		static (Vector3 right, Vector3 up) InitialFrame(Vector3 tangent)
		{
			var reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
			var right = Vector3.Cross(reference, tangent).normalized;

			return (right, Vector3.Cross(tangent, right));
		}

		static (Vector3 right, Vector3 up) NextFrame(Vector3 p0, (Vector3 right, Vector3 up) frame0, Vector3 t0, Vector3 p1, Vector3 t1)
		{
			var v1 = p1 - p0;
			var c1 = Vector3.Dot(v1, v1);

			if (c1 < 1e-10f)
				return frame0;

			var reflectedRight = frame0.right - (2f / c1) * Vector3.Dot(v1, frame0.right) * v1;
			var reflectedTangent = t0 - (2f / c1) * Vector3.Dot(v1, t0) * v1;

			var v2 = t1 - reflectedTangent;
			var c2 = Vector3.Dot(v2, v2);
			var right = c2 < 1e-10f ? reflectedRight : reflectedRight - (2f / c2) * Vector3.Dot(v2, reflectedRight) * v2;

			return (right.normalized, Vector3.Cross(t1, right).normalized);
		}

		static (Vector3 right, Vector3 up) RotateFrame((Vector3 right, Vector3 up) frame, Vector3 axis, float angleDegrees)
		{
			if (angleDegrees == 0f)
				return frame;

			var rotation = Quaternion.AngleAxis(angleDegrees, axis);
			return (rotation * frame.right, rotation * frame.up);
		}

		static float[] BuildArcLengthRatios(List<Vector3> path, bool closed)
		{
			var count = path.Count;
			var ratios = new float[count];
			var cumulative = 0f;

			for (var i = 1; i < count; i++)
			{
				cumulative += Vector3.Distance(path[i - 1], path[i]);
				ratios[i] = cumulative;
			}

			var total = cumulative + (closed ? Vector3.Distance(path[count - 1], path[0]) : 0f);

			if (total > 0f)
				for (var i = 0; i < count; i++)
					ratios[i] /= total;

			return ratios;
		}

		// Closes the ring back onto column 0 using its exact value rather than re-evaluating cos/sin(2*PI) - see
		// SphereGeneratorNode for why the float-precision drift matters once RecalculateNormals has to weld the seam.
		static float[] BuildAngleTable(int sides, out float[] sin)
		{
			var cos = new float[sides + 1];
			sin = new float[sides + 1];

			for (var i = 0; i < sides; i++)
			{
				var angle = i / (float) sides * Mathf.PI * 2f;
				cos[i] = Mathf.Cos(angle);
				sin[i] = Mathf.Sin(angle);
			}

			cos[sides] = cos[0];
			sin[sides] = sin[0];

			return cos;
		}
	}
}
