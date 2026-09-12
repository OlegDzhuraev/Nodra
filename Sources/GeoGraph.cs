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

		/// <summary> Which node's output becomes the final mesh. Empty/stale means "figure it out" - see
		/// FindDefaultOutput - so a freshly-built linear chain works without the user ever touching this. </summary>
		public string OutputNodeId;

		/// <summary> Which node's output the graph currently resolves as its final mesh - explicit OutputNodeId if
		/// it still points at a real node, otherwise whichever node feeds nothing else (see FindDefaultOutput).
		/// Exposed so the graph editor can highlight it, not just Evaluate(). </summary>
		public GeoNode GetOutputNode()
		{
			if (!string.IsNullOrEmpty(OutputNodeId))
				foreach (var node in Nodes)
					if (node != null && node.Id == OutputNodeId)
						return node;

			return FindDefaultOutput();
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

			return EvaluateNode(outputNode, byId, consumerCount, new Dictionary<string, GeoData>(), new HashSet<string>());
		}

		GeoData EvaluateNode(GeoNode node, Dictionary<string, GeoNode> byId, Dictionary<string, int> consumerCount,
			Dictionary<string, GeoData> cache, HashSet<string> visiting)
		{
			if (cache.TryGetValue(node.Id, out var cached))
				return cached;

			if (!visiting.Add(node.Id))
			{
				Debug.LogWarning($"Nodra: cycle detected at node '{node.Id}' - treating it as unconnected.");
				return null;
			}

			var inputs = new GeoData[Mathf.Max(node.InputCount, 1)];

			foreach (var edge in Edges)
			{
				if (edge.ToNodeId != node.Id || edge.ToPortIndex < 0 || edge.ToPortIndex >= inputs.Length)
					continue;

				if (!byId.TryGetValue(edge.FromNodeId, out var source))
					continue;

				var sourceData = EvaluateNode(source, byId, consumerCount, cache, visiting);
				var hasMultipleConsumers = consumerCount.GetValueOrDefault(source.Id) > 1;

				inputs[edge.ToPortIndex] = sourceData != null && hasMultipleConsumers ? sourceData.Clone() : sourceData;
			}

			// A disabled node acts as if it weren't there - its first input passes straight through, same as
			// skipping a step used to in the old linear GeoNodeList.
			var result = node.Enabled ? node.Process(inputs) : inputs[0];

			visiting.Remove(node.Id);
			cache[node.Id] = result;
			return result;
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
