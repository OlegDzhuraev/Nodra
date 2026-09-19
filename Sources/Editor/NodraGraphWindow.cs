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
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nodra
{
	/// <summary> Visual editor for one GeoGraph - either a ProceduralMeshGenerator component's own (a toolbar with
	/// target label, Auto Generate, Generate above a NodraGraphView), or a standalone GeoGraphAsset's (same canvas,
	/// no bake-related toolbar/Scene gizmos - there's no baked Mesh or Transform to show them against). Opened from
	/// ProceduralMeshGeneratorEditor's/GeoGraphAssetEditor's "Open Graph Editor" button, or by double-clicking a
	/// GeoGraphAsset (OnOpenGeoGraphAsset); only one owner is bound at a time, re-bound whenever a different one's
	/// button/asset is opened. </summary>
	public class NodraGraphWindow : EditorWindow
	{
		static readonly Color SceneViewBackgroundColor = new (0.16f, 0.16f, 0.16f);
		static readonly Color BoundsFillColor = new (1f, 0.6f, 0.15f, 0.12f);
		static readonly Color BoundsOutlineColor = new (1f, 0.6f, 0.15f, 0.9f);
		static readonly Color PointColorLow = Color.red;
		static readonly Color PointColorHigh = Color.green;

		[SerializeField] ProceduralMeshGenerator target;
		[SerializeField] GeoGraphAsset asset;

		// Point-color gizmo panel state - persisted on the window itself (like target above) so it survives a
		// domain reload/window re-layout instead of resetting every time the graph is reopened.
		[SerializeField] bool pointColorPanelExpanded = true;
		[SerializeField] bool pointColorEnabled;
		[SerializeField] string pointColorAttribute = "";
		[SerializeField] Vector2 pointColorRemap = new (0f, 1f);

		[SerializeField] bool previewPanelExpanded = true;

		NodraGraphView graphView;
		Label targetLabel;
		Toggle autoGenerateToggle;
		ToolbarButton generateButton;

		// Live mesh preview (like Shader Graph's own) - MeshPreview is the same UnityEditor utility class the Mesh
		// asset Inspector's own preview uses: handles the orbit camera (drag), zoom and lighting internally, so this
		// only has to keep its .mesh up to date. Null until the graph has ever produced a mesh - MeshPreview's own
		// constructor throws (NullReferenceException in CheckAvailableAttributes) if given a null target, so it's
		// created lazily in RefreshPreviewMesh the first time there's an actual Mesh, not eagerly in OnEnable.
		// previewMesh/previewHash track what's currently shown so RefreshPreviewMesh (called every Update() tick)
		// can cheaply no-op most of the time - see its own comment.
		MeshPreview meshPreview;
		Mesh previewMesh;
		string previewHash;
		GUIStyle previewBackgroundStyle;

		readonly Dictionary<SceneView, (CameraClearFlags clearFlags, Color backgroundColor, bool showSkybox)> sceneViewState = new ();

		// Compared against graphView.selection every Update() tick (see below) purely to know when to force a
		// Scene view repaint - selecting a node happens inside this window, not the Scene view, so nothing would
		// otherwise tell a Scene view to redraw and pick up the newly (de)selected node's gizmo until something
		// else happened to repaint it anyway (e.g. the mouse crossing over it).
		readonly HashSet<string> lastSelectedNodeIds = new ();

		public static void Open(ProceduralMeshGenerator generator)
		{
			var window = GetWindow<NodraGraphWindow>();
			window.titleContent = new GUIContent("Nodra Graph");
			window.minSize = new Vector2(400f, 250f);
			window.BindGenerator(generator);
		}

		public static void OpenAsset(GeoGraphAsset graphAsset)
		{
			var window = GetWindow<NodraGraphWindow>();
			window.titleContent = new GUIContent("Nodra Graph");
			window.minSize = new Vector2(400f, 250f);
			window.BindAsset(graphAsset);
		}

		// Lets double-clicking a GeoGraphAsset in the Project window open it here directly, the same way double-
		// clicking a Shader Graph/Timeline asset opens its own editor - on top of, not instead of, the "Open Graph
		// Editor" Inspector button (GeoGraphAssetEditor) for anyone who selects it without double-clicking.
		[UnityEditor.Callbacks.OnOpenAsset(1)]
		static bool OnOpenGeoGraphAsset(EntityId entityId, int line)
		{
			if (EditorUtility.EntityIdToObject(entityId) is not GeoGraphAsset graphAsset)
				return false;

			OpenAsset(graphAsset);
			return true;
		}

		void OnEnable()
		{
			Undo.undoRedoPerformed += OnUndoRedoPerformed;

			// The graph is the thing being edited while this window is open - hide the Scene view's built-in
			// Move/Rotate/Scale gizmo so it can't be dragged by accident.
			Tools.hidden = true;
			SceneView.duringSceneGui += ApplySceneViewBackground;
			SceneView.duringSceneGui += DrawSelectedBoundsGizmos;
			SceneView.duringSceneGui += DrawPointColorGizmos;

			BuildLayout();

			if (target != null)
				BindGenerator(target);
			else if (asset != null)
				BindAsset(asset);
		}

		void OnDisable()
		{
			Undo.undoRedoPerformed -= OnUndoRedoPerformed;
			Tools.hidden = false;

			SceneView.duringSceneGui -= ApplySceneViewBackground;
			SceneView.duringSceneGui -= DrawSelectedBoundsGizmos;
			SceneView.duringSceneGui -= DrawPointColorGizmos;
			RestoreSceneViewBackgrounds();

			meshPreview?.Dispose();
			meshPreview = null;

			if (previewMesh != null)
				DestroyImmediate(previewMesh);
			previewMesh = null;
			previewHash = null;
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

		// A translucent box for whichever selected node's own Bounds field is the one its Mode/Source combination
		// actually reads (VertexColorNode's Gradient+Bounds, SetAttributeNode's Bounds) - shown only while such a
		// node is selected right here in the graph, not permanently, so it doesn't linger over the Scene view once
		// you've moved on to editing something else.
		void DrawSelectedBoundsGizmos(SceneView sceneView)
		{
			if (target == null || graphView == null)
				return;

			foreach (var nodeView in graphView.selection.OfType<NodraNodeView>())
				if (TryGetActiveBounds(target.Graph, nodeView.Node, out var bounds))
					DrawBoundsBox(target.transform, bounds);
		}

		// Add a case here for any future node that gets its own Bounds field - nothing discovers these
		// automatically (Bounds is just a plain UnityEngine type, not a marker interface), so a new one goes
		// silently un-visualized until it's added below. Port index 1 must match GetInputPortName's "Bounds" slot
		// on that node - see its own ResolveBounds for the same field-vs-port precedence this mirrors.
		static bool TryGetActiveBounds(GeoGraph graph, GeoNode node, out Bounds bounds)
		{
			Bounds fieldBounds;

			switch (node)
			{
				case VertexColorNode vertexColor when vertexColor.Mode == VertexColorNode.ColorMode.Gradient
					&& vertexColor.Source == VertexColorNode.GradientSource.Bounds:
					fieldBounds = vertexColor.Bounds;
					break;

				case SetAttributeNode setAttribute when setAttribute.Mode == SetAttributeNode.ValueMode.Bounds:
					fieldBounds = setAttribute.Bounds;
					break;

				default:
					bounds = default;
					return false;
			}

			bounds = TryResolveBoundsPort(graph, node.Id) ?? fieldBounds;
			return true;
		}

		// Mirrors SetAttributeNode/VertexColorNode's own ResolveBounds, but reading from GeoGraph.TryGetCachedResult
		// instead of a live Process() input - the gizmo runs outside any Evaluate() call, so the best it can do is
		// show whatever the last actual bake computed for whatever feeds this node's Bounds port. Null (falls back
		// to the field) if nothing's wired in, or that source hasn't been evaluated yet.
		static Bounds? TryResolveBoundsPort(GeoGraph graph, string nodeId)
		{
			if (graph == null)
				return null;

			foreach (var edge in graph.Edges)
			{
				if (edge.ToNodeId != nodeId || edge.ToPortIndex != 1)
					continue;

				var source = graph.TryGetCachedResult(edge.FromNodeId);
				return source is { PointCount: > 0 } ? source.GetBounds() : null;
			}

			return null;
		}

		// Bounds is in the generator's own local space (VertexColorNode/SetAttributeNode both compare it directly
		// against GeoData.Points, which are local) - transform.TransformPoint carries each corner into world space
		// the same way DrawPointCloud/DrawMeshNormals already do in ProceduralMeshGeneratorEditor.
		static void DrawBoundsBox(Transform transform, Bounds bounds)
		{
			Vector3 Corner(int xi, int yi, int zi) => transform.TransformPoint(new Vector3(
				xi == 0 ? bounds.min.x : bounds.max.x,
				yi == 0 ? bounds.min.y : bounds.max.y,
				zi == 0 ? bounds.min.z : bounds.max.z));

			var c000 = Corner(0, 0, 0);
			var c100 = Corner(1, 0, 0);
			var c010 = Corner(0, 1, 0);
			var c110 = Corner(1, 1, 0);
			var c001 = Corner(0, 0, 1);
			var c101 = Corner(1, 0, 1);
			var c011 = Corner(0, 1, 1);
			var c111 = Corner(1, 1, 1);

			DrawFace(c000, c100, c110, c010); // -Z
			DrawFace(c001, c101, c111, c011); // +Z
			DrawFace(c000, c100, c101, c001); // -Y
			DrawFace(c010, c110, c111, c011); // +Y
			DrawFace(c000, c010, c011, c001); // -X
			DrawFace(c100, c110, c111, c101); // +X
		}

		static void DrawFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
			Handles.DrawSolidRectangleWithOutline(new[] { a, b, c, d }, BoundsFillColor, BoundsOutlineColor);

		// Every point of the target's last evaluated GeoData, colored by one named attribute read via
		// GeoData.GetAttribute - pointColorRemap.x/.y map that value's range onto the red->green gradient (clamped,
		// via InverseLerp) so an attribute whose actual range doesn't happen to sit in [0, 1] (a raw height, say)
		// can still be visualized meaningfully. Independent of DrawPointCloud in ProceduralMeshGeneratorEditor,
		// which only draws when the result has no faces - this is meant to work on any generated result.
		void DrawPointColorGizmos(SceneView sceneView)
		{
			if (!pointColorEnabled || target == null || string.IsNullOrEmpty(pointColorAttribute))
				return;

			var data = target.LastEvaluatedData;
			if (data == null || data.PointCount == 0)
				return;

			var previousColor = Handles.color;

			for (var i = 0; i < data.PointCount; i++)
			{
				var value = data.GetAttribute(pointColorAttribute, i);
				var t = Mathf.InverseLerp(pointColorRemap.x, pointColorRemap.y, value);

				var worldPosition = target.transform.TransformPoint(data.Points[i]);
				var size = HandleUtility.GetHandleSize(worldPosition) * 0.05f;

				Handles.color = Color.Lerp(PointColorLow, PointColorHigh, t);
				Handles.SphereHandleCap(0, worldPosition, Quaternion.identity, size, EventType.Repaint);
			}

			Handles.color = previousColor;
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
				{
					target.Generate();
					RefreshPreviewMesh(force: true);
				}
			});
			toolbar.Add(autoGenerateToggle);

			generateButton = new ToolbarButton(() =>
			{
				if (target == null)
					return;

				target.Generate();
				RefreshPreviewMesh(force: true);

				// Jumps the Scene view to the generated result - separate from (and in addition to) this window's
				// own floating preview panel, since that one's decoupled from any scene Transform/lighting and is
				// meant for a quick look at shape, not a final in-context check.
				Selection.activeGameObject = target.gameObject;
				SceneView.lastActiveSceneView?.FrameSelected();
			}) { text = "Generate" };
			toolbar.Add(generateButton);

			rootVisualElement.Add(toolbar);

			graphView = new NodraGraphView { style = { flexGrow = 1 } };
			rootVisualElement.Add(graphView);

			BuildPointColorPanel();
			BuildPreviewPanel();
		}

		// A floating live preview of the graph's current output - MeshPreview (see the meshPreview field) handles
		// the actual rendering/orbit-drag/zoom; this just hosts it in an IMGUIContainer (the only way to embed
		// legacy OnGUI-style drawing - which is what MeshPreview.OnPreviewGUI is - inside a UI Toolkit layout) and
		// keeps its mesh current via RefreshPreviewMesh. Docked bottom-right so it doesn't collide with the Point
		// Colors panel's own top-right spot.
		void BuildPreviewPanel()
		{
			var panel = new Foldout
			{
				text = "Preview",
				value = previewPanelExpanded,
				style =
				{
					position = Position.Absolute,
					bottom = 8f,
					right = 8f,
					width = 220f,
					paddingLeft = 8f,
					paddingRight = 8f,
					paddingTop = 6f,
					paddingBottom = 6f,
					backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1f),
					borderTopLeftRadius = 4f,
					borderTopRightRadius = 4f,
					borderBottomLeftRadius = 4f,
					borderBottomRightRadius = 4f,
				},
			};
			panel.AddToClassList("nodra-node-fields");

			panel.RegisterValueChangedCallback(evt =>
			{
				if (evt.target == panel)
					previewPanelExpanded = evt.newValue;
			});

			// IMGUIContainer needs an explicit height - unlike the plain UI Toolkit fields elsewhere in this window,
			// IMGUI content underneath it doesn't report a size UI Toolkit's own flex layout can measure. 24px for
			// MeshPreview.OnPreviewSettings' toolbar row plus a 200px square for OnPreviewGUI itself.
			var previewGui = new IMGUIContainer(DrawPreviewGUI) { style = { height = 224f } };
			panel.Add(previewGui);

			graphView.Add(panel);
		}

		// GUIStyle construction has to happen inside an actual OnGUI call (the skin it pulls "preBackground" from
		// isn't ready any earlier) - built once, lazily, on the first call here rather than every single one.
		void DrawPreviewGUI()
		{
			using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
				meshPreview?.OnPreviewSettings();

			var rect = GUILayoutUtility.GetRect(200f, 200f, GUILayout.ExpandWidth(true));

			if (meshPreview == null || meshPreview.mesh == null)
			{
				GUI.Label(rect, "No preview", EditorStyles.centeredGreyMiniLabel);
				return;
			}

			previewBackgroundStyle ??= new GUIStyle("preBackground");
			meshPreview.OnPreviewGUI(rect, previewBackgroundStyle);
		}

		// Rebuilds the preview Mesh whenever the graph's own cheap output hash has moved - checked every Update()
		// tick rather than wired to specific edit callbacks, since a live preview needs to react to every kind of
		// edit a field slider drag included, which (unlike a structural add/remove/connect) never reaches
		// NodraGraphView.OnGraphViewChanged at all. ComputeOutputHash() is the same no-Process()/no-Clone() cheap
		// walk ProceduralMeshGenerator.Generate() already leans on for exactly this reason - safe to call
		// unconditionally every tick; the real cost (Evaluate() + GeoMeshBuilder.Build()) only pays off on an
		// actual change. A ProceduralMeshGenerator with AutoGenerate off freezes the preview at whatever it last
		// showed instead of chasing every edit - the preview should track what's actually baked, not run ahead of
		// it - so the periodic Update() call below only refreshes while AutoGenerate is on; the AutoGenerate
		// toggle/Generate button call this with force: true right after target.Generate() so the preview still
		// snaps to the new result immediately instead of waiting for AutoGenerate to be flipped back on. A bare
		// GeoGraphAsset has no AutoGenerate/Generate of its own, so its preview always stays live either way.
		void RefreshPreviewMesh(bool force = false)
		{
			var graph = target != null ? target.Graph : asset != null ? asset.Graph : null;

			if (graph == null)
			{
				previewHash = null;

				if (previewMesh != null)
				{
					DestroyImmediate(previewMesh);
					previewMesh = null;
				}

				meshPreview?.Dispose();
				meshPreview = null;

				return;
			}

			if (!force && target != null && !target.AutoGenerate)
				return;

			var hash = graph.ComputeOutputHash();
			if (hash == previewHash)
				return;

			previewHash = hash;

			GeoData data;
			try
			{
				data = graph.Evaluate();
			}
			catch (TimeoutException)
			{
				return; // GeoCsg/BooleanNode's controlled abort - leave whatever the preview was already showing
			}

			if (previewMesh != null)
				DestroyImmediate(previewMesh);

			previewMesh = data != null ? GeoMeshBuilder.Build(data, "Preview") : null;

			// MeshPreview's own constructor throws given a null target (NullReferenceException inside its
			// CheckAvailableAttributes) - so it's only ever constructed here, once there's an actual Mesh to hand
			// it, never eagerly with null (see OnEnable/the graph == null branch above, which Dispose() instead).
			// Once it already exists, reassigning its .mesh property is a plain field set with no such check.
			if (previewMesh == null)
				meshPreview?.Dispose();
			else if (meshPreview == null)
				meshPreview = new MeshPreview(previewMesh);
			else
				meshPreview.mesh = previewMesh;

			if (previewMesh == null)
				meshPreview = null;

			Repaint();
		}

		// A small floating panel docked over the graph canvas, the same way GraphView-based editors typically dock
		// a Blackboard - added as a child of graphView itself (absolutely positioned) rather than of rootVisualElement,
		// so it overlays the canvas instead of taking up toolbar/layout space.
		//
		// Tagged with the same "nodra-node-fields" class NodraNodeView's own field container uses, so it picks up
		// NodraGraphView.uss's existing rule capping .unity-base-field__label width at 90px - without that, a plain
		// Toggle/TextField's default (much wider) label leaves the input box no room in a panel this narrow.
		void BuildPointColorPanel()
		{
			var panel = new Foldout
			{
				text = "Point Colors",
				value = pointColorPanelExpanded,
				style =
				{
					position = Position.Absolute,
					top = 8f,
					right = 8f,
					width = 240f,
					paddingLeft = 8f,
					paddingRight = 8f,
					paddingTop = 6f,
					paddingBottom = 6f,
					backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1f),
					borderTopLeftRadius = 4f,
					borderTopRightRadius = 4f,
					borderBottomLeftRadius = 4f,
					borderBottomRightRadius = 4f,
				},
			};
			panel.AddToClassList("nodra-node-fields");

			// Toggle/TextField/FloatField below all raise the same ChangeEvent<bool>/<string>/<float> types, which
			// bubble up through their ancestors - evt.target filters this down to the Foldout's own header actually
			// being toggled, not one of those bubbling through it.
			panel.RegisterValueChangedCallback(evt =>
			{
				if (evt.target == panel)
					pointColorPanelExpanded = evt.newValue;
			});

			var enabledToggle = new Toggle("Enabled") { value = pointColorEnabled };
			enabledToggle.RegisterValueChangedCallback(evt =>
			{
				pointColorEnabled = evt.newValue;
				SceneView.RepaintAll();
			});
			panel.Add(enabledToggle);

			var attributeField = new TextField("Attribute") { value = pointColorAttribute };
			attributeField.RegisterValueChangedCallback(evt =>
			{
				pointColorAttribute = evt.newValue;
				SceneView.RepaintAll();
			});
			panel.Add(attributeField);

			panel.Add(CreateRemapField());

			graphView.Add(panel);
		}

		// Stacks Remap's X/Y as their own full-width rows instead of Unity's default Vector2Field, which packs both
		// into one row that has no room to spare once squeezed into this panel's width - the same problem, and the
		// same fix (one axis per row), NodraNodeView.CreateVectorField already applies to node fields. Reuses that
		// method's matching "nodra-node-vector-field" NodraGraphView.uss rules (full-width header label, indented
		// axis rows) rather than duplicating them under a new name.
		VisualElement CreateRemapField()
		{
			var container = new VisualElement();
			container.AddToClassList("nodra-node-vector-field");

			var header = new Label("Remap");
			header.AddToClassList("unity-base-field__label");
			container.Add(header);

			var xField = new FloatField("X") { value = pointColorRemap.x };
			xField.RegisterValueChangedCallback(evt =>
			{
				pointColorRemap.x = evt.newValue;
				SceneView.RepaintAll();
			});
			container.Add(xField);

			var yField = new FloatField("Y") { value = pointColorRemap.y };
			yField.RegisterValueChangedCallback(evt =>
			{
				pointColorRemap.y = evt.newValue;
				SceneView.RepaintAll();
			});
			container.Add(yField);

			return container;
		}

		void BindGenerator(ProceduralMeshGenerator generator)
		{
			target = generator;
			asset = null;

			if (graphView == null)
				BuildLayout();

			targetLabel.text = target != null ? target.name : "(no target)";
			autoGenerateToggle.SetValueWithoutNotify(target != null && target.AutoGenerate);
			SetGenerateControlsVisible(true);

			graphView.Bind(target, target != null ? target.Graph : null, OnGraphChanged);
		}

		// A GeoGraphAsset has no baked Mesh and no Transform to preview against - AutoGenerate/Generate and the
		// Scene-view gizmos (DrawSelectedBoundsGizmos/DrawPointColorGizmos, both already guarded on target == null)
		// stay meaningless here, so the toolbar just loses those controls instead of pretending they apply.
		void BindAsset(GeoGraphAsset graphAsset)
		{
			target = null;
			asset = graphAsset;

			if (graphView == null)
				BuildLayout();

			targetLabel.text = asset != null ? asset.name : "(no target)";
			SetGenerateControlsVisible(false);

			graphView.Bind(asset, asset != null ? asset.Graph : null, OnGraphChanged);
		}

		void SetGenerateControlsVisible(bool visible)
		{
			var display = visible ? DisplayStyle.Flex : DisplayStyle.None;
			autoGenerateToggle.style.display = display;
			generateButton.style.display = display;
		}

		void OnUndoRedoPerformed()
		{
			if (target == null && asset == null)
				return;

			graphView.Populate();
			if (target != null)
				autoGenerateToggle.SetValueWithoutNotify(target.AutoGenerate);
		}

		void OnGraphChanged()
		{
			if (target != null && target.AutoGenerate)
				target.Generate();
		}

		void Update()
		{
			// The bound owner can be destroyed (deleted GameObject/scene closed, or asset deleted from disk)
			// without this window knowing. Re-binding to null clears the label *and* unbinds/empties the graph
			// view - leaving the stale NodraNodeViews in place instead would keep their PropertyFields bound to the
			// now-disposed SerializedObject, which Unity throws trying to resync every single tick.
			if (target == null && asset == null && targetLabel != null && targetLabel.text != "(no target)")
				BindGenerator(null);

			RefreshPreviewMesh();

			// Selecting a node happens in THIS window, not the Scene view, so nothing else would tell an open
			// Scene view to redraw and pick up a newly (de)selected node's bounds gizmo - forced here, but only on
			// an actual change, so this isn't repainting the Scene view every single tick for no reason.
			if (graphView != null && SelectedNodeIdsChanged())
				SceneView.RepaintAll();
		}

		bool SelectedNodeIdsChanged()
		{
			var current = graphView.selection.OfType<NodraNodeView>().Select(view => view.Node.Id).ToList();

			if (current.Count == lastSelectedNodeIds.Count && current.All(lastSelectedNodeIds.Contains))
				return false;

			lastSelectedNodeIds.Clear();
			foreach (var id in current)
				lastSelectedNodeIds.Add(id);

			return true;
		}
	}
}
