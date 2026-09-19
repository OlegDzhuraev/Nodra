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
	/// <summary> Keeps only primitives whose face normal is within MaxAngle of Direction, dropping the rest (Invert
	/// flips it - discard those instead, keep everything else) - cut the bottom off a sphere, remove upward-facing
	/// caps, carve a half-pipe without a full BooleanNode. Leftover points no primitive references anymore aren't
	/// removed; chain a RemoveUnusedPointsNode after this one if that matters. Runs entirely in the NodraCore
	/// native library (see Native/NodraCore/FaceFilter.cs) - there's no managed fallback, same as DecimateNode's
	/// optional package: without it, Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class FaceFilterNode : GeoNode
	{
		public override string Category => "Cleanup";

		public Vector3 Direction = Vector3.up;
		[Range(0f, 180f)] public float MaxAngle = 90f;
		public bool Invert;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of filtering faces.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.FaceFilter(input, Direction, MaxAngle, Invert);

			return input;
		}
	}
}
