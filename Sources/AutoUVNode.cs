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
	public enum UVProjection
	{
		Triplanar,
		Spherical,
		Cylindrical,
	}

	/// <summary> Regenerates UVs by Projection, scaled by Scale (world units per UV tile). Not a real unwrap -
	/// Triplanar's opposite faces along the same axis project identically and can come out mirrored; Spherical/
	/// Cylindrical project the ±X-facing halves separately (seams at ±90° longitude instead of one at ±180°) to
	/// keep any single primitive's angular span small. </summary>
	[Serializable]
	public class AutoUVNode : GeoNode
	{
		public override string Category => "Color & UV";

		public UVProjection Projection = UVProjection.Triplanar;
		[Min(0.0001f)] public float Scale = 1f;

		// Coplanar primitives computed independently should agree almost exactly - this only needs to be coarse
		// enough to swallow ordinary floating-point noise between them, not so coarse it hides a real difference.
		const float AxisSnapResolution = 0.001f;

		// How close to the Y axis (a Spherical pole) a point's own XZ radius has to be before its atan2-derived
		// angle is considered meaningless and gets borrowed from a primitive neighbor instead (BorrowPoleAngles).
		const float PoleRadiusEpsilon = 1e-5f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0)
				return input;

			switch (Projection)
			{
				case UVProjection.Spherical:
					ApplyWrapped(input, SphericalUV);
					break;

				case UVProjection.Cylindrical:
					ApplyWrapped(input, CylindricalUV);
					break;

				default:
					ApplyTriplanar(input);
					break;
			}

			return input;
		}

		// Every point's HalfAngle is settled once globally, so any two primitives sharing a point always agree on
		// it. A primitive whose own corners still straddle the ±90° cut gets a private duplicate of just the
		// offending corner(s) with a corrected angle, instead of touching that point's other use elsewhere.
		void ApplyWrapped(GeoData input, Func<Vector3, float, float, Vector2> project)
		{
			var angle = new float[input.PointCount];

			for (var i = 0; i < input.PointCount; i++)
				angle[i] = HalfAngle(Mathf.Atan2(input.Points[i].z, input.Points[i].x));

			BorrowPoleAngles(input, angle);

			for (var i = 0; i < input.PointCount; i++)
				input.Uvs[i] = project(input.Points[i], angle[i], Scale);

			for (var p = 0; p < input.Primitives.Count; p++)
			{
				var primitive = input.Primitives[p];
				var reference = angle[primitive[0]];
				int[] corrected = null;

				for (var slot = 0; slot < primitive.Length; slot++)
				{
					var index = primitive[slot];
					var pointAngle = angle[index];

					if (pointAngle - reference <= Mathf.PI / 2f && pointAngle - reference >= -Mathf.PI / 2f)
						continue;

					var fixedAngle = pointAngle - reference > 0f ? pointAngle - Mathf.PI : pointAngle + Mathf.PI;
					var position = input.Points[index];

					corrected ??= (int[]) primitive.Clone();
					corrected[slot] = input.AddPoint(position, input.Normals[index], project(position, fixedAngle, Scale));
				}

				if (corrected != null)
					input.Primitives[p] = corrected;
			}
		}

		// atan2(0, 0) at a Spherical pole (every column's point collapses to the same position there) is 0
		// regardless of which column the point actually belongs to - borrowing whichever non-degenerate primitive
		// neighbor's angle instead keeps a pole point in step with its own column, not an arbitrary fixed value
		// every other column's pole point also shares.
		static void BorrowPoleAngles(GeoData input, float[] angle)
		{
			foreach (var primitive in input.Primitives)
			{
				float? donor = null;

				foreach (var index in primitive)
				{
					if (new Vector2(input.Points[index].x, input.Points[index].z).magnitude > PoleRadiusEpsilon)
						donor = angle[index];
				}

				if (donor == null)
					continue;

				foreach (var index in primitive)
					if (new Vector2(input.Points[index].x, input.Points[index].z).magnitude <= PoleRadiusEpsilon)
						angle[index] = donor.Value;
			}
		}

		static float HalfAngle(float angle)
		{
			if (angle > Mathf.PI / 2f)
				return angle - Mathf.PI;

			if (angle < -Mathf.PI / 2f)
				return angle + Mathf.PI;

			return angle;
		}

		void ApplyTriplanar(GeoData input)
		{
			foreach (var primitive in input.Primitives)
			{
				var normal = ComputeFaceNormal(input, primitive);

				var absNormal = new Vector3(
					SnapAxis(Mathf.Abs(normal.x)),
					SnapAxis(Mathf.Abs(normal.y)),
					SnapAxis(Mathf.Abs(normal.z)));

				foreach (var index in primitive)
				{
					var position = input.Points[index];

					var projected = absNormal.x >= absNormal.y && absNormal.x >= absNormal.z ? new Vector2(position.z, position.y)
						: absNormal.y >= absNormal.z ? new Vector2(position.x, position.z)
						: new Vector2(position.x, position.y);

					input.Uvs[index] = projected / Scale;
				}
			}
		}

		// Angle (already HalfAngle-bounded by the caller) times radius - arc length in world units, so it tiles
		// consistently with Triplanar's world-units-per-tile Scale instead of a fixed number of wraps regardless
		// of the object's size.
		static Vector2 CylindricalUV(Vector3 position, float angle, float scale)
		{
			var radius = new Vector2(position.x, position.z).magnitude;
			return new Vector2(angle * radius, position.y) / scale;
		}

		// Longitude (already HalfAngle-bounded)/colatitude around the origin, each times the local arc radius - same
		// world-units-per-tile idea as CylindricalUV, just measured along the sphere's surface instead of a
		// straight cylinder wall.
		static Vector2 SphericalUV(Vector3 position, float angle, float scale)
		{
			var radius = position.magnitude;
			if (radius <= 0f)
				return Vector2.zero;

			var colatitude = Mathf.Acos(Mathf.Clamp(position.y / radius, -1f, 1f));
			return new Vector2(angle * radius * Mathf.Sin(colatitude), colatitude * radius) / scale;
		}

		static float SnapAxis(float value) => Mathf.Round(value / AxisSnapResolution) * AxisSnapResolution;

		/// <summary> Newell's method - robust for any planar polygon, matches ExtrudeNode.ComputeFaceNormal. </summary>
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
