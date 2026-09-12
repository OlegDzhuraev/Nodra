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
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Nodra
{
	/// <summary> Draws any GeoNodeList field (ProceduralMeshGenerator.Nodes, MergeNode.Branches, ...) as a
	/// reorderable, polymorphic ([SerializeReference]) stack, with an Add dropdown listing every non-abstract
	/// GeoNode type found via TypeCache - instead of Unity's default array drawer, which has no "pick a type" UI
	/// for SerializeReference lists. Being a PropertyDrawer (rather than logic baked into
	/// ProceduralMeshGeneratorEditor) means it also applies automatically to a GeoNodeList nested inside another
	/// node, e.g. MergeNode's embedded branch. </summary>
	[CustomPropertyDrawer(typeof(GeoNodeList))]
	public class GeoNodeListDrawer : PropertyDrawer
	{
		// Keyed by property path since a PropertyDrawer instance can be reused for several fields/rows. Paths embed
		// array indices, so a list can (rarely) end up rebuilt after a reorder elsewhere in the tree shifts an
		// index - harmless, just a redundant rebuild.
		readonly Dictionary<string, ReorderableList> listsByPath = new ();

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			GetOrCreateList(property, label).DoList(position);
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
			GetOrCreateList(property, label).GetHeight();

		ReorderableList GetOrCreateList(SerializedProperty property, GUIContent label)
		{
			if (listsByPath.TryGetValue(property.propertyPath, out var existing))
				return existing;

			var nodesProperty = property.FindPropertyRelative("Nodes");

			var list = new ReorderableList(property.serializedObject, nodesProperty, true, true, true, true)
			{
				drawHeaderCallback = rect => EditorGUI.LabelField(rect, label),
				elementHeightCallback = index => EditorGUI.GetPropertyHeight(nodesProperty.GetArrayElementAtIndex(index), true) + 4f,
				drawElementCallback = (rect, index, _, _) =>
				{
					var element = nodesProperty.GetArrayElementAtIndex(index);

					rect.y += 2f;
					rect.height -= 4f;

					EditorGUI.PropertyField(rect, element, new GUIContent(GetNodeLabel(element)), true);
				},
				onAddDropdownCallback = (_, _) => ShowAddNodeMenu(nodesProperty),
			};

			listsByPath[property.propertyPath] = list;
			return list;
		}

		static string GetNodeLabel(SerializedProperty element)
		{
			var fullTypeName = element.managedReferenceFullTypename;
			if (string.IsNullOrEmpty(fullTypeName))
				return "(missing node)";

			var spaceIndex = fullTypeName.IndexOf(' ');
			var typeName = spaceIndex >= 0 ? fullTypeName[(spaceIndex + 1)..] : fullTypeName;
			var lastDot = typeName.LastIndexOf('.');

			return lastDot >= 0 ? typeName[(lastDot + 1)..] : typeName;
		}

		static void ShowAddNodeMenu(SerializedProperty nodesProperty)
		{
			var menu = new GenericMenu();

			foreach (var type in TypeCache.GetTypesDerivedFrom<GeoNode>())
			{
				if (type.IsAbstract)
					continue;

				menu.AddItem(new GUIContent(type.Name), false, () => AddNode(nodesProperty, type));
			}

			menu.ShowAsContext();
		}

		static void AddNode(SerializedProperty nodesProperty, Type type)
		{
			nodesProperty.serializedObject.Update();

			var index = nodesProperty.arraySize;
			nodesProperty.arraySize++;
			nodesProperty.GetArrayElementAtIndex(index).managedReferenceValue = Activator.CreateInstance(type);

			nodesProperty.serializedObject.ApplyModifiedProperties();
		}
	}
}
