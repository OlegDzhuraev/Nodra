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
	public enum BooleanOperation
	{
		Union,
		Subtract,
		Intersect,
	}

	/// <summary> Combines two closed, manifold shapes with a real CSG boolean - unlike MergeNode, which just
	/// concatenates geometry, this actually cuts each input against the other (see GeoCsg for the BSP-tree
	/// implementation). An open surface on either input (e.g. a bare GridGeneratorNode) has no well-defined
	/// "inside", so results involving one are undefined. Heavy or pathological input (many small separate pieces,
	/// e.g. CopyToPointsNode stamping hundreds of copies) can make this slow - past a minute, GeoCsg aborts with a
	/// TimeoutException, which ProceduralMeshGenerator.Generate() catches and logs rather than propagating. </summary>
	[Serializable]
	public class BooleanNode : GeoNode
	{
		public override string Category => "Combine";

		public BooleanOperation Operation = BooleanOperation.Union;

		public override int InputCount => 2;

		public override string GetInputPortName(int index) => index == 0 ? "A" : "B";

		public override GeoData Process(GeoData[] inputs)
		{
			var a = inputs.Length > 0 ? inputs[0] : null;
			var b = inputs.Length > 1 ? inputs[1] : null;

			if (a == null && b == null)
				return new GeoData();

			// One side unconnected: Union/Subtract fall back to whichever side exists - booleaning against nothing
			// neither adds nor removes anything. Intersect against nothing has nothing in common with anything.
			if (a == null || b == null)
				return Operation == BooleanOperation.Intersect ? new GeoData() : a ?? b;

			return Operation switch
			{
				BooleanOperation.Union => GeoCsg.Union(a, b),
				BooleanOperation.Subtract => GeoCsg.Subtract(a, b),
				BooleanOperation.Intersect => GeoCsg.Intersect(a, b),
				_ => a,
			};
		}
	}
}
