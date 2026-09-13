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
- **Modifiers** — `TransformNode`, `NoiseDisplaceNode` (Perlin or Voronoi/cellular noise, picked via `Type`),
  `ExtrudeNode`, `ChamferNode`, `SmoothByAngleNode` (blends
  normals across a shared point when the angle between its faces is under a threshold, keeps them faceted above
  it - fixes the lighting seam a displaced Sphere/Cylinder/Torus otherwise shows at its UV seam, since
  Mesh.RecalculateNormals works per vertex index and can't tell that the seam's two index columns are the same
  position), `TubeNode` (sweeps a polygon cross-section - a "beam" at low `Sides`, a round tube at high ones - along a
  points-only path like `LineGeneratorNode`/`SplineGeneratorNode`, replacing the path with the swept mesh),
  `ArrayNode` (appends Count copies, each built from a cumulative offset/rotation around a pivot plus a rotating
  radial offset - a linear row, a spinning ring, or a spiral depending on which of the three you use),
  `VertexColorNode` (Flat mode fills one Color everywhere; Gradient mode evaluates a Gradient by Height along an
  Axis or by Slope - grass-on-flat/rock-on-steep terrain coloring, no separate node needed), `MirrorNode` (appends a mirrored copy across an
  axis/Offset plane, welding points already sitting on it), `FlipNormalsNode` (reverses winding + normals - fixes
  an inside-out BooleanNode result or builds an interior-facing shape), `WeldNode` (merges points within Distance
  of each other, transitively, averaging their position/normal/UV/color - cleans up the duplicate points a
  BooleanNode/MergeNode/CopyToPointsNode seam typically leaves), `TaperNode` (scales the cross-section
  perpendicular to an axis from full size to Factor across the input's own extent - cylinder into a cone),
  `BendNode` (curves the input into an arc of a given angle over its extent along an axis - straight tube into a
  pipe bend), `TwistNode` (rotates every point around an axis by an angle that grows across the input's extent -
  straight tube into a drill bit), `RelaxNode` (Laplacian smoothing - moves each point toward its neighbors'
  average position, softening a jagged NoiseDisplaceNode/BooleanNode result), `SubdivideNode` (splits every
  primitive into one quad per corner, fanned around its own centroid, Iterations times - adds detail without
  rounding anything, works on any polygon size), `CapHolesNode` (fans an N-gon across
  every open boundary loop it finds - fills a hole punched through a patch, or the open edge
  ChamferNode/ExtrudeNode/BooleanNode leave by design), `FaceFilterNode` (keeps only primitives whose face normal
  is within MaxAngle of Direction, or drops them instead with Invert - cut the bottom off a sphere, carve a
  half-pipe, without a full BooleanNode), `DecimateNode` (reduces triangle count to a Quality fraction of the
  original via [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier) - see **Optional: DecimateNode**
  below, this is the one node in the whole package with an external dependency), `AutoUVNode` (Triplanar/
  Spherical/Cylindrical projection - a simple default after a node whose own UVs no longer make sense, not a real
  unwrap; `Extras/`
  ships a `Nodra/Checker` URP shader + `M_Checker` material to eyeball the result for stretching/mirroring/seams,
  also multiplying in vertex color so `VertexColorNode` shows up on it too - and actually lit (main light + ambient
  probe) rather than flat-unlit, so it doubles as a normals check: a flipped/wrong-direction face goes visibly dark);
- **Scatter/copy** — `ScatterNode` + `CopyToPointsNode` (scatter points across a surface, then stamp a mesh at each
  one). `ScatterNode`'s own output is points with no faces - meant to feed `CopyToPointsNode`, not to be the graph's
  output directly, so it bakes into a Mesh with nothing to render; set it as Output anyway (e.g. to check the
  distribution) and the Inspector draws its points/normals as Scene view gizmos instead
- **Combine** — `MergeNode` (two input ports, "Base" and "Branch" - appends the branch's geometry into the base);
  `BooleanNode` (Union/Subtract/Intersect - a CSG boolean via a BSP tree; both inputs need to be closed, manifold
  shapes; the tree's own construction/clipping runs multithreaded for its first few levels on heavier input, still
  a single blocking call from BooleanNode's own side).

## How to use

Add a `ProceduralMeshGenerator` component (requires a `MeshFilter`) to a GameObject, then click **Open Graph
Editor** in its inspector. Right-click the graph canvas to add nodes. 

The mesh is rebuilt from whichever node is marked **OUTPUT** (a green outline) - by default whichever node has nothing
connected to its own output (the end of the chain, or of whichever branch you're editing).

If several branches dangle at once, right-click a node and choose **Set As Output** to pin down which one wins instead 
of relying on that default.

Turn on **Auto Generate** (in the graph window's toolbar, or the component's inspector) to have the mesh rebuild
automatically on every graph edit. 

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
console warning about the unresolved package reference). To install: **Window → Package Manager → + → Install
package from git URL...** and paste
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
