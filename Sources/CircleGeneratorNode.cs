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
	/// boundary ring (points only), e.g. to feed ExtrudeNode into an open tube. Runs entirely in the NodraCore
	/// native library (see Native/NodraCore/CircleGenerator.cs) - there's no managed fallback, same as
	/// DecimateNode's optional package: without it, Process() produces no geometry at all and Warning explains
	/// why. </summary>
	[Serializable]
	public class CircleGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float Radius = 1f;
		[Min(3)] public int Segments = 16;
		public bool Fill = true;

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry until it is.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (!NodraNative.IsAvailable)
				return data;

			NodraNative.CircleGenerator(data, Radius, Segments, Fill);

			return data;
		}
	}
}
