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
	/// <summary> Drops points whose named attribute falls INSIDE AttributeRange (Invert flips it - drop those
	/// outside instead), or a Random fraction of them - then drops every primitive that referenced even one of them (a
	/// triangle can't keep existing missing one of its own corners), and compacts what's left via GeoData.
	/// CompactPoints. Deliberately doesn't reimplement Height/Slope/Bounds/Noise itself the way VertexColorNode and
	/// SetAttributeNode both already do - chain a SetAttributeNode first to compute whichever of those (or anything
	/// else) into a named attribute, then threshold it here. Works on a points-only cloud too (ScatterNode's
	/// output, no primitives to drop) - e.g. thinning it by a density attribute or straight Random before
	/// CopyToPointsNode stamps every point that's left.
	///
	/// Only Random mode depends on the NodraCore native library (Native/NodraCore/DeletePoints.cs) - Attribute
	/// mode is a plain per-point comparison against GeoData's own (Unity-side-only) sparse attribute storage, with
	/// nothing for a native port to add, so it keeps working even where NodraCore isn't available. </summary>
	[Serializable]
	public class DeletePointsNode : GeoNode
	{
		public override string Category => "Cleanup";

		public enum ConditionMode { Attribute, Random }

		public ConditionMode Mode = ConditionMode.Attribute;

		[ShowIf(nameof(Mode), ConditionMode.Attribute)]
		public string AttributeName = "Value";

		[ShowIf(nameof(Mode), ConditionMode.Attribute)]
		public Vector2 AttributeRange = new (0f, 1f);

		[ShowIf(nameof(Mode), ConditionMode.Attribute)]
		public bool Invert;

		[ShowIf(nameof(Mode), ConditionMode.Random)]
		[Range(0f, 1f)] public float Chance = 0.5f;

		[ShowIf(nameof(Mode), ConditionMode.Random)]
		public int RandomSeed;

		public override string Warning =>
			Mode == ConditionMode.Random && !NodraNative.IsAvailable
				? "NodraCore native library isn't available - Random mode needs it, so this node passes geometry through unchanged."
				: null;

		public override GeoData Process(GeoData input)
		{
			if (input == null || input.PointCount == 0)
				return input;

			if (Mode == ConditionMode.Random && !NodraNative.IsAvailable)
				return input;

			var keep = Mode == ConditionMode.Random ? NodraNative.DeletePointsRandom(input.PointCount, Chance, RandomSeed) : KeepByAttribute(input);

			var survivingPrimitives = new List<int[]>();

			foreach (var primitive in input.Primitives)
			{
				var survives = true;

				foreach (var index in primitive)
					if (!keep[index])
					{
						survives = false;
						break;
					}

				if (survives)
					survivingPrimitives.Add(primitive);
			}

			input.Primitives.Clear();
			input.Primitives.AddRange(survivingPrimitives);

			var newIndex = input.CompactPoints(keep);

			foreach (var primitive in input.Primitives)
				for (var i = 0; i < primitive.Length; i++)
					primitive[i] = newIndex[primitive[i]];

			return input;
		}

		bool[] KeepByAttribute(GeoData data)
		{
			var keep = new bool[data.PointCount];

			for (var i = 0; i < data.PointCount; i++)
			{
				var value = data.GetAttribute(AttributeName, i);
				var withinRange = value >= AttributeRange.x && value <= AttributeRange.y;
				// The node deletes, so AttributeRange names what gets deleted by default - a point is KEPT when
				// it's NOT in that range. Invert swaps which side survives (keep inside, delete outside instead).
				keep[i] = withinRange == Invert;
			}

			return keep;
		}
	}
}
