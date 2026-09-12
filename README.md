# Nodra

A simplified, code-first node network for generating meshes in Unity, in the Editor or at runtime.

A pipeline is an ordered list of **GeoNode**s (generators, modifiers, scatter/copy) that all read/write a shared
`GeoData` (points + polygon primitives), baked into a `Mesh` at the end. There's no visual node graph yet - nodes are
edited as a reorderable list on the `ProceduralMeshGenerator` component.

## Nodes

- **Generators** — `GridGeneratorNode`, `BoxGeneratorNode`
- **Modifiers** — `TransformNode`, `NoiseDisplaceNode`, `ExtrudeNode` (extrudes a whole connected surface as one
  shell, walling only its outer boundary)
- **Scatter/copy** — `ScatterNode` + `CopyToPointsNode` (scatter points across a surface, then stamp a mesh at each
  one)
- **Combine** — `MergeNode` (runs its own embedded sub-pipeline from scratch and appends the result into the main
  chain — the closest thing to a multi-input merge without a real visual graph)

## How to use

Add a `ProceduralMeshGenerator` component (requires a `MeshFilter`) to a GameObject, use the **+** button in its
inspector to add nodes to the list, configure them, then click **Generate** (or use the component's context menu) to
bake the result into the MeshFilter. `MergeNode` shows its own nested **+** list (`Branches`) the same way.

Turn on **Auto Generate** to have the mesh rebuild automatically whenever a node is added/removed/reordered or a
field on one changes. **Save Mesh to Project...** regenerates and saves the current result as a `.asset` file, so it
survives as a normal project asset instead of only living as an in-memory Mesh on the MeshFilter.

To add your own node, derive from `GeoNode` and implement `Process(GeoData input)` - it'll automatically show up in
the Add Node dropdown.

```cs
using Nodra;

var generator = gameObject.AddComponent<ProceduralMeshGenerator>();
generator.Nodes.Nodes.Add(new GridGeneratorNode { Size = new Vector2(20, 20), Resolution = new Vector2Int(20, 20) });
generator.Nodes.Nodes.Add(new NoiseDisplaceNode { Amplitude = 2f, Frequency = 0.15f });
generator.Nodes.Nodes.Add(new ExtrudeNode { Distance = 1.5f });
generator.Generate();
```

## Structure

```
Sources/
  Nodra.asmdef          runtime assembly - GeoData, GeoNode(s), GeoMeshBuilder, ProceduralMeshGenerator
  Editor/
    Nodra.Editor.asmdef   editor-only assembly - GeoNodeListDrawer, ProceduralMeshGeneratorEditor
```

## License

GPLv3 - see [LICENSE](LICENSE).
