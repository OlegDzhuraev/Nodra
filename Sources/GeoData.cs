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

using System.Collections.Generic;
using UnityEngine;

namespace Nodra
{
	/// <summary> Intermediate geometry passed between GeoNodes: a flat point cloud (position/normal/UV/vertex
	/// color) plus polygon primitives referencing it by index, with per-point float attributes nodes can read/write
	/// to pass extra data along the chain (density, scale, custom masks, etc). </summary>
	public class GeoData
	{
		/// <summary> Well-known per-point attribute SmoothByAngleNode tags a smoothing cluster's points with (0 =
		/// untagged) - GeoMeshBuilder re-averages Unity's per-index RecalculateNormals within each tagged group
		/// afterward, since that call can't see that two different indices share one position (a UV seam). </summary>
		public const string SmoothGroupAttribute = "SmoothGroup";

		public readonly List<Vector3> Points = new ();
		public readonly List<Vector3> Normals = new ();
		public readonly List<Vector2> Uvs = new ();
		public readonly List<Color> Colors = new (); // defaults to white - see AddPoint

		/// <summary> Each primitive is a polygon defined by point indices, walked in order (fan-triangulated on build). </summary>
		public readonly List<int[]> Primitives = new ();

		readonly Dictionary<string, List<float>> attributes = new ();

		// SetAttribute/GetAttribute are typically hammered with the SAME attribute name in a tight per-point loop
		// (SmoothByAngleNode tagging every point it touches, say) - remembering the last name/list pair skips the
		// Dictionary lookup entirely on every call after the first for that name. Per-instance, so Clone()'s own
		// fresh GeoData starts with an empty cache regardless of what its source had cached.
		string lastAttributeName;
		List<float> lastAttributeValues;

		public int PointCount => Points.Count;

		/// <summary> Axis-aligned bounding box of every point currently in this GeoData, in whatever space Points
		/// happen to be in (local, unless something upstream already baked a transform in). Used by
		/// SetAttributeNode/VertexColorNode's Bounds mode when something is wired into their optional Bounds input,
		/// so any upstream geometry - typically a BoxGeneratorNode through a TransformNode, but nothing requires
		/// that - can drive their Bounds without either node knowing what produced it. Default Bounds (zero
		/// center/size) for an empty GeoData. </summary>
		public Bounds GetBounds()
		{
			if (PointCount == 0)
				return default;

			var bounds = new Bounds(Points[0], Vector3.zero);
			for (var i = 1; i < PointCount; i++)
				bounds.Encapsulate(Points[i]);

			return bounds;
		}

		public int AddPoint(Vector3 position, Vector3 normal, Vector2 uv) => AddPoint(position, normal, uv, Color.white);

		public int AddPoint(Vector3 position, Vector3 normal, Vector2 uv, Color color)
		{
			Points.Add(position);
			Normals.Add(normal);
			Uvs.Add(uv);
			Colors.Add(color);
			return Points.Count - 1;
		}

		public void AddPrimitive(params int[] pointIndices) => Primitives.Add(pointIndices);

		public void SetAttribute(string name, int pointIndex, float value)
		{
			var values = GetOrCreateAttributeList(name);

			while (values.Count <= pointIndex)
				values.Add(0f);

			values[pointIndex] = value;
		}

		public float GetAttribute(string name, int pointIndex, float fallback = 0f)
		{
			var values = TryGetAttributeList(name);
			return values != null && pointIndex < values.Count ? values[pointIndex] : fallback;
		}

		List<float> GetOrCreateAttributeList(string name)
		{
			if (lastAttributeName == name)
				return lastAttributeValues;

			if (!attributes.TryGetValue(name, out var values))
			{
				values = new List<float>(new float[Points.Count]);
				attributes[name] = values;
			}

			lastAttributeName = name;
			lastAttributeValues = values;
			return values;
		}

		List<float> TryGetAttributeList(string name)
		{
			if (lastAttributeName == name)
				return lastAttributeValues;

			if (!attributes.TryGetValue(name, out var values))
				return null;

			lastAttributeName = name;
			lastAttributeValues = values;
			return values;
		}

		public bool HasAttribute(string name) => attributes.ContainsKey(name);

