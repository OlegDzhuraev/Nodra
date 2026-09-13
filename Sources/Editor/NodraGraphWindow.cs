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

using System.Collections.Generic;
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
		static readonly Color SceneViewBackgroundColor = new (0.16f, 0.16f, 0.16f);

		[SerializeField] ProceduralMeshGenerator target;

		NodraGraphView graphView;
		Label targetLabel;
		Toggle autoGenerateToggle;

		readonly Dictionary<SceneView, (CameraClearFlags clearFlags, Color backgroundColor, bool showSkybox)> sceneViewState = new ();

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

			// The graph is the thing being edited while this window is open - hide the Scene view's built-in
			// Move/Rotate/Scale gizmo so it can't be dragged by accident.
			Tools.hidden = true;
			SceneView.duringSceneGui += ApplySceneViewBackground;

			BuildLayout();

			if (target != null)
				Bind(target);
		}

		void OnDisable()
		{
			Undo.undoRedoPerformed -= OnUndoRedoPerformed;
			Tools.hidden = false;

			SceneView.duringSceneGui -= ApplySceneViewBackground;
			RestoreSceneViewBackgrounds();
		}

		// Skybox/solid-color background is otherwise only reachable from the Scene view's own Camera overlay, per
		// SceneView instance - applied every duringSceneGui call (not just once in OnEnable) since that overlay,
		// or a newly opened Scene view, can otherwise override it while this window stays open.
		void ApplySceneViewBackground(SceneView sceneView)
		{
			var camera = sceneView.camera;
			if (camera == null)
				return;

			if (!sceneViewState.ContainsKey(sceneView))
				sceneViewState[sceneView] = (camera.clearFlags, camera.backgroundColor, sceneView.sceneViewState.showSkybox);

			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = SceneViewBackgroundColor;
			sceneView.sceneViewState.showSkybox = false;
		}

		void RestoreSceneViewBackgrounds()
		{
			foreach (var (sceneView, state) in sceneViewState)
			{
				if (sceneView == null)
					continue;

				if (sceneView.camera != null)
				{
					sceneView.camera.clearFlags = state.clearFlags;
					sceneView.camera.backgroundColor = state.backgroundColor;
				}

				sceneView.sceneViewState.showSkybox = state.showSkybox;
				sceneView.Repaint();
			}

			sceneViewState.Clear();
		}

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
				if (target == null)
					return;

				target.Generate();

				// Jumps the Scene view to the generated result - handy since the graph window itself has no 3D
				// preview of its own.
				Selection.activeGameObject = target.gameObject;
				SceneView.lastActiveSceneView?.FrameSelected();
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
