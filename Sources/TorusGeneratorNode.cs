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
	/// (around the ring, around the tube) wrap fully closed on themselves - there's no pole or end to cap. Runs
	/// entirely in the NodraCore native library (see Native/NodraCore/TorusGenerator.cs) - there's no managed
	/// fallback, same as DecimateNode's optional package: without it, Process() produces no geometry at all and
	/// Warning explains why. </summary>
	[Serializable]
	public class TorusGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float MajorRadius = 1f;
		[Min(0f)] public float MinorRadius = 0.25f;
		[Min(3)] public int MajorSegments = 24;
		[Min(3)] public int MinorSegments = 12;

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (NodraNative.IsAvailable)
				NodraNative.TorusGenerator(data, MajorRadius, MinorRadius, MajorSegments, MinorSegments);

			return data;
		}
	}
}
