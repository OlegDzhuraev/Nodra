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
	/// tip has nothing to cap. RadiusTop 0 gives a cone; equal radii give a plain cylinder. Runs entirely in the
	/// NodraCore native library (see Native/NodraCore/CylinderGenerator.cs) - there's no managed fallback, same
	/// as DecimateNode's optional package: without it, Process() produces no geometry at all and Warning explains
	/// why. </summary>
	[Serializable]
	public class CylinderGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float RadiusBottom = 0.5f;
		[Min(0f)] public float RadiusTop = 0.5f;
		[Min(0f)] public float Height = 2f;
		[Min(3)] public int Segments = 16;

		// 1 (the default) is just the original two-ring cylinder - bottom and top joined directly. Anything higher
		// adds intermediate rings along the axis, purely extra geometry for downstream nodes (Bend, Twist,
		// NoiseDisplace, ...) to deform; the side wall is straight either way, so this doesn't change its shape.
		[Min(1)] public int HeightSegments = 1;

		public bool CapBottom = true;
		public bool CapTop = true;

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (NodraNative.IsAvailable)
				NodraNative.CylinderGenerator(data, RadiusBottom, RadiusTop, Height, Segments, HeightSegments, CapBottom, CapTop);

			return data;
		}
	}
}