		/// <summary> Drops the named attribute entirely - e.g. a temporary one only ever needed to drive a
		/// DeletePointsNode threshold further downstream. A no-op if it was never set. </summary>
		public void RemoveAttribute(string name)
		{
			attributes.Remove(name);

			if (lastAttributeName == name)
			{
				lastAttributeName = null;
				lastAttributeValues = null;
			}
		}

		/// <summary> Every point's value for name, densely - PointCount long, fallback wherever it was never
		/// explicitly set (the sparse list backing it may be shorter, or not exist at all). For a node that wants
		/// to scan the whole attribute at once (VertexColorNode/SetAttributeNode's own Height mode, say, finding a
		/// min/max) rather than one GetAttribute call per point. </summary>
		public float[] GetAttributeValues(string name, float fallback = 0f)
		{
			var values = new float[PointCount];

			for (var i = 0; i < values.Length; i++)
				values[i] = GetAttribute(name, i, fallback);

			return values;
		}

		/// <summary> Copies every currently-tracked named attribute verbatim from sourceIndex into targetIndex - the
		/// plain "transformed copy of one existing point" case: MirrorNode's reflection, ArrayNode's Nth copy,
		/// ExtrudeNode's offset shell, ChamferNode's inset corner. Without this, a point a node derives from exactly
		/// one source silently drops whatever a SetAttributeNode painted onto that source earlier in the chain. </summary>
		public void CopyAttributes(int targetIndex, int sourceIndex)
		{
			foreach (var name in new List<string>(attributes.Keys))
				SetAttribute(name, targetIndex, GetAttribute(name, sourceIndex));
		}

		/// <summary> Lerps every currently-tracked named attribute from fromIndex/toIndex into targetIndex, using
		/// the SAME weight t a node already computed for position/normal/etc - SliceNode's plane-crossing t,
		/// SubdivideNode's edge midpoint at a fixed 0.5, and so on. </summary>
		public void BlendAttributes(int targetIndex, int fromIndex, int toIndex, float t)
		{
			foreach (var name in new List<string>(attributes.Keys))
				SetAttribute(name, targetIndex, Mathf.Lerp(GetAttribute(name, fromIndex), GetAttribute(name, toIndex), t));
		}

		/// <summary> Averages every currently-tracked named attribute across sourceIndices (equal weight each) into
		/// targetIndex - the N-point case, e.g. SubdivideNode's face centroid. </summary>
		public void BlendAttributes(int targetIndex, IReadOnlyList<int> sourceIndices)
		{
			if (sourceIndices.Count == 0)
				return;

			foreach (var name in new List<string>(attributes.Keys))
			{
				var sum = 0f;
				foreach (var index in sourceIndices)
					sum += GetAttribute(name, index);

				SetAttribute(name, targetIndex, sum / sourceIndices.Count);
			}
		}

		/// <summary> Blends every currently-tracked named attribute into targetIndex using whatever weights the
		/// caller already computed for position/normal/etc, for the case where sources don't contribute equally -
		/// ScatterNode's barycentric sample, say. Weights aren't required to sum to 1; they're used exactly as
		/// given. </summary>
		public void BlendAttributes(int targetIndex, params (int index, float weight)[] weightedSources)
		{
			if (weightedSources.Length == 0)
				return;

			foreach (var name in new List<string>(attributes.Keys))
			{
				var blended = 0f;
				foreach (var (index, weight) in weightedSources)
					blended += GetAttribute(name, index) * weight;

				SetAttribute(name, targetIndex, blended);
			}
		}

		/// <summary> Cross-object version of the weighted BlendAttributes overload - blends every attribute name
		/// currently tracked on SOURCE (not on this) into targetIndex on this, using weights already computed for
		/// position/normal/etc. For a node like ScatterNode that samples a brand-new output GeoData from an
		/// existing input one, rather than deriving a new point within the same object everything else here
		/// assumes. </summary>
		public void BlendAttributesFrom(GeoData source, int targetIndex, params (int index, float weight)[] weightedSources)
		{
			if (weightedSources.Length == 0)
				return;

			foreach (var name in new List<string>(source.attributes.Keys))
			{
				var blended = 0f;
				foreach (var (index, weight) in weightedSources)
					blended += source.GetAttribute(name, index) * weight;

				SetAttribute(name, targetIndex, blended);
			}
		}

