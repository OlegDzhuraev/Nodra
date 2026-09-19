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
	/// <summary> Appends a second branch's geometry into the first, now that the graph editor can actually wire
	/// two real inputs into one node - a proper multi-input merge, unlike the embedded-sub-pipeline approximation
	/// this node used before the graph existed. Nothing is welded, so coincident points from both sides stay
	/// separate. Runs entirely in the NodraCore native library (see Native/NodraCore/Merge.cs) - there's no
	/// managed fallback: without it, Branch is silently dropped instead of merged in, and Warning explains why. </summary>
	[Serializable]
	public class MergeNode : GeoNode
	{
		public override string Category => "Combine";

		public override int InputCount => 2;

		public override string GetInputPortName(int index) => index == 0 ? "Base" : "Branch";

		public override string Warning =>
			NodraNative.IsAvailable ? null : "NodraCore native library isn't available - Branch is dropped instead of being merged in.";

		public override GeoData Process(GeoData[] inputs)
		{
			var baseData = inputs.Length > 0 ? inputs[0] : null;
			var branchData = inputs.Length > 1 ? inputs[1] : null;

			var output = baseData ?? new GeoData();

			if (branchData != null && NodraNative.IsAvailable)
				NodraNative.Merge(output, branchData);

			return output;
		}
	}
}
