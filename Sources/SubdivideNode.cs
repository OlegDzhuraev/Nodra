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
	/// <summary> Splits every primitive into one quad per corner, fanned around its own centroid - the topological
	/// half of Catmull-Clark (no position averaging/smoothing, so it adds detail without rounding sharp shapes).
	/// Works on any polygon size, not just triangles or quads. An edge shared by index between two primitives gets
	/// one shared midpoint; an edge only shared by position (BoxGeneratorNode's per-face corners) gets two,
	/// keeping the hard edge intact through the split. Runs entirely in the NodraCore native library (see Native/
	/// NodraCore/Subdivide.cs) - there's no managed fallback, same as DecimateNode's optional package: without it,
	/// Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class SubdivideNode : GeoNode
	{
		public override string Category => "Build";

		[Range(1, 4)] public int Iterations = 1;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of subdividing.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.Subdivide(input, Iterations);

			return input;
		}
	}
}
