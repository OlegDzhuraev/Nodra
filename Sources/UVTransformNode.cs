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
	/// <summary> Rotates, tiles and offsets every point's UV - the one thing missing next to AutoUVNode, which
	/// projects fresh UVs but has no way to then tile a texture across them or nudge it into place. Rotation is
	/// around UV space's own center (0.5, 0.5) so a centered texture stays centered; Tiling then scales from the
	/// origin (matching a Material's own Tiling field: a UV of 1 becomes Tiling, not 0.5 + (0.5 - center) * Tiling),
	/// and Offset is added last - same order a shader applies uv * tiling + offset in, with rotation folded in
	/// before that. Runs entirely in the NodraCore native library (see Native/NodraCore/UVTransform.cs) - there's
	/// no managed fallback, same as DecimateNode's optional package: without it, Process() passes geometry through
	/// unchanged and Warning explains why. </summary>
	[Serializable]
	public class UVTransformNode : GeoNode
	{
		public override string Category => "Color & UV";

		public float Rotation;
		public Vector2 Tiling = Vector2.one;
		public Vector2 Offset = Vector2.zero;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of transforming UVs.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || !NodraNative.IsAvailable)
				return input;

			NodraNative.UVTransform(input, Rotation, Tiling, Offset);

			return input;
		}
	}
}
