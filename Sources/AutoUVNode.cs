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
	public enum UVProjection
	{
		Triplanar,
		Spherical,
		Cylindrical,
	}

	/// <summary> Regenerates UVs by Projection, scaled by Scale (world units per UV tile). Not a real unwrap -
	/// Triplanar's opposite faces along the same axis project identically and can come out mirrored; Spherical/
	/// Cylindrical project the ±X-facing halves separately (seams at ±90° longitude instead of one at ±180°) to
	/// keep any single primitive's angular span small. Runs entirely in the NodraCore native library (see Native/
	/// NodraCore/AutoUV.cs) - there's no managed fallback, same as DecimateNode's optional package: without it,
	/// Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class AutoUVNode : GeoNode
	{
		public override string Category => "Color & UV";

		public UVProjection Projection = UVProjection.Triplanar;
		[Min(0.0001f)] public float Scale = 1f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of generating UVs.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.AutoUV(input, (int) Projection, Scale);

			return input;
		}
	}
}
