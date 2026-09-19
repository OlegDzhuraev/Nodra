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
	/// <summary> Cuts every primitive against one plane (Center/Normal) via Sutherland-Hodgman, keeping only the
	/// half in front of Normal (Invert flips which half that is) - a much lighter alternative to BooleanNode/GeoCsg
	/// for the common "chop part of a mesh off with a flat cut" case. A primitive straddling the plane is clipped
	/// into a new, smaller polygon; a new point is inserted at each crossing, cached per ORIGINAL edge (not
	/// position) so two primitives sharing that edge get the exact same new point index rather than two separate,
	/// merely coincident ones - Cap relies on that to see one continuous boundary loop rather than a seam of
	/// microscopic gaps. Leftover points no primitive references anymore (fully on the discarded side) aren't
	/// removed, same as FaceFilterNode - chain a RemoveUnusedPointsNode after this one if that matters. Runs
	/// entirely in the NodraCore native library (see Native/NodraCore/Slice.cs) - there's no managed fallback,
	/// same as DecimateNode's optional package: without it, Process() passes geometry through unchanged and
	/// Warning explains why. </summary>
	[Serializable]
	public class SliceNode : GeoNode
	{
		public override string Category => "Build";

		public Vector3 Center = Vector3.zero;
		public Vector3 Normal = Vector3.up;
		public bool Invert;
		public bool Cap = true;

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of slicing.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			var normal = Normal.sqrMagnitude > 0f ? Normal.normalized : Vector3.up;
			if (Invert)
				normal = -normal;

			NodraNative.Slice(input, Center, normal);

			// CapHolesNode's own boundary walk is entirely generic - it looks at every primitive's edges for
			// whichever ones are only ever walked in one direction, with no idea (or need to know) that this cut is
			// what created them - so the exact same pass that plugs an ExtrudeNode/ChamferNode/BooleanNode opening
			// plugs this one too, with zero risk of a second, subtly different boundary-walk implementation drifting
			// out of sync with the first.
			return Cap ? new CapHolesNode().Process(input) : input;
		}
	}
}
