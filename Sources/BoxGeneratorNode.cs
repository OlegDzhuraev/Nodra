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
	/// <summary> Generates a box centered on the origin with a separate set of 4 points per face
	/// (hard edges, flat shading). Runs entirely in the NodraCore native library (see Native/NodraCore/
	/// BoxGenerator.cs) - there's no managed fallback, same as DecimateNode's optional package: without it,
	/// Process() produces no geometry at all and Warning explains why. </summary>
	[Serializable]
	public class BoxGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public Vector3 Size = Vector3.one;

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (NodraNative.IsAvailable)
				NodraNative.BoxGenerator(data, Size);

			return data;
		}
	}
}
