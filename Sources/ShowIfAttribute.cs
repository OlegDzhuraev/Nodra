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
	/// <summary> Hides this field in the node graph editor unless the sibling enum field named `field` currently
	/// holds one of `values` - e.g. [ShowIf(nameof(Mode), ColorMode.Gradient)]. Stack more than one on the same
	/// field for an AND (NodraNodeView requires every attribute's condition to hold); each individual attribute's
	/// own `values` are OR'd together. Enum fields only - the sibling is read via SerializedProperty.intValue,
	/// which for an enum is its actual underlying int. Purely a display-time filter: a hidden field keeps whatever
	/// value it already had and Process() still reads it normally, so toggling Mode back and forth never loses
	/// data the way clearing a hidden field on hide would. </summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
	public class ShowIfAttribute : Attribute
	{
		public readonly string Field;
		public readonly object[] Values;

		public ShowIfAttribute(string field, params object[] values)
		{
			Field = field;
			Values = values;
		}
	}
}
