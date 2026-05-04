# Sensor Path

## What The Sensor Path Feature Does

The current workflow is **3 control points**:

1. Select `Start` point on the model.
2. Select `Middle` point on the model.
3. Select `End` point on the model.

After the third click, the app:

1. Hit-tests each clicked point to model space.
2. Finds nearest triangles for start/middle/end.
3. Traces seam-like triangles from **middle -> start** and **middle -> end**.
4. Combines those into one ordered path (`start ... middle ... end`).
5. Converts that path to sampled sensor points and draws the line.
6. Updates sensor axis values (`X/Y/Z/A/B`), arc-center visuals, and export data.

The tracing/scoring currently uses:

- normal similarity threshold (default `15 deg`),
- triangle area compatibility checks,
- direction continuity plus target-direction preference,
- edge-biased path anchors (triangle side extremes) instead of only centroids.

## Draw Button + Point Pick UX

In draw mode:

- Button shows `Drawing...` before any point is selected.
- After selecting **any** control point, button changes to `Clear`.
- `Clear` works for both:
  - partial picks (1-2 points selected),
  - completed line (all 3 points selected and line drawn).

Control-point previews are shown in viewport:

- `Start`: green marker
- `Middle`: yellow marker
- `End`: red marker

Markers clear when you clear draw state or load a new model.

## How To Use In The App

1. Open a model using `Open Model`.
2. (Optional) Set spacing in `Point Spacing (in)`.
3. Click `Draw Line`.
4. Click `Start`, then `Middle`, then `End`.
5. Review the generated path and axis values.
6. Use `Set` to sync spacing from displayed points if needed.
7. Use `Export Arc Points` to save CSV (`Counter,X,Y,Z`).

Notes:

- Legacy `Calculate Sensor Path` button was removed.
- Seam-angle selector UI was removed; threshold is internal (`15 deg`).

## Wireframe Mode Notes

The app supports wireframe view via `Wireframe` checkbox.  
In WPF/Helix, this is implemented using extracted mesh edges (`LinesVisual3D`) rather than a viewport-level native wireframe toggle.

Current implementation:

- Builds wireframe once on load.
- Uses unique edge deduplication to reduce line count.
- Hides solid shading when wireframe mode is active.

## Main Files And Required Logic

### UI Entry + App Flow

- `AddFlaw/MainWindow.xaml`
  - draw button: `drawOneLineButton`
  - spacing controls: `pointSpacingTextBox`, `setPointSpacingButton`
  - wireframe toggle: `wireframeCheckBox`
  - export button: `exportArcPointsButton`

- `AddFlaw/MainWindow.xaml.cs`
  - `DrawOneLineButton_Click(...)` toggles draw mode and handles clear.
  - `Viewport3D_MouseDown(...)` collects start/middle/end clicks.
  - `TryDrawLineFromControlPoints(...)` performs scan + path draw call.
  - `UpdateDrawLineButtonState()` drives `Draw Line` / `Drawing...` / `Clear` state.
  - `PointSpacingTextBox_LostFocus(...)` and `SetPointSpacingButton_Click(...)` redraw with current control points.
  - `ExportArcPointsButton_Click(...)` writes CSV.

### Hit Test (clicked model point)

- `AddFlaw/Managers/ViewportInteraction.cs`
  - `Get3DPointInModel(...)` returns `(hitFound, hitPosition)`.

### Sensor Path Generation + Drawing

- `AddFlaw/Managers/TransitionRegionManager.cs`
  - Public draw entries:
    - `DrawTransitionCenterlineFromControlPoints(...)`
    - `DrawClosestTransitionCenterline(...)` (legacy single-point entry, still present)
  - Control-point seam tracing:
    - `TraceSeamCenterPathBetweenTargets(...)`
    - `TraceDirectionTowardTarget(...)`
    - `SelectBestSeamNeighborTowardTarget(...)`
  - Seed-based seam tracing (legacy path method still available):
    - `TraceSeamCenterPathFromSeed(...)`
    - `TraceDirection(...)`
    - `SelectBestSeamNeighbor(...)`
    - `MaxNeighborNormalBreakCos(...)`
  - Edge-biased anchor points:
    - `BuildEdgeBiasedPathPoints(...)`
    - `PickExtremePointsBySide(...)`
  - Triangle vertex access:
    - `TryGetTriangleVertices(...)`
    - `TryTriVerts(...)`
  - Polyline/sample + visuals:
    - `DrawFromOrderedTriPath(...)`
    - `TryBuildPolylineFromOrderedCenters(...)`
    - `TryResamplePath(...)`
    - `AddPolylineAsSegments(...)`
  - Arc visuals and export source:
    - `UpdateArcCenterVisual(...)`
    - `GetLastArcPoints()`
  - Control-point preview markers:
    - `UpdateControlPointPreview(...)`

### Model Rendering / Wireframe

- `AddFlaw/Managers/ModelManager.cs`
  - `SetWireframeMode(...)`
  - `RebuildWireframeVisual(...)`
  - `AppendWireframeVisuals(...)`
  - deduplicated edge build (`AddUniqueWireEdge(...)`)

### Mesh Scan Data Used By Sensor Path

- `AddFlaw/Geometry/ModelPartScanner.cs`
  - `Scan(...)` builds `ScanResult`.
  - Fields used by path logic:
    - `TriCenters`
    - `TriNormals`
    - `TriAreas`
    - `TriAdjacency`
    - `MeshSlots`
    - `ModelCenter`, `ModelAxis`

### Arc radial cleanup (sampled polyline smoothing)

After the path is sampled and lifted off the mesh, the app runs **arc-aware radial cleanup**:

- Fits a circle in the approximate arc plane (`TryFitArcGeometry`, same algebra as arc-center fit).
- Compares each point’s **in-plane radius** to the **median** radius along the strip.
- **Moderate radial deviation**: snaps onto the median-radius circle in-plane (`ProjectOntoMedianCircle`).
- **Severe deviation**: drops those samples, then bridges each gap along the chord between kept neighbors **re-snapped** to the median orbit (`StitchNullablePathBridgedArc`).

Functions: `ApplyArcRadialFilterAndRepair(...)`, helpers above.

Tune:

- `ARC_RADIAL_SOFT_DIAG_FRAC`, `ARC_RADIAL_HARD_DIAG_FRAC` (fractions of mesh bounding-diagonal combined with `%` of median radius).

## Key Internal Tuning Constants (Current)

In `TransitionRegionManager`:

- `DEFAULT_NORMAL_GROW_ANGLE_DEG = 15.0`
- `MIN_NEIGHBOR_AREA_RATIO = 0.40`
- `MAX_NEIGHBOR_AREA_RATIO = 2.50`
- `ARC_RADIAL_SOFT_DIAG_FRAC`, `ARC_RADIAL_HARD_DIAG_FRAC` (orbit cleanup)

These are the first values to tune when adapting to a new model family.

