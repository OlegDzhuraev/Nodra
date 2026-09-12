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

		public NodraNodeView(GeoNode node, SerializedProperty nodeProperty, NodraGraphView owner)
		{
			Node = node;
			this.owner = owner;
			viewDataKey = node.Id;
			title = GetDisplayName(node.GetType());

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

			for (var i = 0; i < inputCount; i++)
			{
				var port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(GeoData));
				port.portName = node.GetInputPortName(i);
				InputPorts[i] = port;
				inputContainer.Add(port);
			}

			OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(GeoData));
			OutputPort.portName = "Out";
			outputContainer.Add(OutputPort);

			BuildFieldEditors(nodeProperty);

			SetPosition(new Rect(node.Position, Vector2.zero));
			RefreshExpandedState();
			RefreshPorts();
		}

		/// <summary> Toggles the green border/"OUTPUT" badge - called by NodraGraphView whenever the graph's
		/// resolved output node might have changed (populate, connect/disconnect, remove, explicit Set As Output). </summary>
		public void SetIsOutput(bool isOutput)
		{
			outputBadge.style.display = isOutput ? DisplayStyle.Flex : DisplayStyle.None;

			var width = isOutput ? new StyleFloat(2f) : new StyleFloat(StyleKeyword.Null);
			var color = isOutput ? new StyleColor(OutputHighlightColor) : new StyleColor(StyleKeyword.Null);

			style.borderTopWidth = style.borderRightWidth = style.borderBottomWidth = style.borderLeftWidth = width;
			style.borderTopColor = style.borderRightColor = style.borderBottomColor = style.borderLeftColor = color;
		}

		public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
		{
			base.BuildContextualMenu(evt);

			evt.menu.AppendSeparator();

			if (owner.IsExplicitOutput(Node))
				evt.menu.AppendAction("Clear Output (Auto)", _ => owner.ClearOutputNode());
			else
				evt.menu.AppendAction("Set As Output", _ => owner.SetOutputNode(Node));
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
