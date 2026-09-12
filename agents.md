# agents.md — Nodra

> Language convention: this file is always written in English, regardless of the language used
> in the conversation that edits it.

## What this repository is

`Nodra` (`com.olegdzhuraev.nodra`) is a standalone Unity Package Manager (UPM) package: a simplified, code-first
node network for procedural mesh generation. A pipeline is an ordered list of **GeoNode**s
(generators, modifiers, scatter/copy) threading a shared `GeoData` (points + polygon primitives) through, baked into
a `Mesh` at the end. There's no visual node graph yet - nodes are edited as a reorderable, polymorphic
(`[SerializeReference]`) list on the `ProceduralMeshGenerator` component.

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
  GeoData.cs             point cloud + polygon primitives + per-point float attributes
  GeoNode.cs             abstract base: Process(GeoData input) -> GeoData
  GeoNodeList.cs         reusable ordered/polymorphic node list, shared by the top-level pipeline and MergeNode
  *GeneratorNode.cs      Grid, Box - ignore input, add fresh geometry
  TransformNode.cs, NoiseDisplaceNode.cs, ExtrudeNode.cs  - modifiers, mutate what they receive
  ScatterNode.cs, CopyToPointsNode.cs                     - scatter points across a surface, then stamp a mesh at each
  MergeNode.cs           runs its own embedded GeoNodeList branch and appends the result into the main chain
  GeoMeshBuilder.cs      GeoData -> Unity Mesh (fan-triangulates, always recalculates normals)
  ProceduralMeshGenerator.cs   MonoBehaviour: runs GeoNodeList, bakes into the attached MeshFilter
  Editor/
    Nodra.Editor.asmdef        editor-only assembly, references only Nodra
    GeoNodeListDrawer.cs       CustomPropertyDrawer for GeoNodeList - reorderable list + "pick a node type" Add menu
    ProceduralMeshGeneratorEditor.cs   AutoGenerate toggle, Generate/Save Mesh to Project buttons
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
  `GeoNodeList` via `[SerializeReference]` - a new node type needs no registration beyond deriving from `GeoNode`;
  `GeoNodeListDrawer` finds it automatically via `TypeCache.GetTypesDerivedFrom<GeoNode>()`.
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
