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
using System.Collections.Generic;
using UnityEngine;

namespace Nodra
{
	/// <summary> Sweeps a regular polygon cross-section (Sides 3+ for a beam, higher for a round tube) along the
	/// input's points, connecting consecutive rings into a tube - discards whatever primitives the input had and
	/// replaces its points entirely. Meant to sit after LineGeneratorNode/SplineGeneratorNode (or any other
	/// points-only source); a rotation-minimizing frame is carried along the path so the cross-section doesn't
	/// twist through turns. Runs entirely in the NodraCore native library (see Native/NodraCore/Tube.cs) - there's
	/// no managed fallback, same as DecimateNode's optional package: without it, Process() passes geometry through
	/// unchanged and Warning explains why. </summary>
	[Serializable]
	public class TubeNode : GeoNode
	{
		public override string Category => "Build";

		[Min(3)] public int Sides = 8;
		[Min(0f)] public float Radius = 0.25f;
		public float Twist;

		/// <summary> Loops the last ring back onto the first instead of capping the ends - the path itself isn't
		/// required to repeat its first point (SplineGeneratorNode's own Closed doesn't). </summary>
		public bool Closed;

		public bool CapStart = true;
		public bool CapEnd = true;

		public override string GetInputPortName(int index) => "Path";

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of building a tube.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount < 2 || !NodraNative.IsAvailable)
				return input;

			var path = new List<Vector3>(input.Points);
			input.Points.Clear();
			input.Normals.Clear();
			input.Uvs.Clear();
			input.Colors.Clear();
			input.Primitives.Clear();

			NodraNative.Tube(input, path, Sides, Radius, Twist, Closed, CapStart, CapEnd);

			return input;
		}
	}
}
