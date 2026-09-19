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

namespace Nodra
{
	/// <summary> Fans an N-gon across every open boundary loop it finds - fills a hole punched through the middle
	/// of a patch, or the open edge ChamferNode/ExtrudeNode/BooleanNode leaves by design. Assumes each loop is a
	/// simple, non-self-touching cycle; a non-manifold boundary (two holes sharing one vertex) isn't handled
	/// specially - that vertex's two boundary edges just overwrite each other in the walk. Runs entirely in the
	/// NodraCore native library (see Native/NodraCore/CapHoles.cs) - there's no managed fallback, same as
	/// DecimateNode's optional package: without it, Process() passes geometry through unchanged and Warning
	/// explains why. </summary>
	[Serializable]
	public class CapHolesNode : GeoNode
	{
		public override string Category => "Build";

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - this node passes geometry through unchanged instead of capping holes.";

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.Primitives.Count == 0 || !NodraNative.IsAvailable)
				return input;

			NodraNative.CapHoles(input);

			return input;
		}
	}
}
