# agents.md — Nodra

> Language convention: this file is always written in English, regardless of the language used
> in the conversation that edits it.

## What this repository is

`Nodra` (`com.olegdzhuraev.nodra`) is a standalone Unity Package Manager (UPM) package: a simplified, code-first
node network for procedural mesh generation, edited as a visual graph (`UnityEditor.Experimental.GraphView`). A
`GeoGraph` is a polymorphic (`[SerializeReference]`) bag of **GeoNode**s (generators, modifiers, scatter/copy, merge)
plus the edges wiring their ports together; each node pulls its input(s) - a shared `GeoData` (points + polygon
primitives) - from whatever feeds its input port(s) rather than running in a fixed list order, and the result is
baked into a `Mesh` at the end. `NodraGraphWindow` is the visual editor for a `GeoGraph` - either a
`ProceduralMeshGenerator` component's own, or a standalone `GeoGraphAsset`'s (referenced from elsewhere via
`SubGraphNode`, so one graph's result can be reused as a node in another).

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
                         attributes; Clone() for graph fan-out. All of the below live here rather than on whichever
                         node needs them because attributes is private - only GeoData itself can move named
                         attribute values in step with the points they belong to:
                           CompactPoints(keep) drops points and returns each survivor's new index (or -1) - doesn't
                             touch Primitives itself, since only the caller (RemoveUnusedPointsNode/DeletePointsNode)
                             knows what a primitive referencing a dropped point should become
                           CopyAttributes/BlendAttributes(2-point t, N-point average, weighted) carry attributes
                             into a point a node derives from one or more existing ones - MirrorNode's reflection,
                             ArrayNode's Nth copy, ExtrudeNode's shell, ChamferNode's inset corner, every point of
                             one of TubeNode's rings/cap fans (CopyAttributes, 1 source - the path point that ring/
                             cap is built around, in TubeNode's case); SliceNode's plane-crossing point,
                             SubdivideNode's edge midpoint (2-point, the node's own already-computed t);
                             SubdivideNode's face centroid (N-point average). Without these a node that creates a
                             point via AddPoint alone silently drops whatever a SetAttributeNode painted onto its
                             source(s) - the fallback (0) shows up there instead
                           BlendAttributesFrom(source, ...) is the cross-object weighted form, for a node like
                             ScatterNode sampling a brand-new output GeoData from an existing input one (its own
                             barycentric a/b/c reused as the blend weights) rather than deriving a point within the
                             same object every other case here assumes
                           RemapAttributes(remap, newCount) is WeldNode's own case - it merges many points into
                             fewer by building entirely new Points/Normals/Uvs/Colors lists itself rather than
                             calling AddPoint or CompactPoints, so attributes need the same many-to-one averaging
                             done explicitly, keyed by the same remap[] WeldNode already computed
                           GetAttributeValues(name) returns every point's value densely (fallback where unset) for
                             a node scanning the whole attribute at once (a Height-style min/max) instead of one
                             GetAttribute call per point; RemoveAttribute(name) drops a whole named attribute (e.g.
                             a temporary one only needed to drive a DeletePointsNode threshold)
                           GetBounds() is the AABB of every current Point - used by SetAttributeNode/VertexColorNode's
                             Bounds mode when their optional Bounds input port is connected, and by NodraGraphWindow's
                             Bounds gizmo to show what's actually flowing into that port
                         Known gap NOT covered by any of the above (a node creating points there still resets a
                         carried-over attribute to its fallback): GeoCsg/BooleanNode's clip-intersection points.
                         CopyToPointsNode reads rather than carries - see its own ScaleAttribute
  GeoNode.cs             abstract base: Id/Position (graph bookkeeping), InputCount, Process(GeoData[]) -> GeoData,
                         Warning (null by default - shown as a HelpBox in the graph editor when overridden),
                         Category ("Modifiers" by default - which "Create Node" submenu this type is filed under;
                         every concrete node overrides it - see NodraGraphView.CategoryOrder for the set in use)
                         ExtraHash (null by default) lets a node fold extra content into GeoGraph.ComputeNodeHash
                         beyond what JsonUtility.ToJson(node) can see on its own - only SubGraphNode overrides it
                         EditableAssetFieldName (null by default) names a UnityEngine.Object-reference field this
                         node wants a live "Open" button for in the graph editor (NodraNodeView) - only
                         SubGraphNode overrides it, for its own SubGraph field
  GeoGraphAsset.cs       ScriptableObject wrapping one GeoGraph as its own reusable project asset (Create > Nodra >
                         Geo Graph) instead of living inside a ProceduralMeshGenerator component - referenced by
                         SubGraphNode so several graphs (different objects, even different scenes) can all build on
                         the same shared piece of graph. Edited in the same NodraGraphWindow as any other GeoGraph -
                         double-click the asset (NodraGraphWindow.OnOpenGeoGraphAsset) or its Inspector's "Open
                         Graph Editor" button (GeoGraphAssetEditor)
  SubGraphNode.cs        evaluates a GeoGraphAsset's own Graph.Evaluate() and returns its output - InputCount 0
                         (self-contained, like *GeneratorNode.cs, not a modifier of an input it doesn't have).
                         ExtraHash folds in SubGraph.GetEntityId() + Graph.ComputeOutputHash() so both swapping
                         which asset is referenced and editing that asset's content elsewhere still invalidate this
                         node's GeoGraph.resultCache entry - JsonUtility can't see either on its own. A static
                         `visiting` HashSet<GeoGraphAsset> (shared between Process() and ExtraHash) guards against a
                         cycle across assets (A referencing B referencing A, however many hops) - GeoGraph's own
                         in-graph cycle guard (EvaluateNode/ComputeNodeHashOnly's `visiting` HashSet<string>) can't
                         see across the separate top-level Evaluate()/ComputeOutputHash() call each nesting level
                         makes into its own GeoGraph instance, so a cross-asset cycle needs this separate guard
  MinVector2IntAttribute.cs  [MinVector2Int(min)] / (minX, minY) - Unity's built-in [Min] doesn't support
                             Vector2Int/Vector3Int, so a Resolution field needs this instead to reject a
                             too-small/negative value in the field itself, matching whatever the node's own
                             Process() already clamps to internally (see MinVector2IntDrawer, Sources/Editor/)
  ShowIfAttribute.cs         [ShowIf(nameof(Sibling), Sibling.Value, ...)] - hides a field in the graph editor
                             unless an enum sibling field currently holds one of the given values (stack several
                             on one field for an AND, e.g. VertexColorNode's Bounds needing both Mode == Gradient
                             and Source == Bounds); read directly by NodraNodeView.BindShowIf via reflection, no
                             CustomPropertyDrawer involved. Display-only - Process() still reads a hidden field's
                             value untouched
  GeoEdge.cs             one connection: FromNodeId -> ToNodeId's ToPortIndex
  GeoGraph.cs            [SerializeReference] node list + edges; Evaluate() topologically pulls from the output node,
                         memoizing each node's Process() result in resultCache (in-memory, not serialized) keyed by
                         a JsonUtility hash of the node's own fields plus its whole upstream chain's hash - so
                         editing one node (e.g. VertexColorNode's Color) only re-runs that node and its descendants,
                         reusing whatever unaffected ancestors already computed on a prior Evaluate(). Every node
                         type here is otherwise a pure function of its fields + inputs (ScatterNode/CopyToPointsNode
                         seed System.Random explicitly rather than reading real randomness), which is what makes
                         this safe; a field this hash can't see change under it (e.g. a HeightMap Texture2D repainted
                         in place, same reference) would go stale until something else invalidates that node.
                         ComputeNodeHash folds GeoNode.ExtraHash into the JsonUtility hash when a node overrides it
                         (only SubGraphNode does) - see that node's own entry for why JsonUtility alone isn't enough.
                         GeoGraph.LogCacheStats (ProceduralMeshGeneratorEditor's "Log Cache Stats" toggle) logs
                         reused-vs-recomputed node counts and elapsed time per Evaluate() - the way to actually
                         confirm the cache is doing something instead of eyeballing perceived speed, which is
                         dominated by whatever node was just edited (always recomputes - that's correct, not a
                         cache miss bug) and by GeoMeshBuilder.Build()'s RecalculateNormals/Tangents, which runs in
                         full on every Generate() regardless of node caching since the baked Mesh always needs it.
                         ComputeOutputHash() computes the output node's own combined hash (ComputeNodeHashOnly
                         mirrors EvaluateNode's edge-walk/formula exactly - keep both in sync) without evaluating
                         anything - no Process(), no GeoData, no resultCache - so ProceduralMeshGenerator.Generate()
                         can call it FIRST and skip the real Evaluate() call
                         entirely (not just the bake) when AutoGenerate's OnValidate fires for an edit that couldn't
                         have changed the actual output (a node not wired into it at all, most commonly). Matters
                         because even a 100%-cache-hit Evaluate() still Clone()s every reused node's result along the
                         reachable chain to keep resultCache's own copies pristine - on a large mesh that clone cost
                         alone is enough to lag a slider drag on a node that's nowhere near the output
                         TryGetCachedResult(nodeId) reads resultCache directly, no evaluation - used by
                         NodraGraphWindow's Bounds gizmo to see what the last real Generate() computed for whatever
                         feeds a selected node's Bounds input port
  *GeneratorNode.cs      Grid, HeightMap (grid displaced by a Texture2D's grayscale - needs Read/Write Enabled),
                         Box, Sphere (UV), IcoSphere (subdivided icosahedron), Circle (flat disc), Cylinder
                         (also cone/frustum), Torus - InputCount 0, add fresh geometry. Box/Sphere/Grid/Torus/
                         Cylinder/Circle/IcoSphere run entirely in NodraCore (Native/NodraCore/BoxGenerator.cs,
                         SphereGenerator.cs, GridGenerator.cs, TorusGenerator.cs, CylinderGenerator.cs,
                         CircleGenerator.cs, IcoSphereGenerator.cs) - no managed fallback, same DecimateNode-style
                         Warning + (here) no-geometry-at-all degradation as WeldNode/SmoothByAngleNode; only
                         HeightMap is still plain managed C#. Box's native output shape (24 points, 6 quads) is fully
                         deterministic regardless of Size, so its own ABI needs no point/primitive counts on the
                         wire at all; Sphere/Grid/Torus's DO depend on Resolution, clamped the same one-line way
                         (Mathf.Max) on both the Unity wrapper (to size buffers before the call) and inside each
                         Run itself. Cylinder needs the most sizing logic of the five: point/primitive counts
                         depend on HeightSegments AND on whether each cap actually gets added (CapBottom/CapTop AND
                         that end's own radius > 0), replicated verbatim on the Unity side
                         (NodraNative.CylinderGenerator) rather than a single clamp; its primitives also aren't
                         uniformly-sized like the other four (side quads then up to two triangle cap fans), which
                         NodraNative.CylinderGenerator reads back by relying on CylinderGenerator.Run's own fixed
                         emission order rather than a PrimitiveLengths array on the wire. Circle's own sizing
                         depends on BOTH the usual Mathf.Max(3, Segments) clamp AND its own Fill toggle (a bare
                         ring has zero primitives) - NodraNative.CircleGenerator replicates both before the call,
                         reusing AppendTrianglePrimitives (a 3-index sibling of AppendQuadPrimitives) to read the
                         fan back, since Circle's primitives (when Fill) are uniform triangles rather than quads.
                         IcoSphere's sizing depends on Subdivisions via the exact edge-count recurrence a closed
                         triangle mesh always follows (unique edges = 3*PrimitiveCount/2, and each subdivision adds
                         exactly one new point per unique edge) - NodraNative.IcoSphereGenerator runs that same
                         recurrence itself before the call rather than a single clamp, reusing
                         AppendTrianglePrimitives the same way Circle does
  LineGeneratorNode.cs   thin wrapper around NodraNative.LineGenerator - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() produces no geometry at all instead). InputCount 0, a point cloud (no
                         primitives) same as ScatterNode below
  SplineGeneratorNode.cs thin wrapper around NodraNative.SplineGenerator - see Native/NodraCore/SplineGenerator.cs
                         for the actual Catmull-Rom sampling. InputCount 0, points only, same as LineGeneratorNode
  TransformNode.cs       thin wrapper around NodraNative.Transform - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Native/NodraCore/
                         Transform.cs's Matrix4x4.TRS scale-then-rotate-then-translate composition is standard,
                         unambiguous linear algebra; Quaternion.Euler's OWN internal composition order is the one
                         genuine fidelity risk (can't be checked from NodraCore.Tests at all, no UnityEngine.
                         Quaternion out there to compare against) - see Transform.cs's own doc comment and the
                         "Test Native Transform" Editor menu item, which checks a combined rotation against the
                         real UnityEngine.Quaternion.Euler from inside Unity, the only place the real answer is
                         available at all
  ExtrudeNode.cs         thin wrapper around NodraNative.Extrude - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/Extrude.cs: extrudes all input primitives as a single connected shell -
                         each ORIGINAL point offset once along the averaged normal of its surrounding faces, side
                         walls only along the outer boundary (edges used by exactly one primitive). TopPoints are
                         always exactly data.PointCount - one offset copy per original point, at the SAME index -
                         so Extrude.cs's own Primitives use a combined [0, 2*originalPointCount) index space
                         instead of a separate remap table: [0, originalPointCount) means an untouched original
                         point, the rest means originalPointCount + i's own top point. NodraNative.Extrude needs no
                         remapping at all for this - appending TopPoints right after data's own existing ones
                         reproduces that same combined space on the real GeoData automatically. The boundary-wall
                         primitive count isn't deterministic, so (like Chamfer) NodraNative.Extrude allocates a
                         safe upper bound instead - at most primitiveCount + totalIndices primitives (cap copies,
                         exact if CapNewFace, plus loosely-bounded walls), at most 5*totalIndices total corners
  NoiseDisplaceNode.cs   thin wrapper around NodraNative.NoiseDisplace - no managed fallback (same pattern as
                         WeldNode/SmoothByAngleNode: Warning explains why when NodraNative.IsAvailable is false,
                         Process() passes geometry through unchanged instead). Displaces along XZ-sampled noise
                         (Type: Perlin or Voronoi/cellular, a 3x3 nearest-feature-point search with a sin/fract
                         hash - not cryptographically uniform, just fast and stable) - see Native/NodraCore/
                         NoiseDisplace.cs for the actual algorithm. Voronoi there is a faithful, bit-identical
                         port (it was always Nodra's own algorithm); Perlin is NOT - UnityEngine.Mathf.PerlinNoise
                         runs inside Unity's own closed-source native engine, confirmed (via Unity's own forums,
                         not assumption) to have no publicly reproducible equivalent outside it, so native uses a
                         different, from-scratch gradient noise (Ken Perlin's own 2002 reference algorithm/
                         permutation table) instead. A graph already using NoiseType.Perlin looks visibly
                         different once NodraCore becomes available - a deliberate, discussed tradeoff (see
                         NoiseDisplace.cs's own doc comment), not a bug
  ChamferNode.cs         thin wrapper around NodraNative.Chamfer - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/Chamfer.cs: facets every shared edge (per-face inset + bridge quad) and
                         caps 3+-face corners via a face-adjacency walk (PositionKey, not index, so it works across
                         per-face-duplicated vertices too) - simplified bevel stand-in, only an open boundary edge
                         is left ungapped. By far the most involved native ABI in this project: inset points are
                         always exactly data's own total index count (one per original corner, deterministic), but
                         the final primitive SET (bridges, caps, then each primitive's own inset copy) isn't
                         deterministically sized at all, so NodraNative.Chamfer allocates a safe (loose) upper
                         bound instead - at most 2*totalIndices + primitiveCount primitives, at most 4*totalIndices
                         total corners - derived from bridges consuming edgeOwners entries in pairs (<=
                         totalIndices/2 of them, 4 corners each) plus caps/insets each bounded by totalIndices of
                         their own. Every inset point is generated in the same primitive-major, slot-minor order
                         Chamfer.Run walks primitives in, so NodraNative.Chamfer recovers which ORIGINAL point each
                         inset point came from by replaying that same order itself (reading data.Primitives BEFORE
                         clearing it) rather than needing that mapping reported back over the wire - same
                         Unity-side-only CopyAttributes bookkeeping trick ArrayModifier/AutoUV use. An inset
                         point's own color is NOT carried over from its source (defaults white, matching
                         ChamferNode.BuildInsetFace's original 3-arg AddPoint call) - not something this port
                         changes, just faithfully reproduces
  SmoothByAngleNode.cs   thin wrapper around NodraNative.SmoothByAngle - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives
                         in Native/NodraCore/SmoothByAngle.cs: per shared position (PositionKey), unions primitives
                         into smoothing clusters by a face-normal angle threshold and blends each cluster's normal,
                         duplicating a point only where one original index would otherwise have to carry two
                         different results - also tags every point with a SmoothGroup value (GeoData.
                         SmoothGroupAttribute here, applied via SetAttribute once native returns - see
                         GeoMeshBuilder for how it's consumed). ApplyGroup first buckets a position's corners by
                         their exact (quantized) face normal - any two corners sharing one trivially pass the angle
                         test regardless of threshold (UnionIdenticalNormals), so only one representative per
                         distinct direction needs the real O(m^2) pairwise test. Matters when a position is touched
                         by many near-identical faces (typically ArrayNode duplicating geometry onto itself) - m
                         stays the position's real orientation diversity instead of blowing up to corners^2 over
                         however many duplicates happen to sit there. Unlike WeldNode's port, points here only ever
                         GROW (every duplicated corner is a brand new point), so Run seeds growable lists from the
                         input rather than working over fixed-size spans, and the native ABI sizes every output
                         buffer to data.PointCount + (sum of every primitive's length) - the worst case where
                         literally every corner duplicates its point - since there's no tight upper bound otherwise.
                         The managed original's GeoGraph.LogCacheStats timing/Debug.Log instrumentation didn't come
                         along in the port - Editor-only profiling output, orthogonal to the algorithm's result
  TubeNode.cs            thin wrapper around NodraNative.Tube - see Native/NodraCore/Tube.cs for the actual
                         algorithm: sweeps a polygon cross-section along a points-only path (rotation-minimizing
                         frame via the double reflection method, so it doesn't twist) - beam at low Sides, tube at
                         high ones. Every count (rings, caps, side quads) is fully deterministic from path.Count/
                         Sides/Closed/CapStart/CapEnd, so NodraNative.Tube sizes every buffer exactly rather than a
                         worst-case bound, and recovers which PATH point each output point derives from (for
                         CopyAttributes) by replaying Tube.cs's own fixed ring/cap layout instead of needing that
                         reported back over the wire
  ArrayNode.cs           thin wrapper around NodraNative.ArrayModifier - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/ArrayModifier.cs: appends Count copies, each transformed by
                         Offset/Rotation*copy around Pivot plus a RadialOffset rotated along with it (constant
                         local vector -> walks around a circle). Rotation composes exactly like Unity's own
                         Quaternion.Euler (extrinsic Z, then X, then Y - R = Ry*Rx*Rz), confirmed against Unity's
                         own documented example rather than assumed - see ArrayModifier.cs's own comment. Sparse
                         named attributes (GeoData's private per-point float lists) have no native-side concept, so
                         NodraNative.ArrayModifier calls GeoData.CopyAttributes(newIndex, newIndex % sourceCount)
                         itself after the native call returns, matching ArrayNode.Process's original
                         CopyAttributes(newIndex, i) call exactly
  VertexColorNode.cs     Mode: Flat fills GeoData.Colors with one Color; Gradient evaluates a Gradient by Height
                         (position along Axis, normalized to the input's own extent), Slope (up . normal), or
                         Bounds (same remap as Height but against a Bounds box instead of the input's own extent,
                         so it doesn't reshuffle when upstream geometry changes). Bounds mode has InputCount 2 - port
                         0 "In" is the usual geometry, port 1 "Bounds" is optional: connected and non-empty, its
                         AABB (GeoData.GetBounds) is used instead of the Bounds field, so one upstream box (typically
                         *GeneratorNode.cs's Box, through a TransformNode so it can move/scale independently) can
                         drive several consumers at once; unconnected, the field is used exactly as if the port
                         didn't exist (ResolveBounds). A point is handled per ClampOutsideBounds: false (default)
                         skips it entirely, leaving its color untouched, UNLESS it's genuinely inside Bounds -
                         checked via Bounds.Contains across all three dimensions, not just whether Axis's own
                         projection lands in [0, 1] (a point can sit squarely within Bounds' extent along Axis while
                         being nowhere near the box in either of the other two axes); true pins every point to
                         whichever end of Axis it overshot instead, ignoring containment entirely (the only option
                         before that field existed) - matters most when Bounds doesn't overlap the mesh at all,
                         where clamping alone would land every point on the exact same Gradient end and look like a
                         deliberate flat fill instead of "nothing here is really inside Bounds". A graph saved
                         before this field existed deserializes it as false too, same as a freshly created node -
                         the one behavior change existing Bounds-mode graphs see. Blend
                         combines the result with each point's incoming Colors value: Default replaces it,
                         Multiply/Add/Subtract combine per-channel (including alpha), unclamped. Gradient mode's
                         per-point [0, 1] blend factor (Slope/Height/Bounds) is computed in Native/NodraCore/
                         VertexColor.cs - Gradient.Evaluate itself always stays on the Unity side (a Gradient's own
                         color/alpha keys and color-space handling are Unity-only, nothing for native to
                         understand). Flat mode never needed native at all, so only Gradient mode's own Warning/
                         availability gate applies - same partial-dependency shape as DeletePointsNode
  MirrorNode.cs          thin wrapper around NodraNative.Mirror - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/Mirror.cs: appends a reflected copy across an axis/Offset plane, point
                         order reversed (a reflection is orientation-reversing) - a point already on the plane is
                         reused, not duplicated. NewPoints (the genuinely-off-plane ones) aren't deterministically
                         sized, so NodraNative.Mirror allocates a safe upper bound (sourcePointCount) and reads
                         back NewPointCount; Remap's own values already sit in Mirror.cs's own combined index space
                         (untouched original index, or sourcePointCount + k meaning the k-th NewPoints entry) -
                         appending NewPoints straight after data's own pre-existing points lines that space up with
                         data's real indices automatically, same trick Extrude uses, so the mirrored primitives
                         need no further remapping at all. Sparse named attributes have no native-side concept
                         (same as every wrapper here) - since Remap only records old-index -> new-index,
                         NodraNative.Mirror builds the inverse mapping itself in one pass before calling
                         CopyAttributes(newIndex, originalIndex) for each genuinely new point
  FlipNormalsNode.cs     thin wrapper around NodraNative.FlipNormals - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Native/NodraCore/
                         FlipNormals.cs reverses every primitive's own point order and negates every normal
  WeldNode.cs            thin wrapper around NodraNative.Weld - no managed fallback (same pattern as DecimateNode's
                         optional package: Warning explains why when NodraNative.IsAvailable is false, Process()
                         passes geometry through unchanged instead). The actual algorithm lives in Native/NodraCore/
                         Weld.cs: merges points within Distance transitively (union-find over a spatial hash of
                         cells sized Distance), averaging position/normal/UV/color. RemapPrimitives.
                         CollapseAdjacentDuplicates collapses an ADJACENT (cyclically - the closing edge counts)
                         welded-together corner pair down to just the shared corner instead of dropping the whole
                         primitive - a cone's side quad losing its degenerate (radius-0) apex ring down to one
                         shared tip point still leaves a valid triangle, and naively dropping the whole quad instead
                         (the pre-native behavior) took the cone's entire side wall with it once its tip got welded
                         shut, leaving only whatever didn't touch that ring (its caps) - see NodraCore.Tests' own
                         regression test for this exact cone/apex case. Only a REMAINING non-adjacent duplicate
                         after collapsing (a self-intersecting "bowtie" - two opposite corners of the same primitive
                         welded together) or fewer than 3 surviving corners still drops the primitive entirely
  Axis3D.cs              X/Y/Z axis picker shared by MirrorNode, TaperNode, BendNode, TwistNode
  TaperNode.cs           thin wrapper around NodraNative.Taper - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Native/NodraCore/Taper.cs
                         scales the cross-section perpendicular to Axis, Lerp(1, Factor, t) across the input's
                         own Axis extent - same axis-index convention (X=0, Y=1, Z=2) and Get/degenerate-range
                         guard shape as Bend.cs below
  BendNode.cs            thin wrapper around NodraNative.Bend - no managed fallback (same pattern as DecimateNode's
                         optional package: Warning explains why when NodraNative.IsAvailable is false, Process()
                         passes geometry through unchanged instead). The actual algorithm lives in Native/NodraCore/
                         Bend.cs: curves the input into a total-Angle arc across its Axis extent - bends into the
                         next axis in the X -> Y -> Z -> X cycle, third axis untouched (radius = extent / angle).
                         Points-only in and out, like NoiseDisplaceNode
  TwistNode.cs           thin wrapper around NodraNative.Twist - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Native/NodraCore/Twist.cs
                         rotates every point around Axis by Angle * t, t from the input's own Axis extent - a
                         plain per-point rotation (Quaternion.CreateFromAxisAngle, the same standard formula
                         RandomTransformNode's own port uses - no ambiguity like Quaternion.FromToRotation's
                         antiparallel case), so it keeps normals in sync itself (unlike Taper/Bend)
  RelaxNode.cs           thin wrapper around NodraNative.Relax - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/Relax.cs: Laplacian smoothing - Iterations passes of Lerp(point,
                         neighbor average, Factor) over index-based edge adjacency (see ExtrudeNode's boundary-edge
                         definition, reused here too). Points-only in and out, like NoiseDisplaceNode/BendNode
  SubdivideNode.cs       thin wrapper around NodraNative.Subdivide - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/Subdivide.cs: splits every primitive into one quad per corner around its
                         own centroid, Iterations times (all in one native call, since each iteration's own output
                         feeds the next one's input directly) - the topological half of Catmull-Clark (no
                         smoothing); an edge midpoint is cached by point-index pair, so index-shared edges stay
                         welded and per-face-duplicated ones don't. New points aren't deterministically sized
                         (depends how many edges get their midpoint reused across iterations), but the FINAL
                         primitive count/shape always is - every corner of every primitive becomes exactly one
                         quad, so NodraNative.Subdivide replicates that same exact recurrence (not just a bound) to
                         size Primitives precisely, and only over-allocates Points/Normals/Uvs/Colors and the
                         BlendSources bookkeeping array as a safe upper bound. Sparse named attributes have no
                         native-side concept (same as every wrapper here) - a midpoint's 2-source 0.5-weight blend
                         and a centroid's N-source uniform blend are mathematically the exact same operation, so
                         NodraNative.Subdivide reads back one BlendSources entry per new point (2 or N source
                         indices) and calls the single GeoData.BlendAttributes(index, sources) overload for both
  CapHolesNode.cs        thin wrapper around NodraNative.CapHoles - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/CapHoles.cs: walks each open boundary loop (ExtrudeNode's boundary-edge
                         definition again) via a from->to map and fans an N-gon across it, reversed - see the file
                         for the hand-derivation. Purely combinatorial (int indices only, no floating-point math at
                         all) and purely additive (only ever appends new cap primitives, never touches an existing
                         one), so - unlike every other native port here - there's no floating-point fidelity nuance
                         to call out at all
  SliceNode.cs           thin wrapper around NodraNative.Slice - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Invert/Normal.normalized
                         resolution stays Unity-side (a plain flag flip, not part of the algorithm itself) - see
                         Native/NodraCore/Slice.cs for the actual Sutherland-Hodgman clip against one plane
                         (Center/the already-resolved Normal), keeping the half Normal points at - a lighter
                         BooleanNode/GeoCsg alternative for a flat cut. A crossed edge's new point is cached by its
                         two ORIGINAL point indices (unordered), so two primitives sharing that edge resolve to the
                         identical new point instead of two merely coincident ones a float hair apart - without
                         that, Cap's delegation straight to CapHolesNode.Process() (same generic boundary walk, no
                         separate reimplementation to drift out of sync) would see a seam of microscopic gaps
                         instead of one closed loop. New cut points aren't a uniform average like Subdivide's own
                         BlendSources (a t-weighted lerp between exactly 2 sources, t varies per cut) - Slice.cs
                         reports CutFrom/CutTo/CutT instead, parallel to NewPoints, straight into
                         GeoData.BlendAttributes(index, from, to, t) on the Unity side
  FaceFilterNode.cs      thin wrapper around NodraNative.FaceFilter - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). The actual algorithm lives in
                         Native/NodraCore/FaceFilter.cs: drops primitives whose face normal falls outside MaxAngle
                         of Direction (Invert flips it). Points never change - only which primitives survive - so
                         NodraNative.FaceFilter sizes its output the same way Weld does (data's own current counts
                         are always a safe upper bound, since filtering only ever drops whole primitives)
  RemoveUnusedPointsNode.cs   thin wrapper around NodraNative.RemoveUnusedPointsUsed - no managed fallback (same
                         pattern as DecimateNode's optional package: Warning explains why when NodraNative.
                         IsAvailable is false, Process() passes geometry through unchanged instead). Only the
                         `used` flags cross the boundary (see Native/NodraCore/RemoveUnusedPoints.cs's own doc
                         comment) - the actual point removal (GeoData.CompactPoints) and primitive remap both stay
                         Unity-side regardless, since native has no concept of GeoData's own sparse attribute
                         dictionary. Drops every point no primitive references - the cleanup FaceFilterNode/
                         SliceNode both leave for a following node instead of doing themselves
  DeletePointsNode.cs    drops points whose named attribute falls INSIDE AttributeRange (Invert flips it - drops
                         those outside instead) or a Random fraction, then any primitive that referenced one of
                         them (GeoData.CompactPoints again). Deliberately just thresholds/rolls dice rather than
                         reimplementing Height/Slope/Bounds/Noise a third time (VertexColorNode and SetAttributeNode
                         already have them) - chain a SetAttributeNode first to compute whichever of those into a
                         name, then delete by it here. Works on a points-only cloud too (ScatterNode's output) for
                         thinning before CopyToPoints. Only Random mode depends on NodraCore (Native/NodraCore/
                         DeletePoints.cs, NodraNative.DeletePointsRandom) - Attribute mode is a plain per-point
                         comparison against GeoData's own private sparse-attribute storage with nothing for a
                         native port to add, so Warning/the native gate only apply when Mode == Random, unlike
                         every other node in this list. A seeded System.Random is safe to reproduce in NodraCore
                         unlike Mathf.PerlinNoise: starting with .NET 6, an explicitly seeded Random deliberately
                         keeps the pre-.NET-Core-3.0 algorithm for backward compatibility (only the parameterless
                         constructor moved to a new one), and Unity's own Mono/IL2CPP runtime has always used that
                         same original algorithm - confirmed against .NET's own breaking-change notes, not assumed
                         (see DeletePoints.cs's own doc comment)
  AutoUVNode.cs          thin wrapper around NodraNative.AutoUV - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Triplanar/Spherical/
                         Cylindrical UV projection - not a real unwrap; the actual algorithm lives in Native/
                         NodraCore/AutoUV.cs. Triplanar never changes point count, just overwrites Uvs per
                         primitive's own face normal (Newell's method, axis-snapped). Spherical/Cylindrical fold
                         each point's atan2 longitude into (-90°, 90°] (HalfAngle) so ordinary same-side primitives
                         agree closely; a primitive whose corners still land >90° apart after folding (crossing the
                         engineered ±90°-longitude seam) gets a private duplicate of just the offending corner(s),
                         unwrapped by ±180° - same growable-output/mutate-primitives-in-place shape as
                         SmoothByAngleNode (see NodraNative.cs's own entry). A duplicated corner's own color/
                         attributes are NOT carried over (defaults to white, matching AutoUVNode's original 3-arg
                         AddPoint call) - not something the port changes, just faithfully reproduces
  UVTransformNode.cs     thin wrapper around NodraNative.UVTransform - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Native/NodraCore/
                         UVTransform.cs rotates (around UV-space center (0.5, 0.5)) then tiles (from the origin,
                         matching a Material's own Tiling field) then offsets every point's UV
  SetAttributeNode.cs    writes a per-point float into a named GeoData attribute (the same GeoData.SetAttribute/
                         GetAttribute mechanism SmoothByAngleNode uses internally for SmoothGroupAttribute, exposed
                         generically here) - Mode mirrors VertexColorNode's Gradient sources (Height/Slope/Bounds,
                         same Axis3D/Bounds fields) plus Noise (Perlin off XZ position) and Random (seeded, no
                         spatial coherence). Height/Slope/Bounds reuse NodraNative.VertexColorHeightT/SlopeT/
                         BoundsT directly - byte-for-byte the same raw [0, 1] factor formulas, so no reason to
                         duplicate them; Noise/Random get their own small native functions instead (Native/
                         NodraCore/SetAttribute.cs) since nothing else already computed those - Noise reuses
                         NoiseDisplace.PerlinNoise2D's own from-scratch gradient noise substitute (see that file's
                         own doc comment for why Mathf.PerlinNoise couldn't be faithfully ported: a graph already
                         using Noise mode will look visibly different once native takes over, same deliberate
                         tradeoff as NoiseDisplaceNode's own Perlin option). Remap/Blend/SetAttribute all stay
                         Unity-side regardless (native has no concept of GeoData's own sparse attribute
                         dictionary) - only Constant mode needs no native at all; every other mode has no managed
                         fallback (same pattern as DecimateNode's optional package: Warning explains why when
                         NodraNative.IsAvailable is false, that mode writes nothing instead). Remap rescales the
                         mode's natural [0, 1] to an arbitrary range before
                         Blend (same Default/Multiply/Add/Subtract as VertexColorNode) combines it with whatever
                         was already there - a name nothing has written yet reads back 0 (GeoData's own default),
                         so Multiply/Subtract as the FIRST write to a name always zeroes it; Add/Default only there.
                         Bounds mode's ClampOutsideBounds (default false) matches VertexColorNode's own field of the
                         same name and meaning, "outside" meaning Bounds.Contains across all three dimensions - not
                         just whether Axis's own projection lands in [0, 1] - so skips a point by default instead of
                         pinning it to the nearest end, leaving its attribute value untouched rather than every
                         out-of-reach point landing on the exact same edge value (surprising fed into e.g.
                         DeletePointsNode's own AttributeRange, since it can look like every point is uniformly
                         in/out of range). Bounds mode also has InputCount 2, same optional port-1 "Bounds" override
                         as VertexColorNode (ResolveBounds) - see that node's entry, identical mechanism here
  ScatterNode.cs         thin wrapper around NodraNative.Scatter - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() produces no geometry instead). The actual algorithm lives in Native/
                         NodraCore/Scatter.cs: scatters a fixed number of points across a surface, area-weighted
                         per triangle, sqrt-first uniform barycentric sampling within it. A seeded System.Random
                         drives the triangle pick and barycentric roll, same cross-runtime-safe reasoning as
                         DeletePointsNode's own Random mode (see that entry). Fully deterministic output sizing
                         (always exactly PointCount) - sparse named attributes have no native-side concept (same as
                         every wrapper here), so NodraNative.Scatter reads back each point's own sampled triangle/
                         barycentric weights and calls GeoData.BlendAttributesFrom itself, matching
                         ScatterNode.Process's own call exactly
  CopyToPointsNode.cs    thin wrapper around NodraNative.CopyToPoints - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() produces no geometry instead). The actual algorithm lives in Native/
                         NodraCore/CopyToPoints.cs: stamps a mesh at each point, aligning/jittering/scaling it.
                         ScaleAttribute (empty by default) multiplies a named attribute's own value (fallback 1,
                         not GetAttribute's own 0 - an unset point stamps at whatever UniformScaleRange alone would
                         have given it, not a collapsed zero-scale point) into that per-copy roll - read once per
                         INPUT point, not per SourceMesh vertex, so e.g. a Density SetAttributeNode painted before
                         ScatterNode (whose BlendAttributesFrom already carries it onto the scattered points) can
                         size what gets stamped at each one without a second, bespoke node; resolved into a plain
                         per-point multiplier array by NodraNative.CopyToPoints itself (same Unity-side-only
                         sparse-attribute handling every other wrapper uses) rather than crossing the wire as a
                         name. mesh.vertices/normals/uv/triangles are read here too (a UnityEngine.Mesh can't cross
                         the native boundary) and padded to a uniform per-vertex length exactly like the original's
                         own inline fallback did, so Native/NodraCore/CopyToPoints.cs never needs a bounds check of
                         its own. AlignToNormal is the one non-bit-identical piece: Unity's own
                         Quaternion.FromToRotation is a native engine call ("FromToQuaternionSafe", confirmed via
                         UnityCsReference's Math.bindings.cs - the same class of gap as Mathf.PerlinNoise), so
                         native reproduces the one mathematically correct answer for every normal EXCEPT the exact
                         antiparallel-to-Up direction (straight down - common enough for a flat downward-facing
                         patch to hit for real, not just a theoretical edge case), which has no unique answer at
                         all; native picks a fixed world-Z 180-degree flip there instead of guessing at Unity's own
                         unobservable tie-break - a narrow, deliberate, documented difference (see
                         CopyToPointsNode.cs's own doc comment), not a silent approximation. A seeded System.Random
                         drives the yaw/scale rolls, same cross-runtime-safe reasoning as DeletePointsNode's own
                         Random mode (see that entry)
  RandomTransformNode.cs thin wrapper around NodraNative.RandomTransform - no managed fallback (same pattern as
                         DecimateNode's optional package: Warning explains why when NodraNative.IsAvailable is
                         false, Process() passes geometry through unchanged instead). Native/NodraCore/
                         RandomTransform.cs jitters position (+/- PositionJitter per axis) and, past 0, tilts the
                         normal by a random angle (AngleJitter) around a uniformly-random axis (z/theta
                         parametrization, not three independent per-axis angles - those cluster at the poles) -
                         two independent System.Random streams (position/angle), same cross-runtime-safe
                         reasoning as Scatter.cs's own seeded Random - fills the gap CopyToPointsNode's own jitter
                         (spin + scale) leaves between ScatterNode and it: stamped copies otherwise sit exactly on
                         the point and stay exactly aligned to its normal. Works on a full mesh too - independent
                         per-point jitter (no spatial coherence, unlike NoiseDisplaceNode) reads as a
                         rougher/shattered look instead of NoiseDisplaceNode's rolling one
  MergeNode.cs           InputCount 2 ("Base"/"Branch") - thin wrapper around NodraNative.Merge, which mutates
                         Base in place (appends Branch's own points/normals/uvs/colors after Base's, Branch's
                         own primitive indices shifted by Base's original point count - nothing welded, same as
                         Native/NodraCore/Merge.cs's own Append), same "mutate the passed-in GeoData" shape as
                         Bend/Taper/CapHoles above rather than allocating a new one. No managed fallback for a
                         genuinely connected Branch (same pattern as DecimateNode's optional package: Warning
                         explains why when NodraNative.IsAvailable is false, Branch is dropped instead) - an
                         unconnected Branch never needed native at all, same partial-dependency shape as
                         BooleanNode below
  BooleanNode.cs         InputCount 2 ("A"/"B") - real CSG Union/Subtract/Intersect via NodraNative.Csg (see
                         Native/NodraCore/GeoCsg.cs for the actual BSP tree). One side unconnected still works
                         without native (booleaning against nothing neither adds nor removes anything for Union/
                         Subtract, has nothing in common with anything for Intersect) - only a real boolean
                         between two connected inputs needs it, same partial-dependency shape as DeletePointsNode.
                         TimeoutException still reaches ProceduralMeshGenerator's catch clause exactly as before -
                         NodraNative.Csg re-throws it on the Unity side from a plain TimedOut flag on the wire,
                         since an exception can't cross the native/managed boundary itself
  GeoMeshBuilder.cs      GeoData -> Unity Mesh (fan-triangulates, always recalculates normals - RecalculateNormals
                         works per vertex index and weights by triangle area, neither of which SmoothByAngleNode
                         can rely on: it can't merge a UV seam's position-duplicate indices, and it returns exactly
                         Vector3.zero for a vertex whose only triangle is degenerate (a pole/apex quad's fan always
                         has one - see SmoothByAngleNode above). SmoothGroupAttribute-tagged points take
                         GeoData.Normals directly instead - SmoothByAngleNode already computed it correctly there
  NodraNative.cs         P/Invoke boundary into the NodraCore native library (Native/NodraCore at the repo root,
                         outside this package entirely - built separately via NativeAOT, never compiled by Unity;
                         Native/NodraCore.Tests there, a plain non-AOT console app referencing NodraCore by
                         ProjectReference, is where the actual algorithms get iterated on and checked in seconds
                         via `dotnet run` - see agents.md's own build notes below for why that matters.
                         Native/NodraCore.Viewer, a sibling console app there built the same way but pulling in the
                         Raylib-cs NuGet package, renders Weld/SmoothByAngle's own output live in its Pipeline mode
                         (default), or GeoCsg's Union/Subtract/Intersect against a Box(A)/offset-Sphere(B) pair in
                         its Boolean mode ([M] to switch) - proof this is a genuinely standalone .NET library, not
                         something that only happens to compile outside Unity: it has no Unity reference at all,
                         only NodraCore by ProjectReference). Ships for
                         desktop Editor AND Standalone (Windows/macOS/Linux) - see Plugins/ below, both platform
                         checkboxes need ticking, not just Editor, so a Player build gets the real implementation
                         too. IsAvailable probes once via a throwaway Ping() call and caches false on
                         DllNotFoundException/EntryPointNotFoundException/BadImageFormatException, so a missing or
                         wrong-platform binary degrades to "unavailable" instead of crashing - EVERY node whose own
                         agents.md entry says "thin wrapper around NodraNative.X" has no managed fallback of its
                         own for this (same contract as DecimateNode's optional package: Warning explains why - a
                         modifier passes geometry through unchanged, a generator produces none at all, since
                         there's no "existing geometry" for a 0-input node to fall back to). DeletePointsNode,
                         VertexColorNode, BooleanNode, MergeNode and SetAttributeNode are partial exceptions - see
                         each one's own entry for which part of it doesn't depend on this at all.

                         Native/NodraCore.Graph (plus its own NodraCore.Graph.Tests) is a further step in the same
                         "genuinely standalone" direction NodraCore.Viewer above already demonstrates - a real
                         graph, no Unity AND no Assets/Nodra dependency either: GeoNode/GeoGraph/GeoData in THIS
                         package are Unity-coupled at their core (JsonUtility/[SerializeReference] for graph
                         (de)serialization, UnityEngine.Vector3/Color/Bounds for every point), not just at the
                         edges, so reusing them outside Unity was never realistic - see that project's own
                         top-level doc comment. Its own GeoData is a trimmed analog (same Points/Normals/Uvs/
                         Colors/Primitives/AddPoint/AddPrimitive/GetBounds shape every NodraCore.*.Run slots into
                         with no adapter, no named per-point attribute dictionary at all since nothing there ever
                         writes one - a node that would otherwise call CopyAttributes/BlendAttributes just skips
                         it instead), its GraphNode/Graph a trimmed analog of GeoNode/GeoGraph (Id/InputCount/
                         Process(GeoData[]) survive, Position/Warning/Category/JsonUtility-hash-based resultCache
                         don't - no "skip if nothing changed" editor optimization needed outside an editor),
                         evaluated via a per-Evaluate()-call memoized recursive walk with its own cycle guard.
                         Covers every node whose algorithm is pure geometry (all 10 generators, Weld/
                         SmoothByAngle/Bend/Twist/Taper/Transform/RandomTransform/NoiseDisplace/Mirror/Relax/
                         Subdivide/Extrude/Chamfer/CapHoles/FaceFilter/AutoUV/UVTransform/FlipNormals/Slice/
                         ArrayModifier/RemoveUnusedPoints/DeletePoints (Random only)/Scatter/Merge/Boolean) -
                         deliberately excludes CopyToPoints (its own "stamp a Mesh" shape needs a source-
                         triangulation adapter this first pass doesn't attempt yet), VertexColor/SetAttribute
                         (fundamentally need a Gradient/attribute-dictionary equivalent), and Decimate/
                         HeightMapGenerator (already Unity-only for reasons that apply just as much here - see
                         their own entries above). Graph itself (de)serializes to/from JSON via System.Text.Json's
                         built-in polymorphic support (see Graph.cs's own [JsonDerivedType] list on GraphNode) -
                         Nodes/Edges are real SETTABLE properties there, not get-only ones backed by an already-
                         instantiated list the way this file's own comment above might suggest GeoData works:
                         confirmed by direct experiment that System.Text.Json's reflection-based deserializer does
                         NOT populate an existing collection in place for a get-only property (it comes back
                         silently empty instead), unlike what's sometimes assumed - see Graph.cs's own comment on
                         the two properties for that fix. NodraCore.Viewer's own Graph mode ([M] to cycle into it,
                         or start the Viewer with a JSON path as its command-line argument) loads a saved graph
                         via Graph.LoadFromFile, evaluates it, and renders whatever GeometryOutputNode produces -
                         [L] reloads the same file, so editing the JSON externally and pressing that key is the
                         whole iteration loop. Every failure (missing file, bad JSON, no GeometryOutputNode, a
                         cycle, a real BooleanNode timeout) is caught and shown in the status line/side panel
                         rather than crashing the Viewer. [E] (Graph mode only, needs a JSON path already) opens
                         NodraCore.Viewer/GraphEditor.cs - a hand-rolled node-graph editor on top of the RayGui
                         bindings, since raygui itself has no node-graph widgets at all: every node box, port
                         dot, drag interaction and connection curve is plain raylib drawing plus manual hit-
                         testing. Topology only for now (drag nodes, drag-connect ports, right-click to spawn any
                         of the 36 node types found by reflection over GraphNode's own assembly, Del to remove a
                         selected node/edge, Ctrl+S/Save button writes straight back to the same JSON path) - no
                         per-node field editing yet (Size/Distance/Axis/... still need typing into the JSON by
                         hand). Edits the SAME Graph/GraphNode objects Evaluate() itself would run on, no
                         translation layer - GraphNode.Position (canvas coordinates) and HasOutput (false only
                         for GeometryOutputNode) exist specifically so the editor and the evaluator can share
                         them with nothing node-editor-specific bleeding into GraphNode itself beyond those two.
                         Closing the editor marks the 3D view dirty so it re-reads the file fresh, picking up
                         whatever got saved - same one-way "edit, then reload" flow as external editing, just
                         with the edit itself happening in-app now too. NodraCore.Viewer/UiStyle.cs is the one
                         place both Program.cs's own viewer chrome and GraphEditor.cs share: Program.cs's window
                         grew from 1280x800 to 1920x1080, and TWO separate multipliers fill that extra room (the
                         3D viewport itself is untouched either way, only the flat 2D chrome on top of it) - Scale
                         (1.5x, via UiStyle.R/F) for box/panel/button geometry and margins, and a more aggressive
                         FontScale (2x, via UiStyle.FontSize) for actual TEXT specifically, both this project's
                         own status/help/node-title text (UiStyle.Text, a DrawText-shaped wrapper around
                         DrawTextEx) AND raygui's own internal button/label/group-box-title text (via
                         ApplyTextSize, reasserted every frame - see that method's own doc comment for why).
                         Two multipliers instead of one because text and box geometry read very differently: a
                         proportional increase made boxes plenty bigger while leaving the text inside them barely
                         changed. GraphEditor's own NodeWidth got a small, additional, non-proportional bump (170
                         -> 210 before Scale) so the longest node type names still fit their own header at
                         FontScale's bigger title text. Also loads a real TTF font (Segoe UI, from its usual
                         Windows path, falling back to raylib's own tiny built-in bitmap font if that path
                         doesn't exist) for both of the above via RayGui.SetFont. Deliberately NOT applied to
                         UiGallery.cs - a dedicated "one example of every raw RayGui control" demo screen with
                         its own separate purpose, not part of the actual viewer/editor workflow either of these
                         were asked for. Native/NodraCore.Native.sln (a hand-added solution file, sibling to the
                         5 projects it lists) exists purely so Rider/VS resolve every ProjectReference here
                         without needing NodraCore.Viewer.csproj's own extra `<Reference>`+HintPath at a built
                         NodraCore.Graph.dll (added by hand for the same reason, before this solution existed) -
                         open the .sln instead of a loose .csproj for full cross-project navigation.

                         Every *Input/*Output field is
                         IntPtr, and every buffer is pinned via GCHandle rather than `fixed` - deliberately staying
                         in fully "safe" C# so this file never needs AllowUnsafeCode on Nodra.asmdef; the native
                         side's own mirror structs (Native/NodraCore/NativeExports.cs) are free to use real typed
                         pointers instead; LayoutKind.Sequential blits the two identically either way. Weld's own
                         output buffers are all sized to data.PointCount/data.Primitives.Count - welding only
                         merges points together or drops whole degenerate primitives, so the input's own counts
                         are always a safe upper bound and no "count first, allocate, fill" round trip is needed.
                         SmoothByAngle's output buffers instead need data.PointCount + (sum of every primitive's
                         length), since points there only ever grow - see SmoothByAngleNode.cs's own agents.md
                         entry. SmoothByAngle also mutates data.Primitives' own arrays IN PLACE via Array.Copy back
                         from the same flattened buffer the native call wrote into (shape/length never changes,
                         only some corners' index values) - Weld doesn't have this because dropped/shrunk
                         primitives mean its own output has to be a wholesale replacement instead.
                         All five generators (Box/Sphere/Grid/Torus/Cylinder) have no existing GeoData to read at
                         all (0-input nodes) - a shared GeneratorOutput struct/RunGenerator/AppendGeneratedPoints
                         trio does the common part (pin Points/Normals/Uvs/PrimitiveIndices buffers, call native,
                         AddPoint every returned point onto data, offset by data's own current PointCount -
                         matching the managed originals' `input ?? new GeoData()` + running point-count-offset
                         pattern exactly). Only turning the flattened PrimitiveIndices buffer back into
                         GeoData.Primitives entries differs per generator: AppendQuadPrimitives handles the four
                         that are ALL 4-index quads; CylinderGenerator reads side quads then up to two 3-index
                         cap-fan triangle blocks itself, since it's the one generator whose primitives aren't a
                         uniform length (see its own agents.md entry above for why that doesn't need a
                         PrimitiveLengths array on the wire).
                         NoiseDisplace and Bend are the simplest wrappers here - points only ever move (along
                         their own normal/+Y for NoiseDisplace, within the Axis plane for Bend), Normals/Uvs/
                         Colors/Primitives never change, so each sends Points(+Normals) in and reads back only a
                         same-length Points array, no counts or primitive handling needed at all. noiseType/axis
                         cross the wire as plain ints (NoiseDisplaceNode.NoiseType/BendNode.Axis3D cast to int)
                         rather than this file referencing either node's own nested enum type, keeping it as
                         node-agnostic as every other wrapper here.
                         ArrayModifier is fully deterministic sizing (sourceCount * Count points/uvs/colors,
                         sourcePrimitiveCount * Count primitives, every copy's primitives the exact same shapes as
                         data's own, just index-shifted by that copy's point offset) - no native-side clamp to
                         replicate at all, just the same Mathf.Max(1, Count) ArrayNode.Process already applies.
                         Offset/Rotation/RadialOffset/Pivot cross the wire as flat X/Y/Z floats on the input struct
                         (same LayoutKind.Sequential blitting as every other struct here) rather than a nested
                         Vector3-shaped struct. Sparse named attributes have no native-side concept (same as every
                         wrapper here) - ArrayModifier() calls GeoData.CopyAttributes(newIndex, newIndex %
                         sourceCount) itself once the native call returns, replicating ArrayNode.Process's own
                         per-copy CopyAttributes call.
                         AutoUV is shaped like SmoothByAngle - points only ever GROW (a wrapped projection's seam
                         correction duplicates a corner), so its output buffers are sized to data.PointCount + (sum
                         of every primitive's length) and PrimitiveIndices is mutated in place the same way. Unlike
                         SmoothByAngle it doesn't touch Colors at all: existing points keep whatever color they had
                         (untouched buffer), and AutoUV() pads Colors with white for however many new points came
                         back, matching AutoUVNode.ApplyWrapped's own 3-arg AddPoint call (see AutoUVNode.cs's own
                         entry above).
                         CapHoles is purely additive and purely combinatorial (no points/normals/uvs/colors on the
                         wire at all, just Primitives in and NEW Primitives out) - output buffers are sized to
                         data's own total index count, a safe (if loose) upper bound since every new cap
                         primitive's corners come from data's own boundary edges.
                         CircleGenerator reuses the shared GeneratorOutput/RunGenerator/AppendGeneratedPoints trio
                         every other 0-input generator does, plus a new AppendTrianglePrimitives (3-index sibling
                         of AppendQuadPrimitives) since a filled circle's fan is triangles, not quads.
                         FaceFilter is shaped exactly like Weld's own sizing story (drops-only, never grows) but
                         simpler - only Primitives change, Points/Normals/Uvs/Colors are never even sent across.
                         Extrude appends TopPoints/TopNormals/TopUvs straight onto data via AddPoint + a
                         CopyAttributes(topIndex, i) per point (matching ExtrudeNode.BuildOffsetPoints's own call) -
                         and then needs NO remapping for the Primitives it reads back, since Extrude.cs's own
                         combined index space already lines up with the real GeoData indices those AddPoint calls
                         just produced (see ExtrudeNode.cs's own agents.md entry for that space's exact shape).
                         Chamfer is the most involved wrapper in this file (see ChamferNode.cs's own entry for the
                         full sizing/CopyAttributes story) - it's also the only one that reads data.Primitives
                         AFTER the native call returns (to replay the same primitive-major order Chamfer.Run
                         walked for its own CopyAttributes bookkeeping) and only clears/replaces it afterward.
                         CopyToPoints is the only wrapper that returns a brand-new GeoData rather than mutating the
                         one it's given (matching CopyToPointsNode.Process's own `new GeoData()`), and the only one
                         that reads a UnityEngine.Mesh directly (mesh.vertices/normals/uv/triangles, padded to a
                         uniform per-vertex length here since Native/NodraCore/CopyToPoints.cs assumes that and
                         never bounds-checks itself) - see CopyToPointsNode.cs's own entry for its
                         Quaternion.FromToRotation fidelity note and its ScaleAttribute resolution.
                         DeletePointsRandom is the simplest wrapper of all - a plain pointCount/chance/seed in, a
                         byte-per-point (0/1) keep-flags buffer out; see DeletePointsNode.cs's own entry for why
                         only its Random mode calls into this at all.
                         IcoSphereGenerator's sizing runs the same exact edge-count recurrence
                         (unique edges = 3*PrimitiveCount/2, one new point per unique edge per subdivision level)
                         as Native/NodraCore/IcoSphereGenerator.cs's own Run - see IcoSphereGeneratorNode.cs's own
                         entry.
                         Mirror is shaped like Weld for its deterministic pieces (Remap always exactly
                         sourcePointCount, MirroredPrimitiveIndices/Lengths always exactly the input's own shapes)
                         but like Extrude for its growable one (NewPoints sized to a sourcePointCount upper bound,
                         actual NewPointCount read back) - see MirrorNode.cs's own entry for the combined-index-
                         space trick and the CopyAttributes inverse-mapping pass this needs that Extrude doesn't.
                         Relax is the simplest wrapper here alongside NoiseDisplace/Bend - points only ever move,
                         Normals/Uvs/Colors/Primitives never change, so it sends Points+Primitives in and reads
                         back only a same-length Points array.
                         Scatter is the only wrapper alongside CopyToPoints that returns a brand-new GeoData rather
                         than mutating the one it's given (matching ScatterNode.Process's own `new GeoData()`) -
                         see ScatterNode.cs's own entry for the barycentric-weights-back BlendAttributesFrom story.
                         Subdivide has the most involved sizing story after Chamfer/Extrude: Points/Normals/Uvs/
                         Colors and the BlendSources bookkeeping array are worst-case bounds (accumulated by
                         replaying Subdivide.Run's own per-iteration recurrence in a loop before the call), but
                         Primitives is fully exact - see SubdivideNode.cs's own entry for why primitive count/shape
                         alone (unlike point count) never depends on how much edge-sharing the actual geometry has.
                         SplineGenerator is points-only, fully deterministic sizing (Mathf.Max(2, PointCount)) -
                         the normal (always Vector3.up) and UV (the same [0, 1] parametrization used for the
                         position sample) are computed on the Unity side rather than crossing the wire.
                         VertexColorSlopeT/HeightT/BoundsT never touch UnityEngine.Gradient at all - they only
                         return the per-point [0, 1] factor Gradient.Evaluate needs, which VertexColorNode.cs's own
                         ApplySlope/Height/Bounds call afterward; BoundsT also returns an Applies mask for
                         ClampOutsideBounds's skip case.
                         Tube's every count is exact, not a bound (see TubeNode.cs's own entry) - reuses the shared
                         GeneratorOutput struct like the 0-input generators, plus a CopyAttributes replay loop over
                         its own fixed ring/cap layout.
                         Csg is the one wrapper here whose native side owns the output buffers instead of Unity -
                         see BooleanNode.cs's own entry and Native/NodraCore/GeoCsg.cs for why a BSP split's
                         output size can't be known before the call. CsgOutput's Points/Normals/Uvs/
                         PrimitiveIndices/PrimitiveLengths are raw native pointers (NativeMemory.Alloc'd on the
                         other side), read out via Marshal.Copy rather than GCHandle-pinned managed arrays like
                         every other wrapper - the only "safe" (no AllowUnsafeCode) way to move bytes out of
                         unmanaged memory - and nodra_core_csg_free must be called exactly once afterward to
                         release them. TimedOut on the wire becomes a real, thrown TimeoutException here.
                         nodra_core_ping is a smoke test only (see Editor/NodraNativeMenu.cs), proving the
                         managed/native call boundary
  Plugins/
    Windows/x86_64/NodraCore.dll   built from Native/NodraCore (repo root - `dotnet publish -c Release -r win-x64`)
                                   - a genuine native PE with no CLR header/IL (confirmed via
                                   AssemblyName.GetAssemblyName throwing BadImageFormatException on it), so unlike
                                   a normal C# assembly it can't be opened in an IL decompiler. Only the Windows
                                   binary exists so far; osx-x64/osx-arm64/linux-x64 builds go under sibling
                                   Plugins/<Platform>/ folders the same way once needed - none of the NodraCore-only
                                   nodes have a managed fallback, so a platform with no binary here just means all
                                   of them degrade (Warning explains why per node) until one is built.
                                   Check the Plugin Inspector's platform checkboxes after Unity re-imports a freshly
                                   dropped binary - BOTH "Editor" and "Standalone" need to be ticked (CPU/OS matching
                                   whatever was actually built), since this is meant to ship inside a Player build
                                   too, not just power the Editor-time graph. Rebuilding this file while Unity has
                                   it loaded fails/silently doesn't take effect - Unity never unloads a loaded
                                   native plugin, so a rebuild needs the Editor closed first (see Native/
                                   NodraCore.Tests above for why most iteration shouldn't need this at all)
  Decimate/                one of two optional-dependency exceptions to Nodra's zero-dependencies rule (the other
                         is Editor/FbxExportUtility.cs) - a separate assembly, gated by a versionDefines symbol, so
                         the package being absent is a console warning, not a compile error
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
                           GeoData in, 3-point primitives out, same as Native/NodraCore/GeoCsg.cs's own ToResult
                           does for its BSP output
  ProceduralMeshGenerator.cs   MonoBehaviour: runs Graph.Evaluate(), bakes into the attached MeshFilter; Reset()
                               seeds a fresh Graph with one GeometryOutputNode (first add / Inspector "Reset").
                               Generate() calls Graph.ComputeOutputHash() FIRST (cheap - no Process(), no Clone())
                               and returns immediately if it matches the last bake's, skipping Graph.Evaluate()
                               itself (not just the GeoMeshBuilder.Build + sharedMesh assignment after it) -
                               OnValidate/AutoGenerate has no way to know which node's field just changed, so
                               without this every edit anywhere in the graph (including a node not wired into the
                               output at all) would still Clone() the entire reachable chain's result on every
                               single frame of e.g. a slider drag, for a mesh that provably isn't going to change
  Editor/
    Nodra.Editor.asmdef        editor-only assembly, references Nodra + Unity.Formats.Fbx.Editor
                               (com.unity.formats.fbx, Unity's own FBX Exporter package - install separately first);
                               versionDefines sets NODRA_FBX_EXPORT only while that package is actually installed -
                               see FbxExportUtility.cs. The only other optional dependency (Decimate/) gets a whole
                               separate assembly instead, because DecimateNode is a [SerializeReference] graph node
                               that has to always exist under the same name for old data to deserialize - nothing
                               here has that constraint, so gating directly in this already-existing assembly is
                               simpler and doesn't need its own
    FbxExportUtility.cs        wraps ModelExporter.ExportObject (UnityEditor.Formats.Fbx.Exporter) behind
                               NODRA_FBX_EXPORT - called unconditionally by ProceduralMeshGeneratorEditor/
                               GeoGraphAssetEditor's own "Export to FBX..." buttons, which get a real export when
                               the package is installed or an explanatory dialog when it isn't, instead of a
                               missing-type compile error either way. ExportGameObject hands the exporter an
                               existing scene GameObject as-is (ProceduralMeshGenerator always has one - its own
                               Transform/MeshRenderer/materials come along for free); ExportMesh is for a bare Mesh
                               with no GameObject of its own (GeoGraphAssetEditor's freshly-built preview mesh) -
                               stamps it onto a HideAndDontSave throwaway GameObject, since ModelExporter walks
                               GameObjects rather than accepting a Mesh asset directly, and discards that GameObject
                               again immediately after (success or failure)
    NodraGraphWindow.cs        EditorWindow: toolbar (target, Auto Generate, Generate) + NodraGraphView. Binds to
                               either a ProceduralMeshGenerator (BindGenerator) or a standalone GeoGraphAsset
                               (BindAsset) - only one owner at a time (the other field always nulled out by whichever
                               Bind* just ran). SetGenerateControlsVisible hides Auto Generate/Generate for the
                               GeoGraphAsset case - no baked Mesh to generate, so there's nothing for them to do.
                               OnOpenGeoGraphAsset ([OnOpenAsset]) opens a double-clicked GeoGraphAsset here directly,
                               same as GeoGraphAssetEditor's own "Open Graph Editor" button (NodraGraphWindow.OpenAsset).
                               DrawSelectedBoundsGizmos (SceneView.duringSceneGui) draws a translucent box for
                               whichever selected node's own Bounds field its Mode/Source combination actually
                               reads (VertexColorNode's Gradient+Bounds, SetAttributeNode's Bounds) - only while
                               such a node is selected in the graph, not permanently, and only for the
                               ProceduralMeshGenerator case (needs target.transform - a GeoGraphAsset has none).
                               TryGetActiveBounds prefers whatever's wired into that node's port-1 "Bounds" input
                               over the field itself - TryResolveBoundsPort walks Graph.Edges for a port-1 edge into
                               the selected node and reads the source's last-computed result via
                               GeoGraph.TryGetCachedResult (no evaluation from here; null/empty falls back to the
                               field, same as the node's own ResolveBounds at runtime). Update() force-repaints the
                               Scene view on a selection change (only then, not every tick) since selecting a node
                               happens in this window, not the Scene view, so nothing else would tell it to redraw.
                               BuildPreviewPanel/DrawPreviewGUI: a floating live mesh preview (bottom-right, below
                               Point Colors' top-right) via UnityEditor.MeshPreview - the same utility class the
                               Mesh asset Inspector's own preview uses, so orbit-drag/zoom/lighting all come for
                               free; only its .mesh needs keeping current. RefreshPreviewMesh does that every
                               Update() tick by comparing Graph.ComputeOutputHash() against what it last built
                               (cheap - same no-Process()/no-Clone() walk ProceduralMeshGenerator.Generate() already
                               uses), rebuilding via Graph.Evaluate() + GeoMeshBuilder.Build() only when it's moved.
                               Reacts to every edit (field slider drags included, which never reach
                               OnGraphViewChanged) EXCEPT when bound to a ProceduralMeshGenerator with AutoGenerate
                               off - RefreshPreviewMesh's periodic Update() call then no-ops, freezing the preview at
                               whatever it last showed instead of running ahead of the actual bake; the AutoGenerate
                               toggle/Generate button both call RefreshPreviewMesh(force: true) right after
                               target.Generate() so the preview still snaps forward immediately rather than waiting
                               for AutoGenerate to flip back on. A bare GeoGraphAsset has no AutoGenerate/Generate of
                               its own (nor a baked scene Mesh at all), so its preview always stays live
    NodraGraphView.cs          GraphView: builds node/edge views from a GeoGraph, writes edits back via
                               Undo.RecordObject(owner, ...) - owner is whichever UnityEngine.Object Bind() was
                               given (a ProceduralMeshGenerator or a GeoGraphAsset; graph is that Object's own
                               GeoGraph field, read separately since Undo/SerializedObject need the owning Object,
                               not the plain [Serializable] GeoGraph nested inside it - both owner types happen to
                               name that field "Graph", which PopulateCore's SerializedObject.FindProperty("Graph")
                               depends on, not on which concrete type owner is). Every NodraNodeView gets Unbind()'d
                               before its RemoveElement, and a node removal schedules a deferred Populate() -
                               PropertyField bindings are index-path-based (Array.data[N]...), so a removed node
                               leaves survivors bound to shifted/stale indices otherwise, eventually throwing
                               ObjectDisposedException on an unrelated blur
    NodraNodeView.cs           Node: ports from GeoNode.InputCount, fields bound straight to SerializedProperty,
                               GeoNode.Warning (if any) shown as a HelpBox above the fields; BindShowIf reads each
                               field's [ShowIf] (if any) via reflection and TrackPropertyValue on its sibling
                               condition field(s), toggling style.display live as the sibling(s) change.
                               BuildEditableAssetButton adds a live "Open" button (NodraGraphWindow.OpenAsset) for a
                               node overriding GeoNode.EditableAssetFieldName (only SubGraphNode does, for its own
                               SubGraph field) - TrackPropertyValue on that field's own SerializedProperty the same
                               way BindShowIf does, so the button enables the moment something's actually assigned
    GeoGraphAssetEditor.cs     [CustomEditor(GeoGraphAsset), CanEditMultipleObjects]: "Open Graph Editor"
                               (NodraGraphWindow.OpenAsset) + "Save Mesh to Project..." + "Export to FBX..."
                               (FbxExportUtility.ExportMesh) - no AutoGenerate/Generate, which don't apply to a bare
                               graph asset with no Transform of its own. Both Save/Export share TryBuildMesh - a
                               fresh Graph.Evaluate() + GeoMeshBuilder.Build() purely for that one action (no
                               already-baked MeshFilter.sharedMesh to reuse here, unlike ProceduralMeshGenerator),
                               same as NodraGraphWindow's own preview panel does for its live Mesh. Export's copy of
                               that Mesh gets DestroyImmediate'd right after (it's never handed to AssetDatabase.
                               CreateAsset the way Save's is, so nothing else keeps it alive)
    NodraPort.cs               Port subclass reaching Port's protected ctor - only way to attach our own
                               IEdgeConnectorListener instead of Port's hardcoded default one
    MinVector2IntDrawer.cs     [CustomPropertyDrawer] for MinVector2IntAttribute - PropertyField picks it up
                               automatically, no NodraNodeView changes needed
    ProceduralMeshGeneratorEditor.cs   AutoGenerate toggle, "Open Graph Editor" button, Generate/Save Mesh/
                                       Export to FBX buttons (the last two call target.Generate() first, so the
                                       result always matches the graph's current state), session-only Show Normals
                                       toggle (draws a Scene view line per baked Mesh.normals entry - what's
                                       actually rendered, not GeoData.Normals). Export to FBX hands
                                       FbxExportUtility.ExportGameObject the generator's own gameObject directly -
                                       unlike GeoGraphAssetEditor's version, there's already a real scene GameObject
                                       here, so no throwaway one is needed
    NodraNativeMenu.cs         "Nodra/Test Native ..." menu items (Ping/Weld/Smooth By Angle/Box/Sphere/Grid/
                               Torus/Cylinder Generator/Noise Displace/Array/Auto UV/Bend/Cap Holes/Chamfer/Circle
                               Generator/Copy To Points/Delete Points Random/Extrude/Face Filter/Ico Sphere
                               Generator/Mirror/Relax/Scatter/Subdivide/Spline Generator/Vertex Color/Tube/
                               Boolean CSG/UV Transform/Taper/Merge/Flip Normals/Line Generator/Twist/Transform/
                               Random Transform/Slice/Set Attribute/Remove Unused Points) - manual smoke
                               tests for NodraNative, since there's no automated test runner exercising native
                               plugin loading (or Unity-side struct marshaling) here. Test Native Weld runs a tiny
                               hand-checked case (two near-duplicate points + one untouched outlier) through
                               NodraNative.Weld; Test Native Smooth By Angle runs a sharp two-triangle hinge through
                               NodraNative.SmoothByAngle, expecting both shared corners to duplicate; each Test
                               Native *Generator calls its own NodraNative method on a fresh GeoData and checks the
                               resulting point/primitive counts (and, for Box/Grid, the overall bounds size) against
                               the known-exact expected values - Cylinder's own test specifically requests both caps
                               on a cone (RadiusTop 0) to also confirm CapTop actually gets skipped rather than
                               producing a degenerate fan. Test Native Noise Displace checks that amplitude 0 leaves
                               both points untouched and a nonzero amplitude moves at least one - no exact expected
                               displacement to compare against for Perlin, since (see NoiseDisplaceNode.cs's own
                               entry) that path is deliberately not trying to match anything. Test Native Array
                               checks 3 copies of a 2-point/1-primitive shape land at the exact expected offset
                               positions and re-indexed primitive. Test Native Auto UV checks a Z-facing quad's
                               Triplanar UV matches its own XY position exactly. Test Native Bend checks a 10-unit
                               line bent 90 degrees lands its far end at the hand-derived (radius, radius, 0) (see
                               BendNode.cs's own entry). Test Native Cap Holes checks a single open quad gets
                               exactly one reversed-winding cap primitive. Test Native Chamfer runs the same
                               hand-derived two-bridged-quads case Native/NodraCore.Tests/Program.cs checks
                               directly, confirming the exact bridge/inset point indices. Test Native Circle
                               Generator checks a filled circle's point/triangle counts. Test Native Copy To Points
                               stamps a 1-vertex mesh at a single Up-aligned point and checks the exact landing
                               position. Test Native Delete Points Random checks the Chance=0/Chance=1 boundary
                               cases keep-all/drop-all. Test Native Extrude checks a single quad's exact cap +
                               4-wall primitive count. Test Native Face Filter checks only the +Y-facing quad
                               survives a 45-degree filter against Up. Test Native Ico Sphere Generator checks the
                               plain 12-point/20-triangle icosahedron shape at Subdivisions=0. Test Native Mirror
                               checks a point already on the mirror plane gets reused (not duplicated) while an
                               off-plane one gets exactly the expected reflected position and reversed-winding
                               mirrored primitive. Test Native Relax checks a single quad's every point converges
                               exactly onto its own neighbor average at Factor=1. Test Native Scatter checks the
                               requested point count comes back. Test Native Subdivide checks a single quad's exact
                               9-point/4-quad split and its centroid landing at the quad's own center. Test Native
                               Spline Generator checks a 2-control-point curve's endpoints land exactly on them.
                               Test Native Vertex Color checks Height/Slope's own [0, 1] factor at known points/
                               normals. Test Native Tube checks a straight 2-point path's exact ring/side-quad
                               shape. Test Native Boolean CSG checks a small box strictly inside a bigger one:
                               Union keeps exactly the bigger box's own bounds, Intersect exactly the smaller
                               box's - exact triangle counts aren't asserted anywhere CSG is involved, since a BSP
                               tree classifies against infinite planes, not finite polygon extent, so even
                               non-touching geometry can still get split. All of these just check the result shape
                               - Native/NodraCore.Tests already covers each underlying algorithm in isolation, so
                               these only exist to catch a mismatch in the marshaling glue around them
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
