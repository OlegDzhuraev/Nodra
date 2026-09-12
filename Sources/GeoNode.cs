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
	/// <summary> One step of a procedural geometry pipeline. ProceduralMeshGenerator
	/// runs a list of these in order, threading a single GeoData instance through: generator nodes usually ignore
	/// the input and add fresh points/primitives to it, modifier nodes mutate what they receive, and nodes like
	/// Scatter deliberately return a brand new GeoData instead of passing the input through. </summary>
	[Serializable]
	public abstract class GeoNode
	{
		public bool Enabled = true;

		public abstract GeoData Process(GeoData input);
	}
}
