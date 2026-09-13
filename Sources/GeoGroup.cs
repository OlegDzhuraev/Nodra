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

namespace Nodra
{
	/// <summary> A named visual grouping of nodes in a GeoGraph - purely organizational, has no effect on
	/// Evaluate(). Membership is kept by GeoNode.Id rather than by list index, same as GeoEdge, so it stays valid
	/// across reorders; a stale id (its node got deleted without going through NodraGraphView) is just skipped when
	/// the group is rebuilt. Position/size aren't stored here - GraphView's own Group re-fits itself to whichever
	/// member nodes are actually present every time the view repopulates. </summary>
	[Serializable]
	public class GeoGroup
	{
		public string Title = "New Group";
		public List<string> NodeIds = new ();
	}
}
