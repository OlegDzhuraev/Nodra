# agents.md — Nodra

> Language convention: this file is always written in English, regardless of the language used
> in the conversation that edits it.

## What this repository is

`Nodra` (`com.olegdzhuraev.nodra`) is a standalone Unity Package Manager (UPM) package: a simplified, code-first
node network for procedural mesh generation, edited as a visual graph (`UnityEditor.Experimental.GraphView`). A
`GeoGraph` is a polymorphic (`[SerializeReference]`) bag of **GeoNode**s (generators, modifiers, scatter/copy, merge)
plus the edges wiring their ports together; each node pulls its input(s) - a shared `GeoData` (points + polygon
primitives) - from whatever feeds its input port(s) rather than running in a fixed list order, and the result is
baked into a `Mesh` at the end. `NodraGraphWindow` is the visual editor for one `ProceduralMeshGenerator`'s `Graph`.

This is **not a standalone application** - the repo root is meant to be dropped into a Unity project's `Assets/`
folder (or referenced via `Packages/manifest.json` → git URL), which is why almost every file here has a Unity
`.meta` sibling.

## Structure

```
package.json          UPM manifest: name, version, Unity version requirement, dependencies
README.md             public-facing feature overview/usage examples - keep in sync with new features
LICENSE                GPLv3
Sources/
  Nodra.asmdef          runtime assembly, no dependencies (references: [])
  GeoData.cs             point cloud (position/normal/UV/vertex color) + polygon primitives + per-point float
                         attributes; Clone() for graph fan-out
  GeoNode.cs             abstract base: Id/Position (graph bookkeeping), InputCount, Process(GeoData[]) -> GeoData,
                         Warning (null by default - shown as a HelpBox in the graph editor when overridden),
                         Category ("Modifiers" by default - which "Create Node" submenu this type is filed under;
                         every concrete node overrides it - see NodraGraphView.CategoryOrder for the set in use)
  GeoEdge.cs             one connection: FromNodeId -> ToNodeId's ToPortIndex
  GeoGraph.cs            [SerializeReference] node list + edges; Evaluate() topologically pulls from the output node
  *GeneratorNode.cs      Grid, HeightMap (grid displaced by a Texture2D's grayscale - needs Read/Write Enabled),
                         Box, Sphere (UV), IcoSphere (subdivided icosahedron), Circle (flat disc), Cylinder
                         (also cone/frustum), Torus - InputCount 0, add fresh geometry
  LineGeneratorNode.cs, SplineGeneratorNode.cs (Catmull-Rom)  InputCount 0, points only - see ScatterNode below
  TransformNode.cs, ExtrudeNode.cs  - modifiers, mutate what they receive
  NoiseDisplaceNode.cs   displaces along XZ-sampled noise (Type: Perlin or Voronoi/cellular, a 3x3 nearest-feature-
                         point search with a sin/fract hash - not cryptographically uniform, just fast and stable)
  ChamferNode.cs         facets every shared edge (per-face inset + bridge quad) and caps 3+-face corners via a
                         face-adjacency walk (PositionKey, not index, so it works across per-face-duplicated
                         vertices too) - simplified bevel stand-in, only an open boundary edge is left ungapped
  SmoothByAngleNode.cs   per shared position (PositionKey), unions primitives into smoothing clusters by a
                         face-normal angle threshold and blends each cluster's normal, duplicating a point only
                         where one original index would otherwise have to carry two different results - also
                         tags every point with GeoData.SmoothGroupAttribute (see GeoMeshBuilder)
  TubeNode.cs            sweeps a polygon cross-section along a points-only path (rotation-minimizing frame via
                         the double reflection method, so it doesn't twist) - beam at low Sides, tube at high ones
  ArrayNode.cs           appends Count copies, each transformed by Offset/Rotation*copy around Pivot plus a
                         RadialOffset rotated along with it (constant local vector -> walks around a circle)
  VertexColorNode.cs     Mode: Flat fills GeoData.Colors with one Color; Gradient evaluates a Gradient by Height
                         (position along Axis, normalized to the input's own extent) or Slope (up . normal)
  MirrorNode.cs          appends a reflected copy across an axis/Offset plane, point order reversed (a reflection
                         is orientation-reversing) - a point already on the plane is reused, not duplicated
  FlipNormalsNode.cs     reverses every primitive's point order + negates every normal
  WeldNode.cs            merges points within Distance transitively (union-find over a spatial hash of cells sized
                         Distance), averaging position/normal/UV/color and dropping any now-degenerate primitive
  Axis3D.cs              X/Y/Z axis picker shared by MirrorNode, TaperNode, BendNode, TwistNode
  TaperNode.cs           scales the cross-section perpendicular to Axis, Lerp(1, Factor, t) across the input's
                         own Axis extent
  BendNode.cs            curves the input into a total-Angle arc across its Axis extent - bends into the next
                         axis in the X -> Y -> Z -> X cycle, third axis untouched (radius = extent / angle)
  TwistNode.cs           rotates every point around Axis by Angle * t, t from the input's own Axis extent -
                         a plain per-point rotation, so it keeps normals in sync itself (unlike Taper/Bend)
  RelaxNode.cs           Laplacian smoothing - Iterations passes of Lerp(point, neighbor average, Factor) over
                         index-based edge adjacency (see ExtrudeNode's boundary-edge definition, reused here too)
  SubdivideNode.cs       splits every primitive into one quad per corner around its own centroid, Iterations
                         times - the topological half of Catmull-Clark (no smoothing); an edge midpoint is cached
                         by point-index pair, so index-shared edges stay welded and per-face-duplicated ones don't
  CapHolesNode.cs        walks each open boundary loop (ExtrudeNode's boundary-edge definition again) via a
                         from->to map and fans an N-gon across it, reversed - see the node for the hand-derivation
  FaceFilterNode.cs      drops primitives whose face normal falls outside MaxAngle of Direction (Invert flips it)
  AutoUVNode.cs          Triplanar/Spherical/Cylindrical UV projection - not a real unwrap
  ScatterNode.cs, CopyToPointsNode.cs                     - scatter points across a surface, then stamp a mesh at each
  MergeNode.cs           InputCount 2 ("Base"/"Branch") - appends the branch's geometry into the base
  BooleanNode.cs         InputCount 2 ("A"/"B") - real CSG Union/Subtract/Intersect, delegates to GeoCsg
  GeoCsg.cs              BSP-tree CSG engine (Union/Subtract/Intersect on GeoData) backing BooleanNode - Build/
                         ClipTo fan front/back subtrees onto the thread pool for their first MaxParallelDepth
                         levels (System.Threading.Tasks, no package dependency), falling back to the original
                         Stack<T> walk past that depth or below ParallelPolygonThreshold; AggregateException from
                         Task.Wait()/.Result is unwrapped back to the original exception (WaitFlattened) so a
                         TimeoutException from CheckTimeout() still reaches ProceduralMeshGenerator's catch clause
  GeoMeshBuilder.cs      GeoData -> Unity Mesh (fan-triangulates, always recalculates normals - RecalculateNormals
                         works per vertex index, so it can't merge a UV seam's position-duplicate indices;
                         SmoothGroupAttribute-tagged points are re-averaged afterward to fix that, see the node above)
  Decimate/                the ONE exception to Nodra's zero-dependencies rule - a separate assembly, gated by a
                         versionDefines symbol, so the package being absent is a console warning, not a compile error
    Nodra.Decimate.asmdef  references Nodra + Whinarn.UnityMeshSimplifier.Runtime (com.whinarn.unitymeshsimplifier,
                           MIT - see https://github.com/Whinarn/UnityMeshSimplifier, install separately first);
                           versionDefines sets NODRA_DECIMATE only while that package is actually installed
    DecimateNode.cs        the class itself always compiles (unlike NODRA_DECIMATE-gated code elsewhere) - only
                           Process()'s body and the UnityMeshSimplifier-using helpers are wrapped in #if; without
                           the package, Process() passes input through unchanged and Warning explains why (see
                           GeoNode.Warning/NodraNodeView.BuildWarning), so a graph already carrying one keeps
                           deserializing under the same [SerializeReference] type instead of showing "Missing
                           types referenced from component" or losing the node on the next save. Quadric-error
                           decimation via UnityMeshSimplifier.MeshSimplifier.SimplifyMesh(Quality), fan-triangulates
                           GeoData in, 3-point primitives out, same as GeoCsg.ToGeoData
  ProceduralMeshGenerator.cs   MonoBehaviour: runs Graph.Evaluate(), bakes into the attached MeshFilter
  Editor/
    Nodra.Editor.asmdef        editor-only assembly, references only Nodra
    NodraGraphWindow.cs        EditorWindow: toolbar (target, Auto Generate, Generate) + NodraGraphView
    NodraGraphView.cs          GraphView: builds node/edge views from GeoGraph, writes edits back via Undo.RecordObject.
                               Every NodraNodeView gets Unbind()'d before its RemoveElement, and a node removal
                               schedules a deferred Populate() - PropertyField bindings are index-path-based
                               (Array.data[N]...), so a removed node leaves survivors bound to shifted/stale
                               indices otherwise, eventually throwing ObjectDisposedException on an unrelated blur
    NodraNodeView.cs           Node: ports from GeoNode.InputCount, fields bound straight to SerializedProperty,
                               GeoNode.Warning (if any) shown as a HelpBox above the fields
    NodraPort.cs               Port subclass reaching Port's protected ctor - only way to attach our own
                               IEdgeConnectorListener instead of Port's hardcoded default one
    ProceduralMeshGeneratorEditor.cs   AutoGenerate toggle, "Open Graph Editor" button, Generate/Save Mesh buttons
    Resources/
      NodraGraphView.uss       GridBackground colors - loaded via Resources.Load, not a hard asset reference
```

