<p align="center">
  <img src="Extras/Repo/nodra_logo.png" alt="Nodra" width="300">
</p>

# Nodra

**[Watch the demo on YouTube](https://www.youtube.com/watch?v=BM-pM-0AHjk)**

A simplified, code-first node network for generating meshes in Unity, in the Editor or at runtime - edited as a
visual node graph.

A `GeoGraph` is a collection of **GeoNode**s (generators, modifiers, scatter/copy, merge) wired together by edges; each
node pulls its input(s) - a shared `GeoData` (points + polygon primitives) - from whatever's connected to its input
port(s), and the result is baked into a `Mesh` at the end. Edit the graph visually in the **Nodra Graph** window.

## Nodes

- **Generators** — `GridGeneratorNode`, `HeightMapGeneratorNode` (a grid displaced along Y by a Texture2D's
  grayscale value - simple heightmap terrain, needs the texture's Read/Write Enabled import setting on),
  `BoxGeneratorNode`, `SphereGeneratorNode` (UV sphere), `IcoSphereGeneratorNode`
  (subdivided icosahedron - even triangle sizes, no pole pinching), `CircleGeneratorNode` (flat disc, optionally
  unfilled to feed `ExtrudeNode`), `CylinderGeneratorNode` (also a cone/frustum via `RadiusTop`/`RadiusBottom`),
  `TorusGeneratorNode`, `LineGeneratorNode`/`SplineGeneratorNode` (straight line / Catmull-Rom curve through a list
  of control points - `SplineGeneratorNode`'s own control points always draw as yellow, draggable Scene view
  handles, regardless of the graph's current Output; points only, no faces, feed either into `CopyToPointsNode` for
  fences/columns/stepping stones along a path; set
  one as Output directly and, like `ScatterNode` below, its points/normals draw as Scene view gizmos)
- **Modifiers** — `TransformNode`, `NoiseDisplaceNode` (Perlin or Voronoi/cellular noise),
  `ExtrudeNode`, `ChamferNode`, `SmoothByAngleNode`, `TubeNode` (sweeps a polygon cross-section - a "beam" at low `Sides`,
  a round tube at high ones - along a points-only path like `LineGeneratorNode`/`SplineGeneratorNode`), `ArrayNode`,
  `VertexColorNode`, `MirrorNode`, `FlipNormalsNode`, `WeldNode`, `TaperNode`,
  `BendNode`, `TwistNode`, `RelaxNode` (Laplacian smoothing), `SubdivideNode`, `CapHolesNode`, `FaceFilterNode`,
   `DecimateNode` (requires 3rd party package install), `AutoUVNode` (Triplanar/Spherical/Cylindrical projection);
- **Scatter/copy** — `ScatterNode` + `CopyToPointsNode` (scatter points across a surface, then stamp a mesh at each
  one);
- **Combine** — `MergeNode` (two input ports, "Base" and "Branch" - appends the branch's geometry into the base);
  `BooleanNode` (Union/Subtract/Intersect - a CSG boolean via a BSP tree; both inputs need to be closed, manifold
  shapes).

## How to use

Add a `ProceduralMeshGenerator` component (requires a `MeshFilter`) to a GameObject, then click **Open Graph
Editor** in its inspector. Right-click the graph canvas to add nodes. 

The final mesh is rebuilt from `Geometry Output` node (marked green).

Turn on **Auto Generate** (in the graph window's toolbar, or the component's inspector) to have the mesh rebuild
automatically on every graph edit.

Turn on **Show Normals** (in the inspector) to draw a Scene view line per vertex along the baked `Mesh`'s own
normal (shows the `Mesh.normals` the renderer uses) - useful for checking a generator's winding or
`SmoothByAngleNode`'s result actually looks faceted/smooth where expected. 

**Save Mesh to Project...** (in the inspector) regenerates and saves the current
result as a `.asset` file, so it survives as a normal project asset instead of only living as an in-memory Mesh on
the MeshFilter.

To add your own node, derive from `GeoNode` and implement `Process(GeoData input)` (or `Process(GeoData[] inputs)`
for more than one input port) - it'll automatically show up in the graph's "Create Node" menu, filed under
whichever submenu its `Category` override names (defaults to "Modifiers" if you don't override it).

### Optional: DecimateNode

`DecimateNode` lives in its own `Nodra.Decimate` assembly and needs
[UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier) (MIT) installed separately - it isn't
bundled, and nothing else in Nodra needs it. Without it installed: no compile error, and a graph that already has
a `DecimateNode` in it keeps working - the node itself stays put, showing a warning box right in its own body in
the graph editor and passing its input through unchanged instead of decimating (you'll also see one harmless
console warning about the unresolved package reference). 

To install: **Window → Package Manager → + → Install package from git URL...** and paste
`https://github.com/Whinarn/UnityMeshSimplifier.git`.

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

## License

GPLv3 - see [LICENSE](LICENSE).
