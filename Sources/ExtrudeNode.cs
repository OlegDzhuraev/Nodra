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
	/// <summary> Extrudes all input primitives as a single connected shell, the way a whole connected surface
	/// should extrude together rather than each face separately. Each ORIGINAL point is offset once (along the
	/// average normal of the faces around it) and reused by every primitive/wall touching it, and side walls
	/// are only added along the outer boundary - edges used by exactly one primitive. A group of primitives
	/// that don't actually share points (e.g. BoxGeneratorNode's per-face verts) naturally has every edge count
	/// as "boundary", so each disconnected face still extrudes on its own - there's nothing connected to keep
	/// together. Runs entirely in the NodraCore native library (see Native/NodraCore/Extrude.cs) - there's no
	/// managed fallback, same as DecimateNode's optional package: without it, Process() passes geometry through
	/// unchanged and Warning explains why. </summary>
	[Serializable]
	public class ExtrudeNode : GeoNode
	{
		public override string Category => "Build";

		public float Distance = 1f;
		public bool CapNewFace = true;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of extruding.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.Extrude(input, Distance, CapNewFace);

			return input;
		}
	}
}
