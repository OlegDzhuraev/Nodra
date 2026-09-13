![Nodra](Extras/Repo/nodra_logo.png)

# Nodra

A simplified, code-first node network for generating meshes in Unity, in the Editor or at runtime - edited as a
visual node graph.

A `GeoGraph` is a bag of **GeoNode**s (generators, modifiers, scatter/copy, merge) wired together by edges; each
node pulls its input(s) - a shared `GeoData` (points + polygon primitives) - from whatever's connected to its input
port(s), and the result is baked into a `Mesh` at the end. Edit the graph visually in the **Nodra Graph** window.

## Nodes

- **Generators** — `GridGeneratorNode`, `BoxGeneratorNode`, `SphereGeneratorNode` (UV sphere),
  `CylinderGeneratorNode` (also a cone/frustum via `RadiusTop`/`RadiusBottom`), `TorusGeneratorNode`,
  `LineGeneratorNode` (points only, no faces - feed it into `CopyToPointsNode` for fences/columns/stepping stones;
  set it as Output directly and, like `ScatterNode` below, its points/normals draw as Scene view gizmos instead of
  an empty mesh)
- **Modifiers** — `TransformNode`, `NoiseDisplaceNode`, `ExtrudeNode` (extrudes a whole connected surface as one
  shell, walling only its outer boundary)
- **Scatter/copy** — `ScatterNode` + `CopyToPointsNode` (scatter points across a surface, then stamp a mesh at each
  one). `ScatterNode`'s own output is points with no faces - meant to feed `CopyToPointsNode`, not to be the graph's
  output directly, so it bakes into a Mesh with nothing to render; set it as Output anyway (e.g. to check the
  distribution) and the Inspector draws its points/normals as Scene view gizmos instead
- **Combine** — `MergeNode` (two real input ports, "Base" and "Branch" - appends the branch's geometry into the base);
  `BooleanNode` (Union/Subtract/Intersect - a real CSG boolean via a BSP tree, not just concatenation; both inputs
  need to be closed, manifold shapes). Heavy input can make a boolean slow rather than crash - past a minute it
  aborts and logs an error instead of hanging indefinitely

## How to use

Add a `ProceduralMeshGenerator` component (requires a `MeshFilter`) to a GameObject, then click **Open Graph
Editor** in its inspector. Right-click the graph canvas to add nodes - generators (which start a new shape rather
than act on an input) get their own **Create Node/Generator** submenu, everything else lives directly under
**Create Node**. Drag from a node's output to another's input to connect them, edit a node's fields directly on its
box, and copy/paste (Ctrl+C/Ctrl+V) a selection to duplicate nodes along with the connections between them.
Dragging an edge out to empty canvas (from either end) instead pops up that same node menu and wires the new node in
for you - a generator won't show up there if you dragged out of an output, since it'd have nowhere to plug into. The mesh
is rebuilt from whichever node is marked **OUTPUT** (a green outline) - by default whichever node has nothing
connected to its own output (the end of the chain, or of whichever branch you're editing). If several branches
dangle at once, right-click a node and choose **Set As Output** to pin down which one wins instead of relying on
that default.

Turn on **Auto Generate** (in the graph window's toolbar, or the component's inspector) to have the mesh rebuild
automatically on every graph edit. **Save Mesh to Project...** (in the inspector) regenerates and saves the current
result as a `.asset` file, so it survives as a normal project asset instead of only living as an in-memory Mesh on
the MeshFilter.

To add your own node, derive from `GeoNode` and implement `Process(GeoData input)` (or `Process(GeoData[] inputs)`
for more than one input port) - it'll automatically show up in the graph's "Create Node" menu.

```cs
using Nodra;

var generator = gameObject.AddComponent<ProceduralMeshGenerator>();

var grid = new GridGeneratorNode { Size = new Vector2(20, 20), Resolution = new Vector2Int(20, 20) };
var noise = new NoiseDisplaceNode { Amplitude = 2f, Frequency = 0.15f };
var extrude = new ExtrudeNode { Distance = 1.5f };

generator.Graph.Nodes.Add(grid);
generator.Graph.Nodes.Add(noise);
generator.Graph.Nodes.Add(extrude);
generator.Graph.Edges.Add(new GeoEdge { FromNodeId = grid.Id, ToNodeId = noise.Id, ToPortIndex = 0 });
generator.Graph.Edges.Add(new GeoEdge { FromNodeId = noise.Id, ToNodeId = extrude.Id, ToPortIndex = 0 });

generator.Generate();
```

## Structure

```
Sources/
  Nodra.asmdef          runtime assembly - GeoData, GeoNode(s), GeoGraph, GeoMeshBuilder, ProceduralMeshGenerator
  Editor/
    Nodra.Editor.asmdef   editor-only assembly - NodraGraphWindow/View/NodeView, ProceduralMeshGeneratorEditor
```

## License

GPLv3 - see [LICENSE](LICENSE).
