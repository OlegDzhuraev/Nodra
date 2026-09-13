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
	/// <summary> Generates a flat disc centered on the origin, lying in the XZ plane, normal Vector3.up - the same
	/// ring CylinderGeneratorNode caps with, just standalone. Fill off skips the triangle fan and leaves only the
	/// boundary ring (points only), e.g. to feed ExtrudeNode into an open tube. </summary>
	[Serializable]
	public class CircleGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float Radius = 1f;
		[Min(3)] public int Segments = 16;
		public bool Fill = true;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var segments = Mathf.Max(3, Segments);
			var cos = BuildAngleTable(segments, out var sin);
			// [Min] only constrains the Inspector - a negative Radius still needs clamping here too, or the fan
			// below inverts (see the winding note on it).
			var radius = Mathf.Max(0f, Radius);

			var center = Fill ? data.AddPoint(Vector3.zero, Vector3.up, new Vector2(0.5f, 0.5f)) : -1;
			var ringStart = data.PointCount;

			for (var col = 0; col <= segments; col++)
			{
				var position = new Vector3(radius * cos[col], 0f, radius * sin[col]);
				var uv = new Vector2(cos[col] * 0.5f + 0.5f, sin[col] * 0.5f + 0.5f);

				data.AddPoint(position, Vector3.up, uv);
			}

			// Winding verified by hand against a quarter-circle (segments=4): Cross(b - center, a - center) with
			// a/b in increasing-angle order points down, so (center, b, a) is the one that faces up.
			if (Fill)
				for (var col = 0; col < segments; col++)
				{
					var a = ringStart + col;
					var b = a + 1;

					data.AddPrimitive(center, b, a);
				}

			return data;
		}

		// Closes the ring back onto column 0 using its exact value rather than re-evaluating cos/sin(2*PI) - see
		// SphereGeneratorNode for why the float-precision drift matters once RecalculateNormals has to weld the seam.
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
