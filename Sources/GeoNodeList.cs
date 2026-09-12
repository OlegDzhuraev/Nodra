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
	/// <summary> An ordered, polymorphic list of GeoNodes with the shared "run them in order, skip disabled ones"
	/// logic - used both as ProceduralMeshGenerator's top-level pipeline and as MergeNode's embedded sub-pipeline.
	/// A dedicated wrapper (instead of a plain List&lt;GeoNode&gt; field) so a single PropertyDrawer
	/// (GeoNodeListDrawer) can draw both cases as a reorderable, "pick a node type" list, including nested ones. </summary>
	[Serializable]
	public class GeoNodeList
	{
		[SerializeReference] public List<GeoNode> Nodes = new ();

		public GeoData Process(GeoData input)
		{
			var data = input;

			foreach (var node in Nodes)
			{
				if (node is not { Enabled: true })
					continue;

				data = node.Process(data);
			}

			return data;
		}
	}
}