## Assembly Definitions

- `Nodra` (`Sources/`) - the runtime assembly. **Keep `references: []`** - this package is meant to work with zero
  dependencies, don't add a reference here without a strong reason. `DecimateNode`'s dependency on
  UnityMeshSimplifier is the one exception the package has - and it lives in its own separate assembly (below)
  specifically so it never has to touch this one.
- `Nodra.Editor` (`Sources/Editor/`) - `includePlatforms: ["Editor"]`, references only `Nodra`.
- `Nodra.Decimate` (`Sources/Decimate/`) - references `Nodra` + the external `Whinarn.UnityMeshSimplifier.Runtime`
  (MIT, install separately - see README), referenced by assembly name (a plain string, not `GUID:...`, since that
  package's own asmdef GUID doesn't exist in this repo to reference). Its `versionDefines` entry only defines
  `NODRA_DECIMATE` while `com.whinarn.unitymeshsimplifier` is actually installed - Unity's documented pattern for
  an optional package dependency; dropping the reference instead breaks it once the package IS installed. Without
  the package, a project gets one console warning about the unresolved reference, not a compile error.
  **`DecimateNode` the class is never itself `#if`-gated** - only `Process()`'s body is, per-method - because a
  `[SerializeReference]` field serializes a type by assembly-qualified name; if the whole class disappeared behind
  `#if`, a graph already containing a `DecimateNode` would show "Missing types referenced from component" (or
  silently drop the node on its next save) the moment the package isn't installed. Without the package,
  `Process()` just passes its input through unchanged instead of decimating, and `Warning` (see `GeoNode.Warning`)
  surfaces why directly in the node's own body in the graph editor (a `HelpBox`, via `NodraNodeView.BuildWarning`)
  instead of a console message that would otherwise repeat on every `Generate()`.

