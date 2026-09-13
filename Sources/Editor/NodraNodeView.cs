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

			// Node's default USS gives the card a semi-transparent background - fine over a plain grid, but
			// distracting once nodes overlap/stack, so it's forced opaque here. Widened past Node's fairly narrow
			// default too, since a Vector2/Vector3 field's sub-fields (Offset's X/Y, ...) get squeezed unreadably
			// thin otherwise - see also the label-width rule in NodraGraphView.uss. Capped on the other end so a
			// long GeoNode.Warning string (which wraps, see BuildWarning) can't stretch the card out sideways
			// instead of just growing taller.
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

			OutputPort = NodraPort.Create(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(GeoData), owner);
			OutputPort.portName = "Out";
			outputContainer.Add(OutputPort);

			BuildWarning(node);
			BuildFieldEditors(nodeProperty);

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

				var field = new PropertyField(child.Copy());
				field.BindProperty(child.Copy());
				extensionContainer.Add(field);
			}
		}

		/// <summary> "GridGeneratorNode" -> "Grid Generator" - shared with NodraGraphView's "Create Node" menu so
		/// both places label a node type the same way. </summary>
		public static string GetDisplayName(System.Type type) =>
			ObjectNames.NicifyVariableName(type.Name.EndsWith("Node") ? type.Name[..^4] : type.Name);
	}
}
