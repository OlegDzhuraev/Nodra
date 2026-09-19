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
using UnityEditor;
using UnityEngine;

namespace Nodra
{
	/// <summary> The Graph field itself is edited visually in NodraGraphWindow, not in the Inspector - same reasoning
	/// as ProceduralMeshGeneratorEditor, just without any of its AutoGenerate/bake-related controls, which a bare
	/// GeoGraphAsset (no Mesh, no Transform) has no use for. Double-clicking the asset (NodraGraphWindow.
	/// OnOpenGeoGraphAsset) does the same thing as the "Open Graph Editor" button, for anyone who selects it without
	/// double-clicking. </summary>
	[CustomEditor(typeof(GeoGraphAsset))]
	[CanEditMultipleObjects]
	public class GeoGraphAssetEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			if (GUILayout.Button("Open Graph Editor", GUILayout.Height(24f)))
				NodraGraphWindow.OpenAsset((GeoGraphAsset) target);

			EditorGUILayout.Space();

			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Save Mesh to Project..."))
					foreach (var graphAsset in targets)
						SaveMeshToProject((GeoGraphAsset) graphAsset);

				if (GUILayout.Button("Export to FBX..."))
					foreach (var graphAsset in targets)
						ExportToFbx((GeoGraphAsset) graphAsset);
			}
		}

		// Same idea as ProceduralMeshGeneratorEditor.SaveMeshToProject, but there's no already-baked MeshFilter.
		// sharedMesh to promote into an asset here - a GeoGraphAsset has no Mesh of its own at all, so this builds
		// one from a fresh Graph.Evaluate() purely for this one save, the same way NodraGraphWindow's own preview
		// panel does for its live Mesh.
		static void SaveMeshToProject(GeoGraphAsset graphAsset)
		{
			if (!TryBuildMesh(graphAsset, "Save Mesh", out var mesh))
				return;

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

		// Unlike SaveMeshToProject, this mesh never becomes a persistent asset itself (FbxExportUtility.ExportMesh
		// just reads it to write a separate .fbx file), so it needs destroying afterward instead of being handed
		// off to AssetDatabase.CreateAsset.
		static void ExportToFbx(GeoGraphAsset graphAsset)
		{
			if (!TryBuildMesh(graphAsset, "Export FBX", out var mesh))
				return;

			try
			{
				FbxExportUtility.ExportMesh(mesh, graphAsset.name);
			}
			finally
			{
				DestroyImmediate(mesh);
			}
		}

		// Shared by SaveMeshToProject/ExportToFbx - evaluates the graph and builds a Mesh from it, showing a dialog
		// (and returning false) when that's not possible: BooleanNode's controlled TimeoutException abort, or a
		// graph that doesn't produce anything.
		static bool TryBuildMesh(GeoGraphAsset graphAsset, string dialogTitle, out Mesh mesh)
		{
			GeoData data;
			try
			{
				data = graphAsset.Graph.Evaluate();
			}
			catch (TimeoutException e)
			{
				// GeoCsg (BooleanNode) is the only node that can throw this - see ProceduralMeshGenerator.Generate().
				EditorUtility.DisplayDialog(dialogTitle, e.Message, "OK");
				mesh = null;
				return false;
			}

			mesh = data != null ? GeoMeshBuilder.Build(data, graphAsset.name) : null;

			if (mesh)
				return true;

			EditorUtility.DisplayDialog(dialogTitle, "Nothing to build - the graph produced no mesh.", "OK");
			return false;
		}
	}
}
