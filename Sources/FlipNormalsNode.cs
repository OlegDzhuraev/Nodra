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

namespace Nodra
{
	/// <summary> Reverses every primitive's point order and negates every normal - fixes a mesh that renders
	/// inside-out (a flipped BooleanNode result, an interior-facing shape like a skybox or cave). Reversing a
	/// polygon's traversal order always flips its Cross-product normal, for any polygon, so this needs no
	/// per-shape winding check the way a new generator's own point order would. Runs entirely in the NodraCore
	/// native library (see Native/NodraCore/FlipNormals.cs) - there's no managed fallback, same as DecimateNode's
	/// optional package: without it, Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class FlipNormalsNode : GeoNode
	{
		public override string Category => "Cleanup";

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of flipping normals.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || !NodraNative.IsAvailable)
				return input;

			NodraNative.FlipNormals(input);

			return input;
		}
	}
}
