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
	/// <summary> Generates a torus centered on the origin, ring axis along Y: MajorRadius is the distance from the
	/// center to the middle of the tube, MinorRadius is the tube's own radius. Unlike Sphere/Cylinder, both loops
	/// (around the ring, around the tube) wrap fully closed on themselves - there's no pole or end to cap. </summary>
	[Serializable]
	public class TorusGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		public float MajorRadius = 1f;
		public float MinorRadius = 0.25f;
		[Min(3)] public int MajorSegments = 24;
		[Min(3)] public int MinorSegments = 12;

		public override int InputCount => 0;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			var majorSegments = Mathf.Max(3, MajorSegments);
			var minorSegments = Mathf.Max(3, MinorSegments);
			var startIndex = data.PointCount;

			var cosTheta = BuildAngleTable(majorSegments, out var sinTheta);
			var cosPhi = BuildAngleTable(minorSegments, out var sinPhi);

			for (var row = 0; row <= minorSegments; row++)
			{
				// Distance from the torus's own center axis to the tube's surface at this point around its
				// cross-section - the outward normal there is simply the direction from the tube's own (thin)
				// center circle to the surface point, i.e. (cos(phi), sin(phi)) rotated into this ring position.
				var tubeRadius = MajorRadius + MinorRadius * cosPhi[row];

				for (var col = 0; col <= majorSegments; col++)
				{
					var position = new Vector3(tubeRadius * cosTheta[col], MinorRadius * sinPhi[row], tubeRadius * sinTheta[col]);
					var normal = new Vector3(cosPhi[row] * cosTheta[col], sinPhi[row], cosPhi[row] * sinTheta[col]);
					var uv = new Vector2(col / (float) majorSegments, row / (float) minorSegments);

					data.AddPoint(position, normal, uv);
				}
			}

			for (var row = 0; row < minorSegments; row++)
			{
				for (var col = 0; col < majorSegments; col++)
				{
					var i0 = startIndex + row * (majorSegments + 1) + col;
					var i1 = i0 + 1;
					var i2 = i0 + majorSegments + 1;
					var i3 = i2 + 1;

					data.AddPrimitive(i0, i2, i3, i1);
				}
			}

			return data;
		}

		// Closes each ring back onto index 0 using its exact value rather than re-evaluating cos/sin(2*PI) - see
		// SphereGeneratorNode for why the float-precision drift between the two otherwise matters (it reopens the
		// seam as a lighting seam once Mesh.RecalculateNormals can no longer tell the two positions apart). Needed
		// in both directions here, since both the major and minor loop wrap fully closed.
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
