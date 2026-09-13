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
  GeoData.cs             point cloud + polygon primitives + per-point float attributes; Clone() for graph fan-out
  GeoNode.cs             abstract base: Id/Position (graph bookkeeping), InputCount, Process(GeoData[]) -> GeoData
  GeoEdge.cs             one connection: FromNodeId -> ToNodeId's ToPortIndex
  GeoGraph.cs            [SerializeReference] node list + edges; Evaluate() topologically pulls from the output node
  *GeneratorNode.cs      Grid, Box, Sphere, Cylinder (also cone/frustum), Torus - InputCount 0, add fresh geometry
  LineGeneratorNode.cs   InputCount 0, points only (no primitives) - see ScatterNode below
  TransformNode.cs, NoiseDisplaceNode.cs, ExtrudeNode.cs  - modifiers, mutate what they receive
  ScatterNode.cs, CopyToPointsNode.cs                     - scatter points across a surface, then stamp a mesh at each
  MergeNode.cs           InputCount 2 ("Base"/"Branch") - appends the branch's geometry into the base
  BooleanNode.cs         InputCount 2 ("A"/"B") - real CSG Union/Subtract/Intersect, delegates to GeoCsg
  GeoCsg.cs              BSP-tree CSG engine (Union/Subtract/Intersect on GeoData) backing BooleanNode
  GeoMeshBuilder.cs      GeoData -> Unity Mesh (fan-triangulates, always recalculates normals)
  ProceduralMeshGenerator.cs   MonoBehaviour: runs Graph.Evaluate(), bakes into the attached MeshFilter
  Editor/
    Nodra.Editor.asmdef        editor-only assembly, references only Nodra
    NodraGraphWindow.cs        EditorWindow: toolbar (target, Auto Generate, Generate) + NodraGraphView
    NodraGraphView.cs          GraphView: builds node/edge views from GeoGraph, writes edits back via Undo.RecordObject
    NodraNodeView.cs           Node: ports from GeoNode.InputCount, fields bound straight to SerializedProperty
    NodraPort.cs               Port subclass reaching Port's protected ctor - only way to attach our own
                               IEdgeConnectorListener instead of Port's hardcoded default one
    ProceduralMeshGeneratorEditor.cs   AutoGenerate toggle, "Open Graph Editor" button, Generate/Save Mesh buttons
    Resources/
      NodraGraphView.uss       GridBackground colors - loaded via Resources.Load, not a hard asset reference
```

## Assembly Definitions

- `Nodra` (`Sources/`) - the runtime assembly. **Keep `references: []`** - this package is meant to work with zero
  dependencies, don't add a reference here without a strong reason.
- `Nodra.Editor` (`Sources/Editor/`) - `includePlatforms: ["Editor"]`, references only `Nodra`.

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
