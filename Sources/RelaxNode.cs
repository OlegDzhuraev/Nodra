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
	/// <summary> Laplacian smoothing: moves every point toward the average position of its edge-connected
	/// neighbors by Factor, Iterations times - softens jagged NoiseDisplaceNode/BooleanNode results without
	/// changing topology. PreserveBoundary keeps an open mesh's outer rim from shrinking inward. Runs entirely in
	/// the NodraCore native library (see Native/NodraCore/Relax.cs) - there's no managed fallback, same as
	/// DecimateNode's optional package: without it, Process() passes geometry through unchanged and Warning
	/// explains why. </summary>
	[Serializable]
	public class RelaxNode : GeoNode
	{
		public override string Category => "Deform";

		[Range(0f, 1f)] public float Factor = 0.5f;
		[Min(1)] public int Iterations = 1;
		public bool PreserveBoundary = true;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of relaxing.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.Relax(input, Factor, Iterations, PreserveBoundary);

			return input;
		}
	}
}
