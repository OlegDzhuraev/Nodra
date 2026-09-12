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
	/// <summary> Visual editor for one ProceduralMeshGenerator's GeoGraph - a toolbar (target label, Auto Generate,
	/// Generate) above a NodraGraphView. Opened from ProceduralMeshGeneratorEditor's "Open Graph Editor" button;
	/// only one target is bound at a time, re-bound whenever a different generator's button is clicked. </summary>
	public class NodraGraphWindow : EditorWindow
	{
		[SerializeField] ProceduralMeshGenerator target;

		NodraGraphView graphView;
		Label targetLabel;
		Toggle autoGenerateToggle;

		public static void Open(ProceduralMeshGenerator generator)
		{
			var window = GetWindow<NodraGraphWindow>();
			window.titleContent = new GUIContent("Nodra Graph");
			window.minSize = new Vector2(400f, 250f);
			window.Bind(generator);
		}

		void OnEnable()
		{
			Undo.undoRedoPerformed += OnUndoRedoPerformed;
			BuildLayout();

			if (target != null)
				Bind(target);
		}

		void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedoPerformed;

		void BuildLayout()
		{
			rootVisualElement.Clear();

			var toolbar = new Toolbar();

			targetLabel = new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 6f, marginRight = 6f } };
			toolbar.Add(targetLabel);

			toolbar.Add(new VisualElement { style = { flexGrow = 1f } });

			autoGenerateToggle = new Toggle("Auto Generate");
			autoGenerateToggle.RegisterValueChangedCallback(evt =>
			{
				if (target == null)
					return;

				Undo.RecordObject(target, "Toggle Auto Generate");
				target.AutoGenerate = evt.newValue;
				EditorUtility.SetDirty(target);

				if (evt.newValue)
					target.Generate();
			});
			toolbar.Add(autoGenerateToggle);

			var generateButton = new ToolbarButton(() =>
			{
				if (target != null)
					target.Generate();
			}) { text = "Generate" };
			toolbar.Add(generateButton);

			rootVisualElement.Add(toolbar);

			graphView = new NodraGraphView { style = { flexGrow = 1 } };
			rootVisualElement.Add(graphView);
		}

		void Bind(ProceduralMeshGenerator generator)
		{
			target = generator;

			if (graphView == null)
				BuildLayout();

			targetLabel.text = target != null ? target.name : "(no target)";
			autoGenerateToggle.SetValueWithoutNotify(target != null && target.AutoGenerate);

			graphView.Bind(target, OnGraphChanged);
		}

		void OnUndoRedoPerformed()
		{
			if (target == null)
				return;

			graphView.Populate();
			autoGenerateToggle.SetValueWithoutNotify(target.AutoGenerate);
		}

		void OnGraphChanged()
		{
			if (target != null && target.AutoGenerate)
				target.Generate();
		}

		void Update()
		{
			// The bound target can be destroyed (deleted GameObject, scene closed) without this window knowing -
			// clear the label instead of holding a dangling reference or throwing on the next graph edit.
			if (target == null && targetLabel != null && targetLabel.text != "(no target)")
				targetLabel.text = "(no target)";
		}
	}
}
