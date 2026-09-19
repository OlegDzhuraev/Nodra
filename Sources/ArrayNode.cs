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
	/// <summary> Appends Count copies of the input geometry, copy 0 unmoved and each next one built from the
	/// cumulative transform for its index: rotated by Rotation * copy around Pivot, then moved by Offset * copy
	/// plus RadialOffset rotated along with it - the last term is what turns a plain rotating array into a ring,
	/// since a fixed local vector rotated by an increasing angle walks around a circle of that radius. Nothing is
	/// welded, so coincident points between copies stay separate (same as MergeNode). Runs entirely in the
	/// NodraCore native library (see Native/NodraCore/ArrayModifier.cs) - there's no managed fallback, same as
	/// DecimateNode's optional package: without it, Process() passes geometry through unchanged and Warning
	/// explains why. </summary>
	[Serializable]
	public class ArrayNode : GeoNode
	{
		public override string Category => "Build";

		[Min(1)] public int Count = 3;
		public Vector3 Offset = new (2f, 0f, 0f);
		public Vector3 Rotation;
		public Vector3 RadialOffset;
		public Vector3 Pivot;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of arraying it.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.ArrayModifier(input, Count, Offset, Rotation, RadialOffset, Pivot);

			return input;
		}
	}
}
