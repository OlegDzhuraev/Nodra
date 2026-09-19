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
	/// <summary> Colors every point - Flat mode fills one Color everywhere; Gradient mode evaluates a Gradient by
	/// Height (the point's own position along Axis, normalized across the input's own extent), Slope (Vector3.up
	/// . normal, remapped from [-1, 1] to [0, 1] - flat faces at the gradient's 1 end, vertical ones at 0), or
	/// Bounds (same as Height, but normalized across a user-set Bounds' extent along Axis instead of the input's
	/// own - stays fixed no matter how the mesh itself changes, e.g. across an Array's copies or after a Decimate
	/// upstream, where Height would re-fit to whatever points happen to be there; ClampOutsideBounds picks what
	/// happens to a point the box doesn't actually reach - see that field). The result is combined with
	/// each point's incoming vertex color per Blend: Default replaces it outright, Multiply/Add/Subtract combine
	/// per-channel (alpha included) with whatever color arrived on the input, and are left unclamped so channels
	/// can exceed [0, 1] just like Unity's own Color arithmetic. Needs a shader that reads vertex color to show
	/// up; the debug `Nodra/Checker` shader in Extras/ multiplies it in. Bounds mode also has a second, optional
	/// input port: wire any geometry into it (typically a BoxGeneratorNode through a TransformNode so it can be
	/// moved/scaled independently) and its AABB (GeoData.GetBounds) is used instead of the Bounds field below -
	/// lets one upstream box drive several consumers' Bounds at once instead of each carrying its own separate
	/// copy. Left unconnected, the field is used exactly as before.
	///
	/// Gradient mode's per-point [0, 1] blend factor is computed in the NodraCore native library (see Native/
	/// NodraCore/VertexColor.cs) - Gradient.Evaluate itself stays on the Unity side, since a Gradient's own color/
	/// alpha keys and color-space handling are a Unity-only concern the native side has no reason to understand.
	/// Flat mode never needed native at all, so it keeps working (and Warning stays silent) even where NodraCore
	/// isn't available - same partial-dependency shape as DeletePointsNode's own Attribute/Random split. </summary>
	[Serializable]
	public class VertexColorNode : GeoNode
	{
		public override string Category => "Color & UV";

		public override int InputCount => 2;

		public override string GetInputPortName(int index) => index == 0 ? "In" : "Bounds";

		public enum ColorMode { Flat, Gradient }
		public enum GradientSource { Height, Slope, Bounds }
		public enum BlendMode { Default, Multiply, Add, Subtract }

		public ColorMode Mode = ColorMode.Flat;

		[ShowIf(nameof(Mode), ColorMode.Flat)]
		public Color Color = Color.white;

		[ShowIf(nameof(Mode), ColorMode.Gradient)]
		public GradientSource Source = GradientSource.Height;

		[ShowIf(nameof(Mode), ColorMode.Gradient)]
		[ShowIf(nameof(Source), GradientSource.Height, GradientSource.Bounds)]
		public Axis3D Axis = Axis3D.Y;

		[ShowIf(nameof(Mode), ColorMode.Gradient)]
		public Gradient Gradient = DefaultGradient();

		// Only read when the Bounds input port (index 1) isn't connected - see ResolveBounds.
		[ShowIf(nameof(Mode), ColorMode.Gradient)]
		[ShowIf(nameof(Source), GradientSource.Bounds)]
		public Bounds Bounds = new (Vector3.zero, Vector3.one);

		/// <summary> A point outside Bounds along Axis has no well-defined position within it - false (default for
		/// a newly created node) skips it entirely, leaving whatever color it already had (white, if nothing set
		/// one yet) untouched; true pins it to whichever end of the box it overshot instead. Matters most when
		/// Bounds doesn't actually overlap the mesh at all - clamped, every point lands on the exact same end of
		/// the Gradient, which can look like a flat, deliberate fill rather than "none of this actually landed
		/// inside Bounds". A graph saved before this field existed deserializes it as false too (there's nothing
		/// to distinguish "old data" from "freshly created" here, both just fall back to the field's own
		/// initializer) - the one behavior change existing Bounds-mode graphs see on top of this feature. </summary>
		[ShowIf(nameof(Mode), ColorMode.Gradient)]
		[ShowIf(nameof(Source), GradientSource.Bounds)]
		public bool ClampOutsideBounds;

		public BlendMode Blend = BlendMode.Default;

		public override string Warning =>
			Mode == ColorMode.Gradient && !NodraNative.IsAvailable
				? "NodraCore native library isn't available - Gradient mode needs it, so this node leaves colors untouched."
				: null;

		public override GeoData Process(GeoData[] inputs)
		{
			var input = inputs.Length > 0 ? inputs[0] : null;

			if (input == null)
				return null;

			if (Mode == ColorMode.Gradient && !NodraNative.IsAvailable)
				return input;

			switch (Mode)
			{
				case ColorMode.Gradient when Source == GradientSource.Slope:
					ApplySlope(input);
					break;
				case ColorMode.Gradient when Source == GradientSource.Bounds:
					ApplyBounds(input, ResolveBounds(inputs.Length > 1 ? inputs[1] : null));
					break;
				case ColorMode.Gradient:
					ApplyHeight(input);
					break;
				default:
					ApplyFlat(input);
					break;
			}

			return input;
		}

		// The Bounds input takes precedence over the Bounds field whenever something is actually wired in and non-
		// empty - an unconnected port (or one feeding a disabled/empty branch) falls back to the field exactly as
		// if the port didn't exist.
		Bounds ResolveBounds(GeoData boundsInput) => boundsInput is { PointCount: > 0 } ? boundsInput.GetBounds() : Bounds;

		void ApplyFlat(GeoData data)
		{
			for (var i = 0; i < data.PointCount; i++)
				data.Colors[i] = Combine(data.Colors[i], Color);
		}

		void ApplySlope(GeoData data)
		{
			var t = NodraNative.VertexColorSlopeT(data);

			for (var i = 0; i < data.PointCount; i++)
				data.Colors[i] = Combine(data.Colors[i], Gradient.Evaluate(t[i]));
		}

		void ApplyHeight(GeoData data)
		{
			var t = NodraNative.VertexColorHeightT(data, (int) Axis);

			for (var i = 0; i < data.PointCount; i++)
				data.Colors[i] = Combine(data.Colors[i], Gradient.Evaluate(t[i]));
		}

		// Same remap as ApplyHeight, but against the fixed Bounds field instead of the input's own min/max - so,
		// unlike Height, a point can legitimately land outside [0, 1] (the mesh has moved past the box the user
		// set). ClampOutsideBounds decides what that means: skipped so this point's color is whatever it already
		// was (default), or pinned to the nearest end instead (the only option before that field existed).
		void ApplyBounds(GeoData data, Bounds bounds)
		{
			var (t, applies) = NodraNative.VertexColorBoundsT(data, (int) Axis, bounds, ClampOutsideBounds);

			for (var i = 0; i < data.PointCount; i++)
				if (applies[i])
					data.Colors[i] = Combine(data.Colors[i], Gradient.Evaluate(t[i]));
		}

		Color Combine(Color previous, Color next) => Blend switch
		{
			BlendMode.Multiply => previous * next,
			BlendMode.Add => previous + next,
			BlendMode.Subtract => previous - next,
			_ => next,
		};

		// A default Gradient field would otherwise start out fully black until someone opens the Inspector and
		// edits it - this gives Gradient mode a sane black-to-white look out of the box.
		static Gradient DefaultGradient()
		{
			var gradient = new Gradient();
			gradient.SetKeys(
				new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
				new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

			return gradient;
		}
	}
}
