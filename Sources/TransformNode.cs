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
	/// <summary> Translates, rotates and scales every point of the input geometry. Runs entirely in the NodraCore
	/// native library (see Native/NodraCore/Transform.cs) - there's no managed fallback, same as DecimateNode's
	/// optional package: without it, Process() passes geometry through unchanged and Warning explains why. </summary>
	[Serializable]
	public class TransformNode : GeoNode
	{
		public override string Category => "Deform";

		public Vector3 Translation;
		public Vector3 Rotation;
		public Vector3 Scale = Vector3.one;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of transforming it.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || !NodraNative.IsAvailable)
				return input;

			var (points, normals) = NodraNative.Transform(input, Translation, Rotation, Scale);

			input.Points.Clear();
			input.Points.AddRange(points);
			input.Normals.Clear();
			input.Normals.AddRange(normals);

			return input;
		}
	}
}
