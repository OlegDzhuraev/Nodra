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
	/// <summary> Writes a per-point float into a named GeoData attribute (GeoData.SetAttribute/GetAttribute) - the
	/// same mechanism SmoothByAngleNode uses internally for SmoothGroupAttribute, exposed here generically so any
	/// node further downstream (a custom one, or a future ScatterNode density input) can read it by name. Value
	/// modes mirror VertexColorNode's Gradient sources (Height/Slope/Bounds) since they're the same useful shapes,
	/// just producing a plain float instead of evaluating a Gradient - plus Noise (Perlin, sampled from XZ position,
	/// same idea as NoiseDisplaceNode) and Random (seeded per-point, no spatial coherence at all). Remap rescales
	/// the mode's natural [0, 1] output to an arbitrary range before Blend combines it with whatever the input
	/// already carried under this name - defaults to 0 for a name nothing has written yet (GeoData's own
	/// convention), so Multiply/Subtract on a brand new attribute name always zeroes it out; Default or Add is
	/// what the FIRST SetAttributeNode writing a given name should use, Multiply/Subtract for refining afterward.
	/// Bounds mode's own ClampOutsideBounds picks what happens to a point outside the box - see that field. Bounds
	/// mode also has a second, optional input port: wire any geometry into it (typically a BoxGeneratorNode through
	/// a TransformNode so it can be moved/scaled independently) and its AABB (GeoData.GetBounds) is used instead of
	/// the Bounds field below - lets one upstream box drive several consumers' Bounds at once instead of each
	/// carrying its own separate copy. Left unconnected, the field is used exactly as before. Height/Slope/
	/// Bounds reuse VertexColorNode's own native factor wrappers directly (Native/NodraCore/VertexColor.cs) -
	/// Noise/Random get their own small native functions instead (Native/NodraCore/SetAttribute.cs), since
	/// nothing else in the package already computed those. Constant needs no native at all and always works;
	/// every other mode has no managed fallback (same pattern as DecimateNode's optional package) - Warning
	/// explains why when NodraNative.IsAvailable is false. </summary>
	[Serializable]
	public class SetAttributeNode : GeoNode
	{
		public override string Category => "Color & UV";

		public override int InputCount => 2;

		public override string GetInputPortName(int index) => index == 0 ? "In" : "Bounds";

		public string AttributeName = "Value";

		public enum ValueMode { Constant, Height, Slope, Bounds, Noise, Random }
		public enum BlendMode { Default, Multiply, Add, Subtract }

		public ValueMode Mode = ValueMode.Constant;

		[ShowIf(nameof(Mode), ValueMode.Constant)]
		public float Value = 1f;

		[ShowIf(nameof(Mode), ValueMode.Height, ValueMode.Bounds)]
		public Axis3D Axis = Axis3D.Y;

		// Only read when the Bounds input port (index 1) isn't connected - see ResolveBounds.
		[ShowIf(nameof(Mode), ValueMode.Bounds)]
		public Bounds Bounds = new (Vector3.zero, Vector3.one);

		/// <summary> A point outside Bounds along Axis has no well-defined position within it - false (default for
		/// a newly created node) skips writing this point's attribute entirely, leaving whatever it already
		/// carried (0, if nothing did yet) untouched; true pins it to whichever end of the box it overshot instead.
		/// Matters most when Bounds doesn't actually overlap the geometry at all - clamped, every point lands on
		/// the exact same edge value, which can look like "every point identically in/out of range" to whatever
		/// reads this attribute next (DeletePointsNode, say) even though none of them are really inside Bounds at
		/// all. A graph saved before this field existed deserializes it as false too (there's nothing to
		/// distinguish "old data" from "freshly created" here, both just fall back to the field's own initializer)
		/// - the one behavior change existing Bounds-mode graphs see on top of this feature. </summary>
		[ShowIf(nameof(Mode), ValueMode.Bounds)]
		public bool ClampOutsideBounds;

		[ShowIf(nameof(Mode), ValueMode.Noise)]
		public float Frequency = 0.2f;

		[ShowIf(nameof(Mode), ValueMode.Noise)]
		public Vector2 NoiseOffset;

		[ShowIf(nameof(Mode), ValueMode.Random)]
		public int RandomSeed;

		[ShowIf(nameof(Mode), ValueMode.Height, ValueMode.Slope, ValueMode.Bounds, ValueMode.Noise, ValueMode.Random)]
		public Vector2 Remap = new (0f, 1f);

		public BlendMode Blend = BlendMode.Default;

		public override string Warning => NodraNative.IsAvailable
			? null
			: "NodraCore native library isn't available - Constant mode still works, but Height/Slope/Bounds/Noise/Random write nothing until it is.";

		public override GeoData Process(GeoData[] inputs)
		{
			var input = inputs.Length > 0 ? inputs[0] : null;

			if (input == null || string.IsNullOrEmpty(AttributeName))
				return input;

			if (Mode == ValueMode.Constant)
			{
				ApplyConstant(input);
				return input;
			}

			if (!NodraNative.IsAvailable)
				return input;

			switch (Mode)
			{
				case ValueMode.Height: ApplyT(input, NodraNative.VertexColorHeightT(input, (int) Axis)); break;
				case ValueMode.Slope: ApplyT(input, NodraNative.VertexColorSlopeT(input)); break;
				case ValueMode.Bounds: ApplyBounds(input, ResolveBounds(inputs.Length > 1 ? inputs[1] : null)); break;
				case ValueMode.Noise: ApplyT(input, NodraNative.SetAttributeNoiseT(input, Frequency, NoiseOffset)); break;
				case ValueMode.Random: ApplyT(input, NodraNative.SetAttributeRandomT(input.PointCount, RandomSeed)); break;
			}

			return input;
		}

		// The Bounds input takes precedence over the Bounds field whenever something is actually wired in and non-
		// empty - an unconnected port (or one feeding a disabled/empty branch) falls back to the field exactly as
		// if the port didn't exist.
		Bounds ResolveBounds(GeoData boundsInput) => boundsInput is { PointCount: > 0 } ? boundsInput.GetBounds() : Bounds;

		void ApplyConstant(GeoData data)
		{
			for (var i = 0; i < data.PointCount; i++)
				Write(data, i, Value);
		}

		// Shared tail for every mode whose native counterpart (VertexColorHeightT/SlopeT, SetAttributeNoiseT/
		// RandomT) already reports one raw [0, 1] factor per point - Remap/Blend/SetAttribute all stay Unity-side
		// regardless (see Write/Combine below), same "native has no concept of this" reasoning as every other
		// wrapper. Bounds needs its own path since a point can be skipped entirely (ClampOutsideBounds false).
		void ApplyT(GeoData data, float[] t)
		{
			for (var i = 0; i < data.PointCount; i++)
				Write(data, i, Mathf.Lerp(Remap.x, Remap.y, t[i]));
		}

		void ApplyBounds(GeoData data, Bounds bounds)
		{
			var (t, applies) = NodraNative.VertexColorBoundsT(data, (int) Axis, bounds, ClampOutsideBounds);

			for (var i = 0; i < data.PointCount; i++)
				if (applies[i])
					Write(data, i, Mathf.Lerp(Remap.x, Remap.y, t[i]));
		}

		void Write(GeoData data, int index, float value) =>
			data.SetAttribute(AttributeName, index, Combine(data.GetAttribute(AttributeName, index), value));

		float Combine(float previous, float next) => Blend switch
		{
			BlendMode.Multiply => previous * next,
			BlendMode.Add => previous + next,
			BlendMode.Subtract => previous - next,
			_ => next,
		};
	}
}
