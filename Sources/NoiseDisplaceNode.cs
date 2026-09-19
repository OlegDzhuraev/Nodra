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
	/// <summary> Displaces every point by 2D noise sampled from its XZ position - Perlin for smooth rolling bumps,
	/// Voronoi for cellular/rocky facets. GeoMeshBuilder recalculates normals after baking, so displacement
	/// doesn't need to keep normals in sync itself. Runs entirely in the NodraCore native library (see Native/
	/// NodraCore/NoiseDisplace.cs) - there's no managed fallback, same as DecimateNode's optional package:
	/// without it, Process() passes geometry through unchanged and Warning explains why.
	///
	/// Voronoi's native result is a faithful, bit-identical port of what this node always computed itself.
	/// Perlin is NOT - UnityEngine.Mathf.PerlinNoise runs inside Unity's own closed-source native engine and can't
	/// be reproduced outside it (confirmed against Unity's own forums - not just an assumption), so native uses a
	/// different, from-scratch gradient noise instead. A graph already using NoiseType.Perlin will look visibly
	/// different once native takes over - same general character (smooth rolling bumps), different specific
	/// bumps - a deliberate, discussed tradeoff in exchange for this node working outside Unity at all. </summary>
	[Serializable]
	public class NoiseDisplaceNode : GeoNode
	{
		public override string Category => "Deform";

		public enum NoiseType { Perlin, Voronoi }

		public NoiseType Type = NoiseType.Perlin;
		public float Amplitude = 1f;
		public float Frequency = 0.2f;
		public Vector2 Offset;
		public bool AlongNormal = true;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of displacing.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || !NodraNative.IsAvailable)
				return input;

			var displaced = NodraNative.NoiseDisplace(input, (int) Type, Amplitude, Frequency, Offset.x, Offset.y, AlongNormal);

			input.Points.Clear();
			input.Points.AddRange(displaced);

			return input;
		}
	}
}
