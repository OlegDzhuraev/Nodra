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
	/// <summary> Generates a UV sphere centered on the origin: latitude rings walked from pole to pole, each split
	/// into longitude segments. Usually the first node in a chain, like the other generators. Each pole is a ring
	/// of coincident points (needed for a clean UV seam), so every quad touching a pole fan-triangulates into one
	/// real triangle plus one harmless zero-area one - the standard, simplest way to build a UV sphere. Runs
	/// entirely in the NodraCore native library (see Native/NodraCore/SphereGenerator.cs) - there's no managed
	/// fallback, same as DecimateNode's optional package: without it, Process() produces no geometry at all and
	/// Warning explains why. </summary>
	[Serializable]
	public class SphereGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float Radius = 1f;

		/// <summary> x: segments around the equator (longitude); y: rings from pole to pole (latitude). </summary>
		[MinVector2Int(3, 2)] public Vector2Int Resolution = new (16, 8);

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (NodraNative.IsAvailable)
				NodraNative.SphereGenerator(data, Radius, Resolution.x, Resolution.y);

			return data;
		}
	}
}
