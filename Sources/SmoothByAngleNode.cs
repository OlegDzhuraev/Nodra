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
	/// <summary> Recomputes normals from face angles instead of trusting whatever the input already carries: at
	/// every shared point, primitives whose face-normal angle is within AngleThreshold blend into one smooth
	/// normal, and the rest keep their own flat-faceted one - duplicating the point where a single original index
	/// would otherwise have to carry two different results. Every point also gets tagged with GeoData.
	/// SmoothGroupAttribute, so GeoMeshBuilder can keep a smoothed group's normals matching on the baked mesh too.
	/// Runs entirely in the NodraCore native library (see Native/NodraCore/SmoothByAngle.cs) - there's no managed
	/// fallback, same as DecimateNode's optional package: without it, Process() passes geometry through unchanged
	/// and Warning explains why. </summary>
	[Serializable]
	public class SmoothByAngleNode : GeoNode
	{
		public override string Category => "Cleanup";

		[Range(0f, 180f)] public float AngleThreshold = 60f;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of smoothing.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			var native = NodraNative.SmoothByAngle(input, AngleThreshold);

			input.Points.Clear();
			input.Points.AddRange(native.Points);
			input.Normals.Clear();
			input.Normals.AddRange(native.Normals);
			input.Uvs.Clear();
			input.Uvs.AddRange(native.Uvs);
			input.Colors.Clear();
			input.Colors.AddRange(native.Colors);

			for (var i = 0; i < native.SmoothGroups.Length; i++)
				if (native.SmoothGroups[i] != 0f)
					input.SetAttribute(GeoData.SmoothGroupAttribute, i, native.SmoothGroups[i]);

			return input;
		}
	}
}
