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
using UnityEngine;

namespace Nodra
{
	/// <summary> The Graph field itself is edited visually in NodraGraphWindow, not in the Inspector - this editor
	/// just adds the AutoGenerate toggle, the button that opens the graph window, and the Generate/Save buttons. </summary>
	[CustomEditor(typeof(ProceduralMeshGenerator))]
	public class ProceduralMeshGeneratorEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoGenerate"));
			serializedObject.ApplyModifiedProperties();

			EditorGUILayout.Space();

			if (GUILayout.Button("Open Graph Editor", GUILayout.Height(24f)))
				NodraGraphWindow.Open((ProceduralMeshGenerator) target);

			EditorGUILayout.Space();

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Generate"))
				{
					foreach (var generator in targets)
						((ProceduralMeshGenerator) generator).Generate();

					SelectAndFrame(targets);
				}

				if (GUILayout.Button("Save Mesh to Project..."))
					foreach (var generator in targets)
						SaveMeshToProject((ProceduralMeshGenerator) generator);
			}
		}

		// Jumps the Scene view to whatever was just (re)generated - Generate is usually clicked right after tweaking
		// a node whose result might now sit far from the current view (a big Transform offset, a fresh generator
		// placed elsewhere, ...), so this saves a manual hunt for it.
		static void SelectAndFrame(Object[] targets)
		{
			var gameObjects = new Object[targets.Length];
			for (var i = 0; i < targets.Length; i++)
				gameObjects[i] = ((ProceduralMeshGenerator) targets[i]).gameObject;

			Selection.objects = gameObjects;
			SceneView.lastActiveSceneView?.FrameSelected();
		}

		static readonly Color PointCloudColor = new (0.3f, 0.85f, 1f, 0.9f);
		static readonly Color ControlPointColor = new (1f, 0.85f, 0.1f, 0.95f);

		void OnSceneGUI()
		{
			var generator = (ProceduralMeshGenerator) target;

			DrawPointCloud(generator);
			DrawSplineControlPoints(generator);
		}

		// A point-cloud result (ScatterNode's/LineGeneratorNode's typical output - or any other node whose result
		// happens to have points but no faces) bakes into a Mesh with vertices but zero triangles - nothing to see
		// there, so its points are drawn directly in the Scene view instead. Judged purely by the data's own shape,
		// not by which node produced it, so this covers any such node without needing to know about it by name;
		// a real mesh already shows itself and doesn't need this.
		static void DrawPointCloud(ProceduralMeshGenerator generator)
		{
			var data = generator.LastEvaluatedData;
			if (data == null || data.PointCount == 0 || data.Primitives.Count > 0)
				return;

			var previousColor = Handles.color;
			Handles.color = PointCloudColor;

			for (var i = 0; i < data.PointCount; i++)
			{
				var worldPosition = generator.transform.TransformPoint(data.Points[i]);
				var size = HandleUtility.GetHandleSize(worldPosition) * 0.05f;

				Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, size, EventType.Repaint);

				if (i < data.Normals.Count)
					Handles.DrawLine(worldPosition, worldPosition + generator.transform.TransformDirection(data.Normals[i]) * (size * 6f));
			}

			Handles.color = previousColor;
		}

		// Drawn (and draggable) for every SplineGeneratorNode in the graph regardless of which one is the current
		// Output - control points are authored data on the node itself, not part of any evaluated GeoData, so you
		// can see/drag the curve's shape in the Scene view even while something further downstream is what
		// actually gets generated. Dragged through SerializedProperty rather than the live field directly, same as
		// every other node field edited from NodraGraphView - gets Undo and the AutoGenerate/OnValidate path for free.
		void DrawSplineControlPoints(ProceduralMeshGenerator generator)
		{
			if (generator.Graph?.Nodes == null)
				return;

			serializedObject.Update();
			var nodesProperty = serializedObject.FindProperty(nameof(ProceduralMeshGenerator.Graph)).FindPropertyRelative(nameof(GeoGraph.Nodes));

			var previousColor = Handles.color;
			Handles.color = ControlPointColor;

			for (var nodeIndex = 0; nodeIndex < generator.Graph.Nodes.Count; nodeIndex++)
			{
				if (generator.Graph.Nodes[nodeIndex] is not SplineGeneratorNode spline || spline.ControlPoints == null || spline.ControlPoints.Count == 0)
					continue;

				var controlPointsProperty = nodesProperty.GetArrayElementAtIndex(nodeIndex).FindPropertyRelative(nameof(SplineGeneratorNode.ControlPoints));

				for (var i = 0; i < spline.ControlPoints.Count; i++)
				{
					var worldPosition = generator.transform.TransformPoint(spline.ControlPoints[i]);
					var size = HandleUtility.GetHandleSize(worldPosition) * 0.05f;

					EditorGUI.BeginChangeCheck();
					var moved = Handles.FreeMoveHandle(worldPosition, size, Vector3.zero, Handles.SphereHandleCap);

					if (EditorGUI.EndChangeCheck())
					{
						controlPointsProperty.GetArrayElementAtIndex(i).vector3Value = generator.transform.InverseTransformPoint(moved);
						serializedObject.ApplyModifiedProperties();
					}
				}

				var count = spline.ControlPoints.Count;
				var segments = spline.Closed ? count : count - 1;

				for (var i = 0; i < segments; i++)
				{
					var from = generator.transform.TransformPoint(spline.ControlPoints[i]);
					var to = generator.transform.TransformPoint(spline.ControlPoints[(i + 1) % count]);

					Handles.DrawLine(from, to);
				}
			}

			Handles.color = previousColor;
		}

		// Regenerates first, so the saved asset always matches the graph's current settings even if the user
		// forgot to click Generate (or has AutoGenerate off) since the last edit. AssetDatabase.CreateAsset is
		// called directly on that freshly built Mesh rather than a copy of it - the MeshFilter keeps pointing at
		// the exact object that's now also a project asset, and the next Generate() simply swaps in another
		// in-memory Mesh, leaving this saved one untouched on disk.
		static void SaveMeshToProject(ProceduralMeshGenerator generator)
		{
			generator.Generate();

			var meshFilter = generator.GetComponent<MeshFilter>();
			var mesh = meshFilter ? meshFilter.sharedMesh : null;

			if (!mesh)
			{
				EditorUtility.DisplayDialog("Save Mesh", "Nothing to save - the graph produced no mesh.", "OK");
				return;
			}

			var path = EditorUtility.SaveFilePanelInProject("Save Procedural Mesh", mesh.name, "asset",
				"Choose where to save the generated mesh.");

			if (string.IsNullOrEmpty(path))
				return;

			// The native save panel already confirmed overwriting with the user if the path existed - but
			// CreateAsset itself refuses to write over an existing asset, so the old one has to go first.
			if (AssetDatabase.LoadAssetAtPath<Mesh>(path))
				AssetDatabase.DeleteAsset(path);

			AssetDatabase.CreateAsset(mesh, path);
			AssetDatabase.SaveAssets();

			EditorUtility.FocusProjectWindow();
			Selection.activeObject = mesh;
		}
	}
}
