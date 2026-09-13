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

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nodra
{
	/// <summary> Vector2IntField that clamps to MinVector2IntAttribute's MinX/MinY on every edit, instead of
	/// letting a too-small/negative value sit in the field until the node's own internal clamp silently
	/// overrides it at generation time. NodraNodeView's plain PropertyField picks this up automatically. </summary>
	[CustomPropertyDrawer(typeof(MinVector2IntAttribute))]
	public class MinVector2IntDrawer : PropertyDrawer
	{
		public override VisualElement CreatePropertyGUI(SerializedProperty property)
		{
			var attr = (MinVector2IntAttribute) attribute;
			var min = new Vector2Int(attr.MinX, attr.MinY);

			var field = new Vector2IntField(property.displayName);
			field.BindProperty(property);

			field.RegisterValueChangedCallback(evt =>
			{
				var clamped = Vector2Int.Max(min, evt.newValue);
				if (clamped != evt.newValue)
					field.SetValueWithoutNotify(clamped);

				// BindProperty's own sync may or may not pick up a SetValueWithoutNotify correction on its own -
				// written through explicitly here so the clamp is guaranteed to land in the serialized data too.
				if (property.vector2IntValue != clamped)
				{
					property.vector2IntValue = clamped;
					property.serializedObject.ApplyModifiedProperties();
				}
			});

			return field;
		}
	}
}
