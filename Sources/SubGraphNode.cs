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
	/// <summary> Evaluates a separate GeoGraphAsset and hands back its output - lets one graph reuse another's
	/// result (a shared base shape, a common detail pass, ...) instead of duplicating its nodes. InputCount 0: it's
	/// a self-contained generator, like *GeneratorNode.cs, not a modifier of whatever feeds it - it has no input to
	/// forward the referenced graph's own result on top of. Null SubGraph (or an asset whose graph has no output)
	/// just produces null, same as any other not-yet-configured node.
	///
	/// SubGraph is a UnityEngine.Object reference - GeoGraph.ComputeNodeHash's JsonUtility-based hash has no
	/// reliable way to see either which asset is referenced or what's currently inside it, so ExtraHash below folds
	/// in the referenced asset's own entity ID (GetEntityId - identifies which asset, cheaper than comparing
	/// references across domain reloads) plus its content hash (GeoGraph.ComputeOutputHash) explicitly. Without
	/// that, editing the referenced graph elsewhere (or swapping which asset is referenced) wouldn't invalidate
	/// this node's GeoGraph.resultCache entry - it would keep returning whatever it last computed. </summary>
	[Serializable]
	public class SubGraphNode : GeoNode
	{
		public override string Category => "Generators";

		public override int InputCount => 0;

		public override string EditableAssetFieldName => nameof(SubGraph);

		public GeoGraphAsset SubGraph;

		// Guards against a cycle - asset A referencing B referencing A, directly or through more hops. Static and
		// shared between Process() and ExtraHash (rather than a plain per-call HashSet, the way GeoGraph.EvaluateNode
		// guards in-graph cycles) since either can recurse several GeoGraphAssets deep through another SubGraphNode's
		// own Process()/ExtraHash, each hop starting its own fresh top-level Evaluate()/ComputeOutputHash() call on a
		// different GeoGraph instance - none of which would otherwise have any memory of an asset already being
		// visited further up the chain.
		static readonly HashSet<GeoGraphAsset> visiting = new ();

		public override GeoData Process(GeoData[] inputs)
		{
			if (SubGraph == null || SubGraph.Graph == null)
				return null;

			if (!visiting.Add(SubGraph))
			{
				Debug.LogWarning($"Nodra: SubGraphNode cycle detected on '{SubGraph.name}' - treating it as empty.", SubGraph);
				return null;
			}

			try
			{
				return SubGraph.Graph.Evaluate();
			}
			finally
			{
				visiting.Remove(SubGraph);
			}
		}

		public override string ExtraHash
		{
			get
			{
				if (SubGraph == null || SubGraph.Graph == null)
					return "none";

				// Process() above already warns about a cycle - this just needs to return something that isn't a
				// plausible real hash, so a cyclic reference doesn't look identical to whatever it last computed.
				if (!visiting.Add(SubGraph))
					return "cycle";

				try
				{
					return SubGraph.GetEntityId() + ":" + SubGraph.Graph.ComputeOutputHash();
				}
				finally
				{
					visiting.Remove(SubGraph);
				}
			}
		}
	}
}