## Code conventions

- Namespace is flat `Nodra` for both the runtime and editor assemblies (not `Nodra.Editor`).
- **Allman brace style** (opening `{` on its own line), tab indentation.
- Public members of static utilities and extension methods carry a one-line `/// <summary>...</summary>`.
- Heavy use of expression-bodied methods/properties (`=>`) where the body is a single statement.
- Private fields - `camelCase` without an underscore prefix; constants - `PascalCase`.
- License header on every `.cs` file - GPLv3 boilerplate (see any existing file for the exact text).
- `GeoNode` subclasses are plain `[Serializable]` C# classes (not `ScriptableObject`/`MonoBehaviour`), added to a
  `GeoGraph.Nodes` via `[SerializeReference]` - a new node type needs no registration beyond deriving from `GeoNode`;
  `NodraGraphView`'s "Create Node" context menu finds it automatically via `TypeCache.GetTypesDerivedFrom<GeoNode>()`.
  Override `InputCount`/`GetInputPortName` only if the node needs something other than the default single "In" port.
  Override `Category` too - it's what submenu ("Deform", "Build", ...) the node is filed under; the default
  ("Modifiers") is a reasonable catch-all but the whole point of Category existing is a flat "Create Node" list
  stopped being usable once there were 30+ node types, so pick one of the existing categories deliberately.
- Winding convention: a primitive's point order is chosen so `Cross(v1 - v0, v2 - v0)` (for its first three points,
  or Newell's method for a general polygon - see `ExtrudeNode.ComputeFaceNormal`) equals the desired outward normal.
  Keep new generator/modifier nodes consistent with this or shading/culling comes out inverted.

## Verifying changes

There's no `.sln`/`.csproj` and no CI config in the repo - the project can't be built outside Unity. After making
changes, verify correctness by manual review; if Unity is available, prefer opening the consuming project and
letting it recompile. Don't delete `.meta` files or rename files without accounting for their `.meta` counterparts.

## Commits

History uses bracketed prefixes: `[+]` new feature, `[*]` change/improvement to existing code, `[!]` bug fix,
`[-]` removed feature or old code, `[^]` version/dependency bump. Follow this format when writing commit messages
in this repository.
