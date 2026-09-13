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
	/// <summary> Generates a cylinder/cone/frustum centered on the origin, axis along Y: a bottom ring (radius
	/// RadiusBottom, y = -Height/2) and a top ring (radius RadiusTop, y = +Height/2) joined by a slanted side wall,
	/// with independently-normaled flat caps at each end - skipped where that end's radius is 0, since a cone's
	/// tip has nothing to cap. RadiusTop 0 gives a cone; equal radii give a plain cylinder. </summary>
	[Serializable]
	public class CylinderGeneratorNode : GeoNode
	{
		public float RadiusBottom = 0.5f;
		public float RadiusTop = 0.5f;
		public float Height = 2f;
		[Min(3)] public int Segments = 16;
		public bool CapBottom = true;
		public bool CapTop = true;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var segments = Mathf.Max(3, Segments);
			var halfHeight = Height * 0.5f;
			var cos = BuildAngleTable(segments, out var sin);

			// The side wall is a straight line from the bottom ring to the top one, so its normal only depends on
			// angle, not height - just how much of it points radially outward vs. straight up/down, which is the
			// slope between the two radii over the full height (a 2D direction in the "unrolled" radius/height
			// plane; Vector2.right - flat vertical wall, no radial lean - covers the Height == RadiusBottom ==
			// RadiusTop degenerate case, where the two radii being equal collapses the slope to zero).
			var slopeVector = new Vector2(Height, RadiusBottom - RadiusTop);
			var slope = slopeVector.sqrMagnitude > 0f ? slopeVector.normalized : Vector2.right;

			var bottomStart = data.PointCount;
			AddRing(data, cos, sin, RadiusBottom, -halfHeight, slope);

			var topStart = data.PointCount;
			AddRing(data, cos, sin, RadiusTop, halfHeight, slope);

			for (var col = 0; col < segments; col++)
			{
				var i0 = bottomStart + col;
				var i1 = i0 + 1;
				var i2 = topStart + col;
				var i3 = i2 + 1;

				data.AddPrimitive(i0, i2, i3, i1);
			}

			if (CapBottom && RadiusBottom > 0f)
				AddCap(data, cos, sin, RadiusBottom, -halfHeight, Vector3.down, flip: false);

			if (CapTop && RadiusTop > 0f)
				AddCap(data, cos, sin, RadiusTop, halfHeight, Vector3.up, flip: true);

			return data;
		}

		static void AddRing(GeoData data, float[] cos, float[] sin, float radius, float y, Vector2 slope)
		{
			var segments = cos.Length - 1;

			for (var col = 0; col <= segments; col++)
			{
				var position = new Vector3(radius * cos[col], y, radius * sin[col]);
				var normal = new Vector3(slope.x * cos[col], slope.y, slope.x * sin[col]);
				var uv = new Vector2(col / (float) segments, y < 0f ? 0f : 1f);

				data.AddPoint(position, normal, uv);
			}
		}

		// A fan from a center point, not one big N-gon across the ring - matches how a capped cylinder/cone is
		// conventionally built, and avoids a handful of very thin sliver triangles at high segment counts that an
		// N-gon fanned from a single ring point would otherwise produce.
		static void AddCap(GeoData data, float[] cos, float[] sin, float radius, float y, Vector3 normal, bool flip)
		{
			var segments = cos.Length - 1;
			var center = data.AddPoint(new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f));
			var ringStart = data.PointCount;

			for (var col = 0; col <= segments; col++)
			{
				var position = new Vector3(radius * cos[col], y, radius * sin[col]);
				var uv = new Vector2(cos[col] * 0.5f + 0.5f, sin[col] * 0.5f + 0.5f);

				data.AddPoint(position, normal, uv);
			}

			for (var col = 0; col < segments; col++)
			{
				var a = ringStart + col;
				var b = a + 1;

				data.AddPrimitive(flip ? new[] { center, b, a } : new[] { center, a, b });
			}
		}

		// Closes the ring back onto column 0 using its exact value rather than re-evaluating cos/sin(2*PI) - see
		// SphereGeneratorNode for why the float-precision drift between the two otherwise matters (it reopens the
		// seam as a lighting seam once Mesh.RecalculateNormals can no longer tell the two positions apart).
		static float[] BuildAngleTable(int segments, out float[] sin)
		{
			var cos = new float[segments + 1];
			sin = new float[segments + 1];

			for (var i = 0; i < segments; i++)
			{
				var angle = i / (float) segments * Mathf.PI * 2f;
				cos[i] = Mathf.Cos(angle);
				sin[i] = Mathf.Sin(angle);
			}

			cos[segments] = cos[0];
			sin[segments] = sin[0];

			return cos;
		}
	}
}
