# Arc Center Path Upgrade

## UX Change

Switched from 3-point (start, middle, end) to **2-point** (start, end). The
middle click was only doing useful work as an A* waypoint, and a single A*
search start->end produces the same path on a well-defined fillet.

## Files Touched

- `AddFlaw/Managers/TransitionRegionManager.cs` -- core path logic.
- `AddFlaw/MainWindow.xaml.cs` -- pick state machine, status messages.

## New Methods

- `CollectArcTriangles` -- curvy triangles inside the capped tube.
- `ComputeEndCaps` -- shared start/end cap planes with stable lookahead.
- `SquaredDistanceToPolyline` -- point-to-polyline distance (per segment, clamped).
- `ComputeArcCenterPathFromTriangles` -- per-station max-sagitta center via cross-sections.
- `OrthogonalToVector` -- pick a stable orthogonal vector.
- `TryFitPolynomialPath` -- 3D polynomial least-squares fit.
- `TrySolveLinearSystem` -- gaussian elimination for the fit.
- `BuildHighlightedTriangleModel` -- belt-overlay mesh (kept, not currently rendered).

## Rewritten Methods

- `TryBuildAnchoredCurve` -- single A* (start -> end), pure centroid output.
  No middle-click waypoint or anchor.
- `DrawTransitionCenterlineFromControlPoints` -- 2-arg signature
  `(start, end)`; cache key dropped middle.
- `DrawFromAnchoredCurve` -- replaced the entire smoothing + Kasa fit + lift
  + draw pipeline with: collect arc triangles, compute centerline,
  polynomial-fit, trim by caps, draw.
- `UpdateControlPointPreview` -- 2-arg signature `(start, end)`.

## New Field

- `_arcSurfaceVisual` -- belt overlay placeholder; set to null in normal use.

## Tuning Knobs

- Tube radius: `max(spacing * 3, modelDiag * 0.006)`.
- Curvy threshold: 8 deg.
- Polynomial degree: 3.
- Cap lookahead: `max(2, polyN / 8)` vertices.

