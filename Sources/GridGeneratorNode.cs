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
	/// <summary> Generates a flat, subdivided quad grid on the XZ plane, centered on the origin. Usually the
	/// first node in a chain. Runs entirely in the NodraCore native library (see Native/NodraCore/
	/// GridGenerator.cs) - there's no managed fallback, same as DecimateNode's optional package: without it,
	/// Process() produces no geometry at all and Warning explains why. </summary>
	[Serializable]
	public class GridGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public Vector2 Size = new (10f, 10f);
		[MinVector2Int(1)] public Vector2Int Resolution = new (10, 10);

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (NodraNative.IsAvailable)
				NodraNative.GridGenerator(data, Size, Resolution.x, Resolution.y);

			return data;
		}
	}
}
