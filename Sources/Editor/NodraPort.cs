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
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Nodra
{
	/// <summary> Same job as Node.InstantiatePort()/Port.Create&lt;Edge&gt;(), but wired to a caller-supplied
	/// IEdgeConnectorListener instead of Port's own hardcoded default one. Port's public factory gives no way to
	/// override that listener - and its constructor is `protected`, reachable only from a subclass - so this class
	/// exists purely to reach it. That listener is what lets dragging an edge out to empty canvas open a
	/// node-creation menu (NodraGraphView.OnDropOutsidePort) instead of silently doing nothing, which is what the
	/// built-in default listener does. </summary>
	class NodraPort : Port
	{
		NodraPort(Orientation orientation, Direction direction, Capacity capacity, Type type) : base(orientation, direction, capacity, type) { }

		public static Port Create(Orientation orientation, Direction direction, Capacity capacity, Type type, IEdgeConnectorListener listener)
		{
			var port = new NodraPort(orientation, direction, capacity, type);
			port.AddManipulator(new EdgeConnector<Edge>(listener));
			return port;
		}
	}
}
