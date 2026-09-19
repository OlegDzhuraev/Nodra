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

using System.IO;
using UnityEditor;
using UnityEngine;

// Nodra.Editor.asmdef's versionDefines only sets NODRA_FBX_EXPORT while com.unity.formats.fbx is actually
// installed - same gating DecimateNode.cs uses for UnityMeshSimplifier. Unlike that one, there's no
// [SerializeReference] type here that has to keep existing under a fixed name for old data to deserialize into -
// this is a plain Editor-only utility, so the whole class just does nothing useful (shows an explanatory dialog)
// instead of a no-op Process().
#if NODRA_FBX_EXPORT
using UnityEditor.Formats.Fbx.Exporter;
#endif

namespace Nodra
{
	/// <summary> Thin wrapper around the optional FBX Exporter package - ProceduralMeshGeneratorEditor/
	/// GeoGraphAssetEditor call this unconditionally and get a save panel + real export when the package is
	/// installed, or a dialog explaining how to install it when it isn't, instead of either a missing-type compile
	/// error or a silently absent button. </summary>
	static class FbxExportUtility
	{
		/// <summary> Exports an existing scene GameObject as-is - its real Transform/MeshRenderer/materials come
		/// along for the ride, the same as exporting it by hand via Unity's own GameObject context menu would. Used
		/// for ProceduralMeshGenerator, which always has one. </summary>
		public static void ExportGameObject(GameObject target)
		{
#if NODRA_FBX_EXPORT
			var path = EditorUtility.SaveFilePanelInProject("Export FBX", target.name, "fbx",
				"Choose where to export the FBX file.");

			if (string.IsNullOrEmpty(path))
				return;

			Finish(ModelExporter.ExportObject(ToAbsolutePath(path), target));
#else
			ShowMissingPackageDialog();
#endif
		}

		/// <summary> Exports a standalone Mesh that has no GameObject of its own - a GeoGraphAsset's own freshly
		/// built preview mesh, say. ModelExporter walks GameObjects, not bare Mesh assets, so this stamps the mesh
		/// onto a throwaway one (HideAndDontSave keeps it out of the Hierarchy/Undo stack for the moment it exists)
		/// and discards it again right after, regardless of whether the export itself succeeded. </summary>
		public static void ExportMesh(Mesh mesh, string name)
		{
#if NODRA_FBX_EXPORT
			var path = EditorUtility.SaveFilePanelInProject("Export FBX", name, "fbx",
				"Choose where to export the FBX file.");

			if (string.IsNullOrEmpty(path))
				return;

			var temp = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

			try
			{
				temp.AddComponent<MeshFilter>().sharedMesh = mesh;
				temp.AddComponent<MeshRenderer>();

				Finish(ModelExporter.ExportObject(ToAbsolutePath(path), temp));
			}
			finally
			{
				Object.DestroyImmediate(temp);
			}
#else
			ShowMissingPackageDialog();
#endif
		}

#if NODRA_FBX_EXPORT
		// SaveFilePanelInProject returns a project-relative "Assets/..." path (the same form AssetDatabase.
		// CreateAsset wants) - but ModelExporter.ExportObject writes straight to disk via the FBX SDK, bypassing
		// AssetDatabase entirely, and per Unity's own FBX Exporter examples wants a real OS path instead (typically
		// built from Application.dataPath). Path.GetFullPath resolves the same way - the Editor process's working
		// directory is always the project root - without manually stitching Application.dataPath back together.
		static string ToAbsolutePath(string projectRelativePath) => Path.GetFullPath(projectRelativePath);

		// ModelExporter.ExportObject returns the file path on success, null on failure - it logs its own reason to
		// the Console either way, so this only needs to surface that something went wrong, not repeat why.
		static void Finish(string exportedPath)
		{
			if (exportedPath == null)
				EditorUtility.DisplayDialog("Export FBX", "Export failed - see the Console for details.", "OK");
			else
				AssetDatabase.Refresh();
		}
#else
		static void ShowMissingPackageDialog() =>
			EditorUtility.DisplayDialog("Export FBX",
				"The FBX Exporter package isn't installed.\n\n" +
				"Window → Package Manager → + → Add package by name... → com.unity.formats.fbx",
				"OK");
#endif
	}
}
