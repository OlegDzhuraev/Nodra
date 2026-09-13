<p align="center">
  <img src="Extras/Repo/nodra_logo.png" alt="Nodra" width="240">
</p>

# Nodra

**[Watch the demo on YouTube](https://www.youtube.com/watch?v=BM-pM-0AHjk)**

A simplified, code-first node network for generating meshes in Unity, in the Editor or at runtime - edited as a
visual node graph.

A `GeoGraph` is a collection of **GeoNode**s (generators, modifiers, scatter/copy, merge) wired together by edges; each
node pulls its input(s) - a shared `GeoData` (points + polygon primitives) - from whatever's connected to its input
port(s), and the result is baked into a `Mesh` at the end. Edit the graph visually in the **Nodra Graph** window.

## Nodes

#### Generators

- `GridGeneratorNode` — a flat, subdivided plane
- `HeightMapGeneratorNode` — a terrain-like plane shaped by a heightmap texture
- `BoxGeneratorNode` — a box
- `SphereGeneratorNode` — a sphere
- `IcoSphereGeneratorNode` — a sphere with even, non-pinched triangles
- `CircleGeneratorNode` — a flat disc
- `CylinderGeneratorNode` — a cylinder or cone
- `TorusGeneratorNode` — a torus (donut shape)
- `LineGeneratorNode` — a straight line of points
- `SplineGeneratorNode` — a smooth curved path of points, editable directly in the Scene view

#### Modifiers

- `TransformNode` — moves, rotates and scales the mesh
- `NoiseDisplaceNode` — roughens the surface with noise
- `ExtrudeNode` — pushes the surface outward into a solid shape
- `ChamferNode` — softens sharp edges with a small bevel
- `TubeNode` — wraps a tube or beam around a path
- `ArrayNode` — repeats the mesh in a row, ring or spiral
- `MirrorNode` — mirrors the mesh across an axis
- `TaperNode` — narrows or widens the mesh along an axis
- `BendNode` — curves the mesh into an arc
- `TwistNode` — twists the mesh around an axis
- `RelaxNode` — smooths out jagged geometry
- `SubdivideNode` — adds extra detail to the mesh
- `FaceFilterNode` — removes faces facing a chosen direction
- `DecimateNode` — reduces the mesh's triangle count (needs an optional extra package)

#### Geometry fixers

- `FlipNormalsNode` — turns the mesh inside out
- `WeldNode` — merges nearby duplicate points together
- `SmoothByAngleNode` — smooths or facets shading based on edge angle
- `CapHolesNode` — fills open holes in the mesh

#### Color/UV

- `AutoUVNode` — generates simple UVs automatically
- `VertexColorNode` — paints the mesh with a flat color or gradient

#### Scatter/copy

- `ScatterNode` — scatters points across a surface
- `CopyToPointsNode` — stamps a mesh at each scattered point

#### Combine

- `MergeNode` — combines two meshes into one
- `BooleanNode` — cuts or combines two shapes like real solids

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


### Optional: DecimateNode

`DecimateNode` lives in its own `Nodra.Decimate` assembly and needs
[UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier) (MIT) installed separately - it isn't
bundled, and nothing else in Nodra needs it. Without it installed: no compile error, and a graph that already has
a `DecimateNode` in it keeps working - the node itself stays put, showing a warning box right in its own body in
the graph editor and passing its input through unchanged instead of decimating (you'll also see one harmless
console warning about the unresolved package reference). 

To install: **Window → Package Manager → + → Install package from git URL...** and paste
`https://github.com/Whinarn/UnityMeshSimplifier.git`.

## Creating custom nodes

To add your own node, derive from `GeoNode` and implement `Process(GeoData input)` (or `Process(GeoData[] inputs)`
for more than one input port) - it'll automatically show up in the graph's "Create Node" menu, filed under
whichever submenu its `Category` override names (defaults to "Modifiers" if you don't override it).

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
