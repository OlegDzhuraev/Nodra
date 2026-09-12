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
	/// <summary> The Nodes field itself is drawn by GeoNodeListDrawer (registered for the GeoNodeList type) - this
	/// editor adds the AutoGenerate toggle above it and the Generate/Save buttons below. </summary>
	[CustomEditor(typeof(ProceduralMeshGenerator))]
	public class ProceduralMeshGeneratorEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("AutoGenerate"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("Nodes"));
			serializedObject.ApplyModifiedProperties();

			EditorGUILayout.Space();

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Generate"))
					foreach (var generator in targets)
						((ProceduralMeshGenerator) generator).Generate();

				if (GUILayout.Button("Save Mesh to Project..."))
					foreach (var generator in targets)
						SaveMeshToProject((ProceduralMeshGenerator) generator);
			}
		}

		// Regenerates first, so the saved asset always matches the pipeline's current settings even if the user
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
				EditorUtility.DisplayDialog("Save Mesh", "Nothing to save - the pipeline produced no mesh.", "OK");
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
