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
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Nodra
{
	/// <summary> Boolean mesh operations (union/subtract/intersect) via a BSP-tree CSG: each operand's triangles are
	/// classified against the other's splitting planes and cut in two wherever they straddle one, until neither
	/// operand has a polygon left crossing the other - then the surviving/split pieces are recombined per
	/// operation. Expects closed, manifold input; an open surface (e.g. a bare GridGeneratorNode) has no
	/// well-defined "inside", so results involving one are undefined. Used by BooleanNode. </summary>
	public static class GeoCsg
	{
		/// <summary> Heavy/pathological input (many small disjoint pieces, near-coplanar geometry, ...) can make
		/// the tree operations below run for a very long time rather than actually hang - CheckTimeout() aborts
		/// with a normal, catchable exception past this point instead of leaving the Editor looking frozen. </summary>
		static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(1);

		// CsgTree.Build/ClipTo below fan out onto the thread pool for their first few levels, where a BSP tree
		// from real geometry usually has the most independent work to split across cores - capped depth (at most
		// 2^6 concurrent branches) rather than the tree's actual depth, so a pathologically unbalanced tree still
		// can't spawn runaway Tasks; whatever's left past that depth falls back to the existing sequential walk.
		const int MaxParallelDepth = 6;

		// Below this many polygons, Task-scheduling overhead costs more than the split itself would - not worth
		// spawning a Task for, just walk it on the current thread instead.
		const int ParallelPolygonThreshold = 200;

		// Ambient rather than threaded through every CsgTree method call - Combine() below is the sole place
		// that starts/clears it, before any Task is spawned and after every one of them has been joined, so
		// there's never a concurrent write while a worker thread might be reading it - only concurrent reads of
		// the same Stopwatch, which is safe (Elapsed doesn't mutate shared state).
		static Stopwatch activeStopwatch;

		public static GeoData Union(GeoData a, GeoData b) => Combine(a, b, (treeA, treeB) =>
		{
			treeA.ClipTo(treeB);
			treeB.ClipTo(treeA);
			treeB.Invert();
			treeB.ClipTo(treeA);
			treeB.Invert();
			treeA.Build(treeB.AllPolygons());
		});

		public static GeoData Subtract(GeoData a, GeoData b) => Combine(a, b, (treeA, treeB) =>
		{
			treeA.Invert();
			treeA.ClipTo(treeB);
			treeB.ClipTo(treeA);
			treeB.Invert();
			treeB.ClipTo(treeA);
			treeB.Invert();
			treeA.Build(treeB.AllPolygons());
			treeA.Invert();
		});

		public static GeoData Intersect(GeoData a, GeoData b) => Combine(a, b, (treeA, treeB) =>
		{
			treeA.Invert();
			treeB.ClipTo(treeA);
			treeB.Invert();
			treeA.ClipTo(treeB);
			treeB.ClipTo(treeA);
			treeA.Build(treeB.AllPolygons());
			treeA.Invert();
		});

		static GeoData Combine(GeoData a, GeoData b, Action<CsgTree, CsgTree> operate)
		{
			var outerStopwatch = activeStopwatch;
			activeStopwatch = Stopwatch.StartNew();

			try
			{
				// The two operands' polygon lists don't depend on each other at all, so building them is free to
				// run in parallel - cheap to set up and, for two large meshes, a real (if usually small next to
				// the tree operations themselves) head start.
				var polygonsBTask = Task.Run(() => ToPolygons(b));
				var polygonsA = ToPolygons(a);
				var polygonsB = WaitFlattened(polygonsBTask);

				var treeA = new CsgTree(polygonsA);
				var treeB = new CsgTree(polygonsB);

				operate(treeA, treeB);

				return ToGeoData(treeA.AllPolygons());
			}
			finally
			{
				// Restored rather than just cleared to null, in case a caller ever nests a Combine() inside
				// another node's evaluation that's itself inside a Combine() - unlikely today, but free to keep safe.
				activeStopwatch = outerStopwatch;
			}
		}

		// Task.Wait()/.Result wrap any exception (including CheckTimeout()'s plain TimeoutException, thrown from
		// inside a parallel branch) in an AggregateException - unwrapped back to the original here, with its
		// original stack trace preserved, so ProceduralMeshGenerator's `catch (TimeoutException)` still catches it
		// exactly as it did before any of this ran in parallel.
		static void WaitFlattened(Task task)
		{
			try
			{
				task.Wait();
			}
			catch (AggregateException ex)
			{
				ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
			}
		}

		static T WaitFlattened<T>(Task<T> task)
		{
			try
			{
				return task.Result;
			}
			catch (AggregateException ex)
			{
				ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
				return default; // unreachable - Throw() above never returns, but the compiler can't know that
			}
		}

		/// <summary> Called once per iteration of every CsgTree tree-walk below - throws a plain, catchable
		/// TimeoutException once a single Union/Subtract/Intersect call has been running for too long, instead of
		/// letting genuinely pathological input (or a bug) run indefinitely. </summary>
		static void CheckTimeout()
		{
			if (activeStopwatch != null && activeStopwatch.Elapsed > TimeLimit)
			{
				throw new TimeoutException($"Nodra: Boolean operation took longer than {TimeLimit.TotalSeconds:0}s and was " +
					"aborted - the input is likely too heavy or has too many separate pieces for this BSP-tree CSG.");
			}
		}

		static List<CsgPolygon> ToPolygons(GeoData data)
		{
			var polygons = new List<CsgPolygon>();

			foreach (var primitive in data.Primitives)
			{
				// Fan-triangulated the same way GeoMeshBuilder bakes primitives - guarantees every polygon fed
				// into the BSP tree is planar, which CsgPlane.FromPoints assumes.
				for (var i = 1; i < primitive.Length - 1; i++)
				{
					var triangle = new List<CsgVertex>
					{
						ToVertex(data, primitive[0]),
						ToVertex(data, primitive[i]),
						ToVertex(data, primitive[i + 1]),
					};

					if (IsDegenerate(triangle))
						continue;

					polygons.Add(new CsgPolygon(triangle));
				}
			}

			return polygons;
		}

		static CsgVertex ToVertex(GeoData data, int index) => new (data.Points[index], data.Normals[index], data.Uvs[index]);

		// A near-zero-area triangle has no meaningful plane (the cross product below is ~0, so normalizing it is
		// unstable) and would poison every split it's involved in - collapsed geometry from upstream nodes is the
		// usual source, so these are silently dropped rather than fed into the tree.
		static bool IsDegenerate(List<CsgVertex> triangle) =>
			Vector3.Cross(triangle[1].Position - triangle[0].Position, triangle[2].Position - triangle[0].Position).sqrMagnitude <= 1e-12f;

		static GeoData ToGeoData(List<CsgPolygon> polygons)
		{
			var data = new GeoData();

			foreach (var polygon in polygons)
			{
				// Splitting a convex polygon against a plane always yields convex pieces, so every surviving
				// polygon here is still convex and can be added as one fan-triangulated primitive as-is.
				var indices = new int[polygon.Vertices.Count];

				for (var i = 0; i < polygon.Vertices.Count; i++)
				{
					var vertex = polygon.Vertices[i];
					indices[i] = data.AddPoint(vertex.Position, vertex.Normal.normalized, vertex.Uv);
				}

				data.AddPrimitive(indices);
			}

			return data;
		}

		readonly struct CsgVertex
		{
			public readonly Vector3 Position;
			public readonly Vector3 Normal;
			public readonly Vector2 Uv;

			public CsgVertex(Vector3 position, Vector3 normal, Vector2 uv)
			{
				Position = position;
				Normal = normal;
				Uv = uv;
			}

			public CsgVertex Flipped() => new (Position, -Normal, Uv);

			public static CsgVertex Lerp(CsgVertex a, CsgVertex b, float t) => new (
				Vector3.Lerp(a.Position, b.Position, t),
				Vector3.Lerp(a.Normal, b.Normal, t),
				Vector2.Lerp(a.Uv, b.Uv, t));
		}

		readonly struct CsgPlane
		{
			const float Epsilon = 1e-5f;
			const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

			public readonly Vector3 Normal;
			public readonly float Distance;

			CsgPlane(Vector3 normal, float distance)
			{
				Normal = normal;
				Distance = distance;
			}

			public static CsgPlane FromPoints(Vector3 a, Vector3 b, Vector3 c)
			{
				var normal = Vector3.Cross(b - a, c - a).normalized;
				return new CsgPlane(normal, Vector3.Dot(normal, a));
			}

			public CsgPlane Flipped() => new (-Normal, -Distance);

			/// <summary> Classifies `polygon` against this plane and files it into whichever of the four lists
			/// matches - splitting it in two along the plane first if it straddles both sides. </summary>
			public void Split(CsgPolygon polygon, List<CsgPolygon> coplanarFront, List<CsgPolygon> coplanarBack,
				List<CsgPolygon> front, List<CsgPolygon> back)
			{
				var vertexCount = polygon.Vertices.Count;
				var types = new int[vertexCount];
				var polygonType = 0;

				for (var i = 0; i < vertexCount; i++)
				{
					var side = Vector3.Dot(Normal, polygon.Vertices[i].Position) - Distance;
					types[i] = side < -Epsilon ? Back : side > Epsilon ? Front : Coplanar;
					polygonType |= types[i];
				}

				switch (polygonType)
				{
					case Coplanar:
						(Vector3.Dot(Normal, polygon.Plane.Normal) > 0f ? coplanarFront : coplanarBack).Add(polygon);
						break;

					case Front:
						front.Add(polygon);
						break;

					case Back:
						back.Add(polygon);
						break;

					default: // Spanning - straddles both sides, split it along this plane.
						SplitSpanning(polygon, types, front, back);
						break;
				}
			}

			void SplitSpanning(CsgPolygon polygon, int[] types, List<CsgPolygon> front, List<CsgPolygon> back)
			{
				var frontVertices = new List<CsgVertex>();
				var backVertices = new List<CsgVertex>();
				var vertexCount = polygon.Vertices.Count;

				for (var i = 0; i < vertexCount; i++)
				{
					var j = (i + 1) % vertexCount;
					var typeI = types[i];
					var typeJ = types[j];
					var vertexI = polygon.Vertices[i];
					var vertexJ = polygon.Vertices[j];

					if (typeI != Back)
						frontVertices.Add(vertexI);

					if (typeI != Front)
						backVertices.Add(vertexI);

					if ((typeI | typeJ) == Spanning)
					{
						var t = (Distance - Vector3.Dot(Normal, vertexI.Position)) / Vector3.Dot(Normal, vertexJ.Position - vertexI.Position);
						var splitVertex = CsgVertex.Lerp(vertexI, vertexJ, t);
						frontVertices.Add(splitVertex);
						backVertices.Add(splitVertex);
					}
				}

				if (frontVertices.Count >= 3)
					front.Add(new CsgPolygon(frontVertices));

				if (backVertices.Count >= 3)
					back.Add(new CsgPolygon(backVertices));
			}
		}

		class CsgPolygon
		{
			public readonly List<CsgVertex> Vertices;
			public readonly CsgPlane Plane;

			public CsgPolygon(List<CsgVertex> vertices)
			{
				Vertices = vertices;
				Plane = CsgPlane.FromPoints(vertices[0].Position, vertices[1].Position, vertices[2].Position);
			}

			public CsgPolygon Flipped()
			{
				var flipped = new List<CsgVertex>(Vertices.Count);

				for (var i = Vertices.Count - 1; i >= 0; i--)
					flipped.Add(Vertices[i].Flipped());

				return new CsgPolygon(flipped);
			}
		}

		/// <summary> One operand's BSP tree: a splitting plane (borrowed from the first polygon built into this
		/// node), the polygons lying exactly on it, and front/back subtrees holding everything else. </summary>
		class CsgTree
		{
			CsgPlane? plane;
			CsgTree frontTree;
			CsgTree backTree;
			readonly List<CsgPolygon> polygons = new ();

			public CsgTree(List<CsgPolygon> polygons) => Build(polygons);

			CsgTree() { } // grown internally by Build() for a front/back subtree

			// Every method below walks the tree with an explicit, heap-allocated Stack<T> instead of recursing -
			// a BSP tree here can end up as deep as it has polygons (many near-coplanar or disjoint pieces, e.g.
			// CopyToPointsNode stamping the same mesh at a few hundred scattered points, don't split evenly), and a
			// recursive walk over that many stack frames overflows the native call stack. A Stack<T> just grows on
			// the heap instead, so tree depth stops being a crash risk - it's still the same tree, walked the same
			// way, just without using the CLR call stack to remember where to come back to at each level.

			public void Invert()
			{
				var pending = new Stack<CsgTree>();
				pending.Push(this);

				while (pending.Count > 0)
				{
					CheckTimeout();
					var node = pending.Pop();

					for (var i = 0; i < node.polygons.Count; i++)
						node.polygons[i] = node.polygons[i].Flipped();

					if (node.plane.HasValue)
						node.plane = node.plane.Value.Flipped();

					(node.frontTree, node.backTree) = (node.backTree, node.frontTree);

					if (node.frontTree != null)
						pending.Push(node.frontTree);

					if (node.backTree != null)
						pending.Push(node.backTree);
				}
			}

			/// <summary> Removes every part of this tree's polygons that lies inside `other`'s solid - the core
			/// clipping step shared by all three operations. </summary>
			public void ClipTo(CsgTree other) => ClipTo(other, 0);

			// Below MaxParallelDepth, frontTree and backTree are clipped independently - the two subtrees never
			// touch each other's fields, only their own, so there's nothing to synchronize. Falls back to the
			// sequential walk past that depth.
			void ClipTo(CsgTree other, int depth)
			{
				if (depth >= MaxParallelDepth)
				{
					ClipToSequential(other);
					return;
				}

				CheckTimeout();

				var kept = other.ClipPolygons(polygons);
				polygons.Clear();
				polygons.AddRange(kept);

				Task frontTask = null;

				if (frontTree != null)
				{
					var capturedFront = frontTree;
					frontTask = Task.Run(() => capturedFront.ClipTo(other, depth + 1));
				}

				if (backTree != null)
					backTree.ClipTo(other, depth + 1);

				if (frontTask != null)
					WaitFlattened(frontTask);
			}

			void ClipToSequential(CsgTree other)
			{
				var pending = new Stack<CsgTree>();
				pending.Push(this);

				while (pending.Count > 0)
				{
					CheckTimeout();
					var node = pending.Pop();

					var kept = other.ClipPolygons(node.polygons);
					node.polygons.Clear();
					node.polygons.AddRange(kept);

					if (node.frontTree != null)
						pending.Push(node.frontTree);

					if (node.backTree != null)
						pending.Push(node.backTree);
				}
			}

			public List<CsgPolygon> AllPolygons()
			{
				var result = new List<CsgPolygon>();
				var pending = new Stack<CsgTree>();
				pending.Push(this);

				while (pending.Count > 0)
				{
					CheckTimeout();
					var node = pending.Pop();
					result.AddRange(node.polygons);

					if (node.frontTree != null)
						pending.Push(node.frontTree);

					if (node.backTree != null)
						pending.Push(node.backTree);
				}

				return result;
			}

			public void Build(List<CsgPolygon> input) => Build(input, 0);

			// Same depth-capped fan-out as ClipTo above: below MaxParallelDepth and ParallelPolygonThreshold,
			// splits this node's own input, then builds frontTree/backTree independently - neither touches the
			// other's fields, so no synchronization is needed beyond joining the Task before returning.
			void Build(List<CsgPolygon> input, int depth)
			{
				if (depth >= MaxParallelDepth || input.Count < ParallelPolygonThreshold)
				{
					BuildSequential(input);
					return;
				}

				CheckTimeout();

				plane ??= input[0].Plane;

				var frontPolygons = new List<CsgPolygon>();
				var backPolygons = new List<CsgPolygon>();

				foreach (var polygon in input)
					plane.Value.Split(polygon, polygons, polygons, frontPolygons, backPolygons);

				Task frontTask = null;

				if (frontPolygons.Count > 0)
				{
					var capturedFront = frontTree ??= new CsgTree();
					frontTask = Task.Run(() => capturedFront.Build(frontPolygons, depth + 1));
				}

				if (backPolygons.Count > 0)
					(backTree ??= new CsgTree()).Build(backPolygons, depth + 1);

				if (frontTask != null)
					WaitFlattened(frontTask);
			}

			void BuildSequential(List<CsgPolygon> input)
			{
				var pending = new Stack<(CsgTree node, List<CsgPolygon> polygons)>();
				pending.Push((this, input));

				while (pending.Count > 0)
				{
					CheckTimeout();
					var (node, polygonsToAdd) = pending.Pop();
					if (polygonsToAdd.Count == 0)
						continue;

					node.plane ??= polygonsToAdd[0].Plane;

					var frontPolygons = new List<CsgPolygon>();
					var backPolygons = new List<CsgPolygon>();

					foreach (var polygon in polygonsToAdd)
						node.plane.Value.Split(polygon, node.polygons, node.polygons, frontPolygons, backPolygons);

					if (frontPolygons.Count > 0)
						pending.Push((node.frontTree ??= new CsgTree(), frontPolygons));

					if (backPolygons.Count > 0)
						pending.Push((node.backTree ??= new CsgTree(), backPolygons));
				}
			}

			// Same "front/back-of-a-missing-child" rules as the recursive version this replaces (no frontTree ->
			// that portion passes through untouched; no backTree -> that portion is discarded, i.e. it was inside
			// this operand's solid) - just accumulated into one flat list instead of concatenating a result back up
			// through each level, since a plain union doesn't care what order or how deep it came from.
			List<CsgPolygon> ClipPolygons(List<CsgPolygon> input)
			{
				var result = new List<CsgPolygon>();
				var pending = new Stack<(CsgTree node, List<CsgPolygon> polygons)>();
				pending.Push((this, input));

				while (pending.Count > 0)
				{
					CheckTimeout();
					var (node, polygonsToClip) = pending.Pop();

					if (!node.plane.HasValue)
					{
						result.AddRange(polygonsToClip);
						continue;
					}

					var frontPolygons = new List<CsgPolygon>();
					var backPolygons = new List<CsgPolygon>();

					foreach (var polygon in polygonsToClip)
						node.plane.Value.Split(polygon, frontPolygons, backPolygons, frontPolygons, backPolygons);

					if (node.frontTree != null)
						pending.Push((node.frontTree, frontPolygons));
					else
						result.AddRange(frontPolygons);

					if (node.backTree != null)
						pending.Push((node.backTree, backPolygons));
				}

				return result;
			}
		}
	}
}
