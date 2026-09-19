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
	/// <summary> Stamps a source mesh at every point of the input geometry, optionally aligning to the point's
	/// normal and jittering rotation/scale, merging all copies into one output. Typically fed by a ScatterNode. A
	/// per-instance attribute (ScaleAttribute) can drive scale too - read once per INPUT point (via GeoData.
	/// GetAttribute, fallback 1 so an unset point stamps at its otherwise-normal size instead of collapsing to
	/// nothing), multiplied into the same per-copy scale UniformScaleRange's own random roll produces, not read per
	/// SourceMesh vertex - a "Density"/custom value ScatterNode already carried onto its scattered points
	/// (BlendAttributesFrom) can size what gets stamped there without a second node. Runs entirely in the
	/// NodraCore native library (see Native/NodraCore/CopyToPoints.cs) - there's no managed fallback, same as
	/// DecimateNode's optional package: without it, Process() produces no geometry at all and Warning explains why.
	///
	/// AlignToNormal is NOT a bit-identical port when it actually rotates something: Unity's own
	/// Quaternion.FromToRotation is a native engine call ("FromToQuaternionSafe", confirmed via UnityCsReference's
	/// own Math.bindings.cs - the same kind of gap as Mathf.PerlinNoise elsewhere in this project) with no publicly
	/// reproducible algorithm. Every normal direction except one still resolves to the single mathematically
	/// correct shortest-arc rotation, which native reproduces exactly - only a point whose normal lands EXACTLY
	/// antiparallel to Up (straight down - common enough for a flat downward-facing patch to hit in practice, not
	/// just a theoretical edge case) has no unique answer; native picks a fixed axis (world Z) for that one
	/// direction instead of guessing at Unity's own unobservable native tie-break, a narrow, deliberate,
	/// documented difference (see CopyToPoints.cs's own comment) rather than a silent approximation. </summary>
	[Serializable]
	public class CopyToPointsNode : GeoNode
	{
		public override string Category => "Scatter/Copy";

		public Mesh SourceMesh;
		public bool AlignToNormal = true;
		public float RandomYRotation = 360f;
		const float MinUniformScale = 0.001f;

		[Min(MinUniformScale)] public Vector2 UniformScaleRange = new (1f, 1f);
		public int RandomSeed;

		/// <summary> Empty (default) leaves scale entirely up to UniformScaleRange's own random roll, same as
		/// before this field existed. Set to a name (e.g. one a SetAttributeNode wrote) to multiply that point's
		/// own value into the roll instead of ignoring it - a point where the name was never set reads back 1
		/// (explicitly, not GetAttribute's own 0 default), so it stamps at whatever UniformScaleRange alone would
		/// have given it rather than vanishing to a zero-scale point. </summary>
		public string ScaleAttribute = "";

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry until it is.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || SourceMesh == null || !NodraNative.IsAvailable)
				return new GeoData();

			return NodraNative.CopyToPoints(input, SourceMesh, AlignToNormal, RandomYRotation,
				UniformScaleRange.x, UniformScaleRange.y, ScaleAttribute, RandomSeed);
		}
	}
}
