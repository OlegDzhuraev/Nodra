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
	/// <summary> Runs its own embedded sub-pipeline from scratch (starting from an empty input, same as the top
	/// level ProceduralMeshGenerator) and appends the result into the main chain's geometry - approximated here
	/// as one extra branch since this package doesn't have a visual multi-input graph yet. Point indices in the
	/// branch's primitives are shifted so they still point at the right (now-merged) points; nothing is welded,
	/// so coincident points from both sides stay separate. </summary>
	[Serializable]
	public class MergeNode : GeoNode
	{
		public GeoNodeList Branches = new ();

		public override GeoData Process(GeoData input)
		{
			var output = input ?? new GeoData();
			var branchData = Branches.Process(null);

			if (branchData != null)
				Append(output, branchData);

			return output;
		}

		static void Append(GeoData target, GeoData source)
		{
			var indexOffset = target.PointCount;

			for (var i = 0; i < source.PointCount; i++)
				target.AddPoint(source.Points[i], source.Normals[i], source.Uvs[i]);

			foreach (var primitive in source.Primitives)
			{
				var shifted = new int[primitive.Length];
				for (var i = 0; i < primitive.Length; i++)
					shifted[i] = primitive[i] + indexOffset;

				target.AddPrimitive(shifted);
			}
		}
	}
}
