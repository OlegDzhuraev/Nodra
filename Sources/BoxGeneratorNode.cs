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
	/// <summary> Generates a box centered on the origin with a separate set of 4 points per face
	/// (hard edges, flat shading). </summary>
	[Serializable]
	public class BoxGeneratorNode : GeoNode
	{
		public Vector3 Size = Vector3.one;

		public override GeoData Process(GeoData input)
		{
			var data = input ?? new GeoData();
			var h = Size * 0.5f;

			AddFace(data, new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), Vector3.up); // top
			AddFace(data, new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), Vector3.down); // bottom
			AddFace(data, new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, -h.y, h.z), Vector3.right); // right
			AddFace(data, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), Vector3.left); // left
			AddFace(data, new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.forward); // front
			AddFace(data, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, -h.y, -h.z), Vector3.back); // back

			return data;
		}

		static void AddFace(GeoData data, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
		{
			var i0 = data.AddPoint(a, normal, new Vector2(0f, 0f));
			var i1 = data.AddPoint(b, normal, new Vector2(0f, 1f));
			var i2 = data.AddPoint(c, normal, new Vector2(1f, 1f));
			var i3 = data.AddPoint(d, normal, new Vector2(1f, 0f));

			data.AddPrimitive(i0, i1, i2, i3);
		}
	}
}
