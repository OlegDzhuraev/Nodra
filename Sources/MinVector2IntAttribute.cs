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
	/// <summary> Clamps a Vector2Int field's X/Y to MinX/MinY live as the user types - Unity's built-in [Min]
	/// only supports float/int/Vector2/Vector3/Vector4, not the Int vector types, so a Resolution field otherwise
	/// accepts a negative value in the field itself even though a node's own Process() clamps it internally. </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public class MinVector2IntAttribute : PropertyAttribute
	{
		public readonly int MinX, MinY;

		public MinVector2IntAttribute(int min) : this(min, min) { }

		public MinVector2IntAttribute(int minX, int minY)
		{
			MinX = minX;
			MinY = minY;
		}
	}
}
