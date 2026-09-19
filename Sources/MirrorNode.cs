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
	/// <summary> Appends a mirrored copy of the input across the given axis/Offset plane - model half a symmetric
	/// shape and get the whole thing. A point that already sits on the plane (within WeldDistance) is reused
	/// instead of duplicated, so the seam doesn't get a doubled-up row of coincident points. Runs entirely in the
	/// NodraCore native library (see Native/NodraCore/Mirror.cs) - there's no managed fallback, same as
	/// DecimateNode's optional package: without it, Process() passes geometry through unchanged and Warning
	/// explains why. </summary>
	[Serializable]
	public class MirrorNode : GeoNode
	{
		public override string Category => "Build";

		public Axis3D Axis = Axis3D.X;
		public float Offset;
		[Min(0f)] public float WeldDistance = 0.0001f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of mirroring.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.Mirror(input, (int) Axis, Offset, WeldDistance);

			return input;
		}
	}
}
