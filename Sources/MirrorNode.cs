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
	/// <summary> Appends a mirrored copy of the input across the given axis/Offset plane - model half a symmetric
	/// shape and get the whole thing. A point that already sits on the plane (within WeldDistance) is reused
	/// instead of duplicated, so the seam doesn't get a doubled-up row of coincident points. </summary>
	[Serializable]
	public class MirrorNode : GeoNode
	{
		public override string Category => "Build";

		public Axis3D Axis = Axis3D.X;
		public float Offset;
		[Min(0f)] public float WeldDistance = 0.0001f;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0)
				return input;

			var sourcePointCount = input.PointCount;
			var sourcePrimitives = new List<int[]>(input.Primitives);
			var remap = new int[sourcePointCount];

			for (var i = 0; i < sourcePointCount; i++)
			{
				var position = input.Points[i];
				var reflected = Reflect(position, Axis, Offset);

				remap[i] = Vector3.Distance(position, reflected) <= WeldDistance
					? i
					: input.AddPoint(reflected, ReflectDirection(input.Normals[i], Axis), input.Uvs[i], input.Colors[i]);
			}

			// A reflection is orientation-reversing - verified by hand against a concrete triangle - so the
			// mirrored primitive needs its point order reversed too, or it would face inward instead of matching
			// the original's outward winding.
			foreach (var primitive in sourcePrimitives)
			{
				var mirrored = new int[primitive.Length];
				for (var i = 0; i < primitive.Length; i++)
					mirrored[primitive.Length - 1 - i] = remap[primitive[i]];

				input.AddPrimitive(mirrored);
			}

			return input;
		}

		static Vector3 Reflect(Vector3 position, Axis3D axis, float offset) => axis switch
		{
			Axis3D.X => new Vector3(2f * offset - position.x, position.y, position.z),
			Axis3D.Y => new Vector3(position.x, 2f * offset - position.y, position.z),
			_ => new Vector3(position.x, position.y, 2f * offset - position.z),
		};

		// Directions reflect the same way but without the plane's own offset - only their sign along the axis flips.
		static Vector3 ReflectDirection(Vector3 direction, Axis3D axis) => axis switch
		{
			Axis3D.X => new Vector3(-direction.x, direction.y, direction.z),
			Axis3D.Y => new Vector3(direction.x, -direction.y, direction.z),
			_ => new Vector3(direction.x, direction.y, -direction.z),
		};
	}
}
