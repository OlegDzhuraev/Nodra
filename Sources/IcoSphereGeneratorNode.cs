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
	/// <summary> Generates a sphere by subdividing an icosahedron and projecting every vertex onto Radius - unlike
	/// SphereGeneratorNode's UV sphere, triangles stay near-equal in size everywhere, with no pinching at the
	/// poles. Subdivisions 0 gives the plain 20-triangle icosahedron; each step after that quarters every
	/// triangle. Runs entirely in the NodraCore native library (see Native/NodraCore/IcoSphereGenerator.cs) -
	/// there's no managed fallback, same as DecimateNode's optional package: without it, Process() produces no
	/// geometry at all and Warning explains why. </summary>
	[Serializable]
	public class IcoSphereGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		[Min(0f)] public float Radius = 1f;
		[Range(0, 6)] public int Subdivisions = 2;

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry until it is.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (!NodraNative.IsAvailable)
				return data;

			NodraNative.IcoSphereGenerator(data, Radius, Subdivisions);

			return data;
		}
	}
}
