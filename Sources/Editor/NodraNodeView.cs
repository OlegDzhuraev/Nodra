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
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nodra
{
	/// <summary> Visual representation of one GeoNode in the graph: an input port per GeoNode.InputCount slot, one
	/// multi-capacity output port (a node's result can feed several downstream nodes - GeoGraph clones it under the
	/// hood so they don't step on each other), and the node's own fields bound straight to its SerializedProperty
	/// so editing here goes through the same serialization/Undo path as any other Inspector field. </summary>
	public class NodraNodeView : Node
	{
		static readonly Color OutputHighlightColor = new (0.3f, 0.85f, 0.4f);

		public readonly GeoNode Node;
		public readonly Port[] InputPorts;

		/// <summary> Null for a node whose GeoNode.HasOutput is false (GeometryOutputNode) - it's the graph's
		/// terminal node, so it has nothing to connect downstream. </summary>
		public readonly Port OutputPort;

		readonly NodraGraphView owner;
		readonly Label outputBadge;
		readonly VisualElement outputOutline;

		public NodraNodeView(GeoNode node, SerializedProperty nodeProperty, NodraGraphView owner)
		{
			Node = node;
			this.owner = owner;
			viewDataKey = node.Id;
			title = GetDisplayName(node.GetType());

			// Lets NodraGraphView.uss give Generator nodes' header their own background instead of Node's default,
			// without hardcoding a color here - see the ".nodra-node-generator #title" rule there.
			if (node.Category == "Generators")
				AddToClassList("nodra-node-generator");

			// Node's default USS gives the card a semi-transparent background - fine over a plain grid, but
			// distracting once nodes overlap/stack, so it's forced opaque here. maxWidth stops a long
			// GeoNode.Warning string (wraps, see BuildWarning) from stretching the card sideways instead of down.
			style.minWidth = 260f;
			style.maxWidth = 320f;
			var opaqueBackground = new Color(0.13f, 0.13f, 0.13f, 1f);
			mainContainer.style.backgroundColor = opaqueBackground;
			extensionContainer.style.backgroundColor = opaqueBackground;

			extensionContainer.AddToClassList("nodra-node-fields");

			var enabledToggle = new Toggle { tooltip = "Enabled" };
			enabledToggle.BindProperty(nodeProperty.FindPropertyRelative("Enabled"));
			titleButtonContainer.Add(enabledToggle);

			outputBadge = new Label("OUTPUT")
			{
				tooltip = "This node's result becomes the final mesh.",
				style =
				{
					display = DisplayStyle.None,
					unityFontStyleAndWeight = FontStyle.Bold,
					fontSize = 9,
					color = OutputHighlightColor,
					marginLeft = 4f,
					marginRight = 4f,
					unityTextAlign = TextAnchor.MiddleCenter,
				},
			};
			titleButtonContainer.Add(outputBadge);

			var inputCount = Mathf.Max(node.InputCount, 0);
			InputPorts = new Port[inputCount];

			// NodraPort.Create (not the inherited InstantiatePort) - only it lets a drag that ends on empty canvas
			// reach NodraGraphView.OnDropOutsidePort instead of silently doing nothing.
			for (var i = 0; i < inputCount; i++)
			{
				var port = NodraPort.Create(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(GeoData), owner);
				port.portName = node.GetInputPortName(i);
				InputPorts[i] = port;
				inputContainer.Add(port);
			}

			if (node.HasOutput)
			{
				OutputPort = NodraPort.Create(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(GeoData), owner);
				OutputPort.portName = "Out";
				outputContainer.Add(OutputPort);
			}

			BuildWarning(node);
			BuildFieldEditors(nodeProperty);
			BuildEditableAssetButton(node, nodeProperty);

			SetPosition(new Rect(node.Position, Vector2.zero));
			RefreshExpandedState();
			RefreshPorts();

			outputOutline = new VisualElement { pickingMode = PickingMode.Ignore, style = { display = DisplayStyle.None } };
			outputOutline.AddToClassList("nodra-node-output-outline");
			hierarchy.Add(outputOutline);
		}

		/// <summary> Toggles the green border/"OUTPUT" badge - called by NodraGraphView whenever the graph's
		/// resolved output node might have changed (populate, connect/disconnect, remove, add/remove a
		/// GeometryOutputNode). </summary>
		public void SetIsOutput(bool isOutput)
		{
			var display = isOutput ? DisplayStyle.Flex : DisplayStyle.None;
			outputBadge.style.display = display;
			outputOutline.style.display = display;
		}

		// GeoNode.Warning is null for the overwhelming majority of nodes - only ones like DecimateNode, whose
		// optional dependency might be missing, override it, so this stays a no-op for everything else.
		void BuildWarning(GeoNode node)
		{
			if (string.IsNullOrEmpty(node.Warning))
				return;

			// Wraps instead of forcing the node wider to fit one long line - maxWidth above caps it, but without
			// this the HelpBox's own label would rather overflow than respect that.
			var warning = new HelpBox(node.Warning, HelpBoxMessageType.Warning) { style = { whiteSpace = WhiteSpace.Normal } };
			extensionContainer.Add(warning);
		}

		// SubGraphNode-style nodes (GeoNode.EditableAssetFieldName) get a button that jumps straight into editing
		// whatever UnityEngine.Object asset that field currently holds, instead of having to hunt it down in the
		// Project window - tracks the same SerializedProperty its own PropertyField (built just above, in
		// BuildFieldEditors) is bound to, so it live-enables the moment something's actually assigned rather than
		// needing this node view rebuilt first.
		void BuildEditableAssetButton(GeoNode node, SerializedProperty nodeProperty)
		{
			var fieldName = node.EditableAssetFieldName;
			if (string.IsNullOrEmpty(fieldName))
				return;

			var property = nodeProperty.FindPropertyRelative(fieldName);
			if (property == null)
				return;

			var button = new Button { text = "Open" };
			button.clicked += () =>
			{
				if (property.objectReferenceValue is GeoGraphAsset graphAsset)
					NodraGraphWindow.OpenAsset(graphAsset);
			};

			void Refresh() => button.SetEnabled(property.objectReferenceValue != null);

			Refresh();
			button.TrackPropertyValue(property, _ => Refresh());
			extensionContainer.Add(button);
		}

		void BuildFieldEditors(SerializedProperty nodeProperty)
		{
			var child = nodeProperty.Copy();
			var end = child.GetEndProperty();
			var enteredChildren = false;

			while (child.NextVisible(!enteredChildren) && !SerializedProperty.EqualContents(child, end))
			{
				enteredChildren = true;

				// Enabled has its own toggle in the title bar; Id/Position are graph bookkeeping, not user data.
				if (child.name is "Enabled" or "Id" or "Position")
					continue;

				var field = CreateVectorField(child) ?? CreateDefaultField(child);
				extensionContainer.Add(field);

				BindShowIf(field, nodeProperty, child.name);
			}
		}

		static VisualElement CreateDefaultField(SerializedProperty property)
		{
			var field = new PropertyField(property.Copy());
			field.BindProperty(property.Copy());
			return field;
		}

		// PropertyField's default Vector2/Vector3(Int) control packs X/Y/Z into one row that never stretches to
		// the node's width - stacking each axis as its own full-width field sidesteps that. Returns null (falling
		// back to CreateDefaultField) for a field with a PropertyAttribute, so a CustomPropertyDrawer on it (e.g.
		// MinVector2IntAttribute's clamp) still runs instead of being silently skipped.
		VisualElement CreateVectorField(SerializedProperty property)
		{
			string[] axisNames = property.propertyType switch
			{
				SerializedPropertyType.Vector2 or SerializedPropertyType.Vector2Int => new[] { "x", "y" },
				SerializedPropertyType.Vector3 or SerializedPropertyType.Vector3Int => new[] { "x", "y", "z" },
				_ => null,
			};

			if (axisNames == null)
				return null;

			var hasPropertyAttribute = Node.GetType()
				.GetField(property.name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				?.GetCustomAttributes<PropertyAttribute>()
				.Any() ?? false;

			if (hasPropertyAttribute)
				return null;

			var isInt = property.propertyType is SerializedPropertyType.Vector2Int or SerializedPropertyType.Vector3Int;

			var container = new VisualElement();
			container.AddToClassList("nodra-node-vector-field");

			var header = new Label(property.displayName);
			header.AddToClassList("unity-base-field__label");
			container.Add(header);

			foreach (var axisName in axisNames)
			{
				var axisProperty = property.FindPropertyRelative(axisName);
				var axisLabel = axisName.ToUpperInvariant();

				if (isInt)
				{
					var axisField = new IntegerField(axisLabel);
					axisField.BindProperty(axisProperty);
					container.Add(axisField);
				}
				else
				{
					var axisField = new FloatField(axisLabel);
					axisField.BindProperty(axisProperty);
					container.Add(axisField);
				}
			}

			return container;
		}

		// VertexColorNode's Gradient/Bounds/Axis only make sense for some Mode/Source combos - rather than every
		// field always showing regardless of what the node actually uses it for, [ShowIf] on the field declares
		// which sibling value(s) it needs, and this hides/shows it live as those siblings change. Generic over any
		// GeoNode field, not just VertexColorNode's.
		void BindShowIf(VisualElement field, SerializedProperty nodeProperty, string fieldName)
		{
			var conditions = Node.GetType()
				.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				?.GetCustomAttributes<ShowIfAttribute>()
				.Select(showIf => (property: nodeProperty.FindPropertyRelative(showIf.Field), showIf.Values))
				.Where(condition => condition.property != null)
				.ToArray();

			if (conditions == null || conditions.Length == 0)
				return;

			void Refresh() => field.style.display =
				conditions.All(c => c.Values.Any(v => c.property.intValue == Convert.ToInt32(v)))
					? DisplayStyle.Flex
					: DisplayStyle.None;

			foreach (var condition in conditions)
				field.TrackPropertyValue(condition.property, _ => Refresh());

			Refresh();
		}

		/// <summary> "GridGeneratorNode" -> "Grid Generator" - shared with NodraGraphView's "Create Node" menu so
		/// both places label a node type the same way. </summary>
		public static string GetDisplayName(System.Type type) =>
			ObjectNames.NicifyVariableName(type.Name.EndsWith("Node") ? type.Name[..^4] : type.Name);
	}
}
