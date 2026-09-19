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
	/// <summary> Like LineGeneratorNode, but PointCount points resampled along a Catmull-Rom curve through
	/// ControlPoints instead of a straight line - points only, no faces, meant to feed CopyToPointsNode. Every
	/// point's normal is Vector3.up regardless of the curve's own direction, same as LineGeneratorNode and for the
	/// same reason: copies stay upright by default. Runs entirely in the NodraCore native library (see Native/
	/// NodraCore/SplineGenerator.cs) - there's no managed fallback, same as DecimateNode's optional package:
	/// without it, Process() produces no geometry at all and Warning explains why. </summary>
	[Serializable]
	public class SplineGeneratorNode : GeoNode
	{
		public override string Category => "Generators";

		public List<Vector3> ControlPoints = new ()
		{
			new Vector3(0f, 0f, 0f),
			new Vector3(2f, 0f, 4f),
			new Vector3(6f, 0f, 4f),
			new Vector3(8f, 0f, 0f),
		};

		[Min(2)] public int PointCount = 32;

		/// <summary> Adds one more segment looping from the last control point back to the first, instead of
		/// stopping there. </summary>
		public bool Closed;

		public override int InputCount => 0;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node produces no geometry until it is.";

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();

			if (ControlPoints == null || ControlPoints.Count < 2 || !NodraNative.IsAvailable)
				return data;

			NodraNative.SplineGenerator(data, ControlPoints, PointCount, Closed);

			return data;
		}
	}
}