		/// <summary> Rebuilds every named attribute for a wholesale points rebuild that doesn't go through AddPoint
		/// at all (WeldNode, which merges many old points into fewer new ones by building entirely new Points/
		/// Normals/Uvs/Colors lists itself rather than compacting in place) - remap[oldIndex] gives that old point's
		/// new index, many-to-one allowed; every old point mapping to the same new index has its attribute value
		/// averaged together, the same way WeldNode already averages position/normal/UV/color for a merged cluster.
		/// newCount is the final point count (remap's own value range), needed since this doesn't touch Points
		/// itself to infer it from. </summary>
		public void RemapAttributes(int[] remap, int newCount)
		{
			foreach (var name in new List<string>(attributes.Keys))
			{
				var sum = new float[newCount];
				var count = new int[newCount];

				for (var oldIndex = 0; oldIndex < remap.Length; oldIndex++)
				{
					var newIndex = remap[oldIndex];
					sum[newIndex] += GetAttribute(name, oldIndex);
					count[newIndex]++;
				}

				var merged = new List<float>(newCount);
				for (var i = 0; i < newCount; i++)
					merged.Add(count[i] > 0 ? sum[i] / count[i] : 0f);

				attributes[name] = merged;
			}

			// The lists above were replaced wholesale rather than mutated - the cache would otherwise keep pointing
			// at whichever one happened to be cached right before this ran.
			lastAttributeName = null;
			lastAttributeValues = null;
		}

		/// <summary> Drops every point where keep[i] is false, compacting Points/Normals/Uvs/Colors AND every named
		/// attribute together so nothing drifts out of sync with what survives - the reason this lives here rather
		/// than in whichever node needs it (RemoveUnusedPointsNode, today) is that attributes is private: an outside
		/// node has no way to move attribute values in step with the points they belong to. Returns each original
		/// index's new one, or -1 for a dropped point, so a caller holding its own point-index references
		/// (Primitives, above all - CompactPoints doesn't touch those itself, since only the caller knows what a
		/// primitive that lost every one of its points should become) can remap them the same way. </summary>
		public int[] CompactPoints(bool[] keep)
		{
			var newIndex = new int[keep.Length];
			var write = 0;

			for (var read = 0; read < keep.Length; read++)
			{
				newIndex[read] = keep[read] ? write : -1;
				if (!keep[read])
					continue;

				if (write != read)
				{
					Points[write] = Points[read];
					Normals[write] = Normals[read];
					Uvs[write] = Uvs[read];
					Colors[write] = Colors[read];
				}

				write++;
			}

			// One name at a time (not interleaved with the pass above) so SetAttribute/GetAttribute's own
			// single-name cache actually helps instead of thrashing between names on every point.
			foreach (var name in new List<string>(attributes.Keys))
			{
				var attributeWrite = 0;

				for (var read = 0; read < keep.Length; read++)
				{
					if (!keep[read])
						continue;

					if (attributeWrite != read)
						SetAttribute(name, attributeWrite, GetAttribute(name, read));

					attributeWrite++;
				}
			}

			var removed = keep.Length - write;

			if (removed > 0)
			{
				Points.RemoveRange(write, removed);
				Normals.RemoveRange(write, removed);
				Uvs.RemoveRange(write, removed);
				Colors.RemoveRange(write, removed);

				foreach (var values in attributes.Values)
					if (values.Count > write)
						values.RemoveRange(write, values.Count - write);
			}

			return newIndex;
		}

		/// <summary> Deep-enough copy for graph fan-out: a node whose output feeds more than one downstream node
		/// must hand each of them an independent GeoData, since several nodes (TransformNode, NoiseDisplaceNode,
		/// ExtrudeNode, MergeNode's base input...) mutate what they receive in place. </summary>
		public GeoData Clone()
		{
			var clone = new GeoData();

			clone.Points.AddRange(Points);
			clone.Normals.AddRange(Normals);
			clone.Uvs.AddRange(Uvs);
			clone.Colors.AddRange(Colors);

			foreach (var primitive in Primitives)
				clone.Primitives.Add((int[]) primitive.Clone());

			foreach (var pair in attributes)
				clone.attributes[pair.Key] = new List<float>(pair.Value);

			return clone;
		}
	}
}
