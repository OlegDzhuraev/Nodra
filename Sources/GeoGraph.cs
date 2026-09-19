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
	/// <summary> A procedural geometry graph: a polymorphic ([SerializeReference]) bag of GeoNodes plus the edges
	/// connecting their ports - drawn and edited as a visual graph by NodraGraphWindow. Replaces the old flat
	/// GeoNodeList; nodes no longer run in a fixed order, each one instead pulls its input(s) from whatever is
	/// wired into its input ports. </summary>
	[Serializable]
	public class GeoGraph
	{
		[SerializeReference] public List<GeoNode> Nodes = new ();
		public List<GeoEdge> Edges = new ();
		public List<GeoGroup> Groups = new ();

		/// <summary> Per-node memo of the last Process() result, keyed by node Id, alongside the content hash it was
		/// computed from - see EvaluateNode. Survives across separate Evaluate() calls (e.g. every keystroke while
		/// AutoGenerate is on) so editing one node's fields doesn't force its untouched upstream chain to redo
		/// expensive work (Boolean/Subdivide/Decimate, ...) it already did last time. Deliberately not serialized:
		/// it's a pure runtime speedup, GeoData isn't [Serializable] anyway, and starting empty after a domain
		/// reload or scene load just means the next Evaluate() populates it fresh, same as today. </summary>
		readonly Dictionary<string, (string Hash, GeoData Data)> resultCache = new ();

		/// <summary> Reads back whatever the last Evaluate() computed for a specific node, straight from resultCache
		/// - used by the graph editor's Bounds gizmo to show what's actually flowing into a node's Bounds input
		/// port without re-running any part of the graph itself. Null if that node hasn't been evaluated yet
		/// (nothing wired to it, disabled, or no Evaluate() has run since it entered the graph). </summary>
		public GeoData TryGetCachedResult(string nodeId) => resultCache.TryGetValue(nodeId, out var entry) ? entry.Data : null;

		/// <summary> Which node's output the graph currently resolves as its final mesh - a GeometryOutputNode if
		/// the graph has one, otherwise whichever node feeds nothing else (see FindDefaultOutput). Exposed so the
		/// graph editor can highlight it, not just Evaluate(). </summary>
		public GeoNode GetOutputNode()
		{
			GeoNode explicitOutput = null;

			foreach (var node in Nodes)
				if (node is GeometryOutputNode)
					explicitOutput = node;

			return explicitOutput ?? FindDefaultOutput();
		}

		/// <summary> When true, Evaluate() logs one line per call - how many nodes were reused from resultCache vs.
		/// actually recomputed, and how long the whole call took (see ProceduralMeshGeneratorEditor's "Log Cache
		/// Stats" toggle). Off by default: this is a debugging aid for checking the cache is doing what's expected,
		/// not something to leave spamming the Console during normal use. </summary>
		public static bool LogCacheStats;

		/// <summary> The same combined hash Evaluate() would end up computing for the current output node, but
		/// without producing (or cloning) a single GeoData along the way - just JsonUtility hashing every reachable
		/// node's own fields. AutoGenerate's OnValidate fires on every field edit anywhere in the graph with no way
		/// to tell whether it could possibly matter, so ProceduralMeshGenerator.Generate() calls this FIRST and only
		/// pays for a real Evaluate() (and everything that comes with it - Process(), and a Clone() of every reused
		/// node along the reachable chain, to keep resultCache's own copies pristine) when the hash actually moved.
		/// Without this, editing a node that's nowhere near the output still clones the entire reachable chain's
		/// result on every single frame of a slider drag, for a mesh that provably isn't going to change. </summary>
		public string ComputeOutputHash()
		{
			var outputNode = GetOutputNode();
			if (outputNode == null)
				return null;

			var byId = new Dictionary<string, GeoNode>();
			foreach (var node in Nodes)
				if (node != null && !string.IsNullOrEmpty(node.Id))
					byId[node.Id] = node;

			return ComputeNodeHashOnly(outputNode, byId, new Dictionary<string, string>(), new HashSet<string>());
		}

		// Mirrors EvaluateNode's own edge-walk and hash formula exactly (same ToPortIndex bounds check, same
		// ComputeNodeHash(node) + "#" + sorted "port:sourceHash" join) - keep the two in sync if that formula ever
		// changes, since ProceduralMeshGenerator.Generate() relies on them producing identical strings for
		// identical graph state. Deliberately doesn't touch resultCache, node.Enabled, or Process() at all: a
		// hash-only walk has nothing to cache or run, only fields to read.
		string ComputeNodeHashOnly(GeoNode node, Dictionary<string, GeoNode> byId, Dictionary<string, string> hashes, HashSet<string> visiting)
		{
			if (hashes.TryGetValue(node.Id, out var cached))
				return cached;

			if (!visiting.Add(node.Id))
				return null; // cycle - EvaluateNode logs this case itself; a second warning here would be redundant

			var inputCount = Mathf.Max(node.InputCount, 1);
			var inputHashes = new List<string>();

			foreach (var edge in Edges)
			{
				if (edge.ToNodeId != node.Id || edge.ToPortIndex < 0 || edge.ToPortIndex >= inputCount)
					continue;

				if (!byId.TryGetValue(edge.FromNodeId, out var source))
					continue;

				inputHashes.Add($"{edge.ToPortIndex}:{ComputeNodeHashOnly(source, byId, hashes, visiting)}");
			}

			inputHashes.Sort();
			var hash = ComputeNodeHash(node) + "#" + string.Join(",", inputHashes);
			hashes[node.Id] = hash;
			visiting.Remove(node.Id);
			return hash;
		}

		public GeoData Evaluate()
		{
			var outputNode = GetOutputNode();
			if (outputNode == null)
				return null;

			var byId = new Dictionary<string, GeoNode>();
			foreach (var node in Nodes)
				if (node != null && !string.IsNullOrEmpty(node.Id))
					byId[node.Id] = node;

			// A node that feeds more than one downstream consumer needs each of them to see an untouched copy of
			// its output - several nodes (TransformNode, NoiseDisplaceNode, ExtrudeNode, MergeNode's base input...)
			// mutate the GeoData they receive in place, which would otherwise let one branch corrupt another's
			// input. The final mesh result counts as one more consumer of the output node specifically, so it's
			// protected the same way if that node also happens to feed something else.
			var consumerCount = new Dictionary<string, int>();
			foreach (var edge in Edges)
				consumerCount[edge.FromNodeId] = consumerCount.GetValueOrDefault(edge.FromNodeId) + 1;

			consumerCount[outputNode.Id] = consumerCount.GetValueOrDefault(outputNode.Id) + 1;

			var hashes = new Dictionary<string, string>();
			var stats = LogCacheStats ? new EvalStats() : null;
			var stopwatch = LogCacheStats ? System.Diagnostics.Stopwatch.StartNew() : null;

			var result = EvaluateNode(outputNode, byId, consumerCount, new Dictionary<string, GeoData>(), hashes, new HashSet<string>(), stats);

			PruneCache(byId);

			if (stats != null)
				Debug.Log($"Nodra: {stats.Hits} reused / {stats.Misses} recomputed of {stats.Hits + stats.Misses} " +
					$"enabled node(s) in {stopwatch.ElapsedMilliseconds}ms");

			return result;
		}

		class EvalStats
		{
			public int Hits, Misses;
		}

		GeoData EvaluateNode(GeoNode node, Dictionary<string, GeoNode> byId, Dictionary<string, int> consumerCount,
			Dictionary<string, GeoData> cache, Dictionary<string, string> hashes, HashSet<string> visiting, EvalStats stats)
		{
			if (cache.TryGetValue(node.Id, out var cached))
				return cached;

			if (!visiting.Add(node.Id))
			{
				Debug.LogWarning($"Nodra: cycle detected at node '{node.Id}' - treating it as unconnected.");
				return null;
			}

			var inputs = new GeoData[Mathf.Max(node.InputCount, 1)];
			var inputHashes = new List<string>();

			foreach (var edge in Edges)
			{
				if (edge.ToNodeId != node.Id || edge.ToPortIndex < 0 || edge.ToPortIndex >= inputs.Length)
					continue;

				if (!byId.TryGetValue(edge.FromNodeId, out var source))
					continue;

				var sourceData = EvaluateNode(source, byId, consumerCount, cache, hashes, visiting, stats);
				var hasMultipleConsumers = consumerCount.GetValueOrDefault(source.Id) > 1;

				inputs[edge.ToPortIndex] = sourceData != null && hasMultipleConsumers ? sourceData.Clone() : sourceData;

				// Folds in the whole upstream chain's hash, not just this one source - so a change anywhere further
				// back still invalidates every descendant, exactly as if everything were recomputed from scratch.
				inputHashes.Add($"{edge.ToPortIndex}:{hashes.GetValueOrDefault(source.Id)}");
			}

			// Edges isn't guaranteed to come back in port order, and dictionary/list iteration order isn't a
			// content property anyway - sorted so the hash only reflects what's actually wired in, not the order
			// GeoEdge entries happen to sit in.
			var hashTimer = stats != null ? System.Diagnostics.Stopwatch.StartNew() : null;
			inputHashes.Sort();
			var hash = ComputeNodeHash(node) + "#" + string.Join(",", inputHashes);
			hashes[node.Id] = hash;
			hashTimer?.Stop();

			GeoData result;
			var workTimer = stats != null ? System.Diagnostics.Stopwatch.StartNew() : null;
			string status;

			if (!node.Enabled)
			{
				// A disabled node acts as if it weren't there - its first input passes straight through, same as
				// skipping a step used to in the old linear GeoNodeList. Nothing to cache, this is already free.
				result = inputs[0];
				status = "disabled";
			}
			else if (resultCache.TryGetValue(node.Id, out var entry) && entry.Hash == hash)
			{
				// Cache hit: whoever consumes this below could still mutate it in place (many nodes do), so what
				// leaves here is always a fresh clone - the cached copy has to stay pristine forever, since it
				// might be handed out again on some future Evaluate() long after this one returns.
				result = entry.Data?.Clone();
				status = "reused";
				if (stats != null) stats.Hits++;
			}
			else
			{
				result = node.Process(inputs);
				resultCache[node.Id] = (hash, result?.Clone());
				status = "recomputed";
				if (stats != null) stats.Misses++;
			}

			workTimer?.Stop();

			// Split into hash-vs-work so a slow line points at the right culprit - a node whose own Process() (or,
			// for a cache hit, GeoData.Clone()) is genuinely the expensive part looks very different from one
			// where JsonUtility hashing itself is unexpectedly the cost (e.g. a big embedded array/list field).
			if (stats != null)
				Debug.Log($"Nodra:   {status} {node.GetType().Name} - hash {hashTimer.ElapsedMilliseconds}ms, work {workTimer.ElapsedMilliseconds}ms");

			visiting.Remove(node.Id);
			cache[node.Id] = result;
			return result;
		}

		// Drops cache entries for nodes no longer in the graph (deleted, or undone away) - otherwise every node
		// ever created in a long editing session would sit in resultCache forever, each pinning one GeoData alive.
		void PruneCache(Dictionary<string, GeoNode> byId)
		{
			if (resultCache.Count == 0)
				return;

			var stale = new List<string>();

			foreach (var id in resultCache.Keys)
				if (!byId.ContainsKey(id))
					stale.Add(id);

			foreach (var id in stale)
				resultCache.Remove(id);
		}

		// A snapshot of everything about this node Process() could possibly read, used to tell whether it needs to
		// re-run at all. JsonUtility (not manual field ToString()/reflection) so nested Unity types a node might
		// hold - Gradient, Bounds, Color, ... - are compared on their actual data, not just object identity.
		// Position is graph-editor canvas placement only, never read by Process(), so it's zeroed for the hash -
		// otherwise merely dragging a node around would look like a content change and needlessly bust its cache.
		static string ComputeNodeHash(GeoNode node)
		{
			var position = node.Position;
			node.Position = default;

			string json;
			try
			{
				json = JsonUtility.ToJson(node);
			}
			finally
			{
				node.Position = position;
			}

			// Almost always null (GeoNode.ExtraHash's default) - only SubGraphNode overrides it, to fold in
			// whatever its referenced GeoGraphAsset's own content hash is, since JsonUtility has no way to capture
			// that itself (see ExtraHash's own doc comment).
			return node.ExtraHash is { } extra ? json + "|" + extra : json;
		}

		/// <summary> With no explicit output chosen, the output is whichever node feeds nothing else - the end of
		/// the chain. Several such dangling nodes (mid-edit, or multiple disconnected branches) resolve to
		/// whichever comes last in Nodes, so a simple linear chain behaves exactly like the old GeoNodeList did. </summary>
		GeoNode FindDefaultOutput()
		{
			var hasOutgoingEdge = new HashSet<string>();
			foreach (var edge in Edges)
				hasOutgoingEdge.Add(edge.FromNodeId);

			GeoNode candidate = null;
			foreach (var node in Nodes)
				if (node != null && !hasOutgoingEdge.Contains(node.Id))
					candidate = node;

			return candidate;
		}
	}
}
