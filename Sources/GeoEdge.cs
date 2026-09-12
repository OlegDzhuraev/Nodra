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

namespace Nodra
{
	/// <summary> A single connection in a GeoGraph, from one node's output to a specific input port of another -
	/// referencing both ends by GeoNode.Id rather than by list index, so edges stay valid across reorders. </summary>
	[Serializable]
	public class GeoEdge
	{
		public string FromNodeId;
		public string ToNodeId;
		public int ToPortIndex;
	}
}
