# Nodra

A simplified, code-first node network for generating meshes in Unity, in the Editor or at runtime - edited as a
visual node graph.

A `GeoGraph` is a bag of **GeoNode**s (generators, modifiers, scatter/copy, merge) wired together by edges; each
node pulls its input(s) - a shared `GeoData` (points + polygon primitives) - from whatever's connected to its input
port(s), and the result is baked into a `Mesh` at the end. Edit the graph visually in the **Nodra Graph** window.

## Nodes

- **Generators** — `GridGeneratorNode`, `BoxGeneratorNode`
- **Modifiers** — `TransformNode`, `NoiseDisplaceNode`, `ExtrudeNode` (extrudes a whole connected surface as one
  shell, walling only its outer boundary)
- **Scatter/copy** — `ScatterNode` + `CopyToPointsNode` (scatter points across a surface, then stamp a mesh at each
  one)
- **Combine** — `MergeNode` (two real input ports, "Base" and "Branch" - appends the branch's geometry into the base)

## How to use

Add a `ProceduralMeshGenerator` component (requires a `MeshFilter`) to a GameObject, then click **Open Graph
Editor** in its inspector. Right-click the graph canvas to add nodes, drag from a node's output to another's input
to connect them, and edit a node's fields directly on its box. The mesh is rebuilt from whichever node is marked
**OUTPUT** (a green outline) - by default whichever node has nothing connected to its own output (the end of the
chain, or of whichever branch you're editing). If several branches dangle at once, right-click a node and choose
**Set As Output** to pin down which one wins instead of relying on that default.

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
