using System.Windows.Media;
using System.Windows.Media.Media3D;
using AddFlaw.Geometry;
using HelixToolkit.Wpf;

namespace AddFlaw.Managers {
    public sealed class TransitionRegionManager {
        public sealed record SensorAxisSample(Int32 Index, Double X, Double Y, Double Z, Double A, Double B);
        private const Double DEFAULT_NORMAL_GROW_ANGLE_DEG = 15.0;
        private const Double MIN_NEIGHBOR_AREA_RATIO = 0.40;
        private const Double MAX_NEIGHBOR_AREA_RATIO = 2.50;
        private const Int32 SIDE_EXPAND_MAX_STEPS = 12;
        private const Double SIDE_EXPAND_MAX_NORMAL_ANGLE_DEG = 42.0;
        private const Double SIDE_EXPAND_MIN_AREA_RATIO = 0.25;
        private const Double SIDE_EXPAND_MAX_AREA_RATIO = 4.00;
        private const Double SIDE_EXPAND_MIN_SIDE_ALIGN = 0.06;

        private enum PathOverlayMode {
            FullReplace,
            DragAccumulate
        }

        private readonly LinesVisual3D _lineVisual = new() {
            Color = Color.FromRgb(0x00, 0xB7, 0xFF), // bright cyan-blue
            Thickness = 3.0
        };
        private readonly PointsVisual3D _pointVisual = new() {
            Color = Color.FromRgb(0xFF, 0xD7, 0x00), // gold
            Size = 6.0
        };
        private readonly LinesVisual3D _probeXVisual = new() { Color = Colors.Cyan, Thickness = 2.0 };
        private readonly LinesVisual3D _probeYVisual = new() { Color = Colors.Magenta, Thickness = 2.0 };
        private readonly LinesVisual3D _probeZVisual = new() { Color = Colors.DarkGoldenrod, Thickness = 2.0 };
        private readonly ModelVisual3D _probeBodyVisual = new();
        private readonly ModelVisual3D _probeVisualRoot = new();
        private readonly PointsVisual3D _controlStartVisual = new() { Color = Color.FromRgb(0x2E, 0xCC, 0x71), Size = 10.0 };
        private readonly PointsVisual3D _controlMiddleVisual = new() { Color = Color.FromRgb(0xF1, 0xC4, 0x0F), Size = 10.0 };
        private readonly PointsVisual3D _controlEndVisual = new() { Color = Color.FromRgb(0xE7, 0x4C, 0x3C), Size = 10.0 };
        private readonly LinesVisual3D _sideExpandVectorVisual = new() { Color = Color.FromRgb(0xFF, 0x66, 0xCC), Thickness = 1.4 };
        private readonly PointsVisual3D _sideExpandPointVisual = new() { Color = Color.FromRgb(0xB0, 0x5C, 0xFF), Size = 4.0 };
        private readonly List<SensorAxisSample> _allSensorAxisSamples = [];
        private List<SensorAxisSample> _lastFiveAxisSamples = [];
        private Boolean _probeEnabled;
        private Point3D? _lastProbePosition;
        private Vector3D _lastProbeDirection = new(0, 0, 1);
        private Double _lastProbeDiag = 1.0;
        private Double _lastPathSpacingInch;
        private Int32? _lineDragLockRegionIndex;
        private Int32 _lineDragPathStartIndex = -1;
        private readonly Point3DCollection _pathCommittedLine = [];
        private readonly Point3DCollection _pathCommittedPoints = [];
        private Point3DCollection _pathActiveLine = [];
        private Point3DCollection _pathActivePoints = [];

        public TransitionRegionManager(HelixViewport3D viewport_) {
            if (!viewport_.Children.Contains(_lineVisual))
                viewport_.Children.Add(_lineVisual);
            if (!viewport_.Children.Contains(_pointVisual))
                viewport_.Children.Add(_pointVisual);

            _probeVisualRoot.Children.Add(_probeXVisual);
            _probeVisualRoot.Children.Add(_probeYVisual);
            _probeVisualRoot.Children.Add(_probeZVisual);
            _probeVisualRoot.Children.Add(_probeBodyVisual);
            if (!viewport_.Children.Contains(_probeVisualRoot))
                viewport_.Children.Add(_probeVisualRoot);
            if (!viewport_.Children.Contains(_controlStartVisual))
                viewport_.Children.Add(_controlStartVisual);
            if (!viewport_.Children.Contains(_controlMiddleVisual))
                viewport_.Children.Add(_controlMiddleVisual);
            if (!viewport_.Children.Contains(_controlEndVisual))
                viewport_.Children.Add(_controlEndVisual);
            if (!viewport_.Children.Contains(_sideExpandVectorVisual))
                viewport_.Children.Add(_sideExpandVectorVisual);
            if (!viewport_.Children.Contains(_sideExpandPointVisual))
                viewport_.Children.Add(_sideExpandPointVisual);
        }

        public void DrawTransitionCenterline(ModelPartScanner.ScanResult scan_, Double spacingInch_) {
            _ = DrawRegions(scan_, spacingInch_, scan_.TransitionRegionTriangles);
        }

        public void ClearTransitionCenterline() {
            _lineDragLockRegionIndex = null;
            _lineDragPathStartIndex = -1;
            _pathCommittedLine.Clear();
            _pathCommittedPoints.Clear();
            _pathActiveLine = [];
            _pathActivePoints = [];
            _lineVisual.Points = [];
            _pointVisual.Points = [];
            _lastPathSpacingInch = 0;
            _probeXVisual.Points = [];
            
            _probeYVisual.Points = [];
            _probeZVisual.Points = [];
            _probeBodyVisual.Content = null;
            _sideExpandVectorVisual.Points = [];
            _sideExpandPointVisual.Points = [];
            _lastFiveAxisSamples = [];
            _allSensorAxisSamples.Clear();
            _lastProbePosition = null;
        }

        public void UpdateControlPointPreview(Point3D? startPoint_, Point3D? middlePoint_, Point3D? endPoint_) {
            _controlStartVisual.Points = startPoint_ is Point3D s ? [s] : [];
            _controlMiddleVisual.Points = middlePoint_ is Point3D m ? [m] : [];
            _controlEndVisual.Points = endPoint_ is Point3D e ? [e] : [];
        }

        public IReadOnlyList<SensorAxisSample> GetLastFiveAxisSamples() => _lastFiveAxisSamples;

        public IReadOnlyList<SensorAxisSample> GetAllSensorAxisSamples() => _allSensorAxisSamples;

        public void BeginLineDragSession() {
            _lineDragLockRegionIndex = null;
            _lineDragPathStartIndex = -1;
            _pathActiveLine = [];
            _pathActivePoints = [];
            RefreshMergedPathVisuals();
        }

        public void EndLineDragSession() {
            _lineDragLockRegionIndex = null;
            _lineDragPathStartIndex = -1;
            foreach (Point3D p in _pathActiveLine)
                _pathCommittedLine.Add(p);
            foreach (Point3D p in _pathActivePoints)
                _pathCommittedPoints.Add(p);
            _pathActiveLine = [];
            _pathActivePoints = [];
            _lineVisual.Points = new Point3DCollection(_pathCommittedLine);
            _pointVisual.Points = new Point3DCollection(_pathCommittedPoints);
        }

        private void RefreshMergedPathVisuals() {
            _lineVisual.Points = MergePoint3DCols(_pathCommittedLine, _pathActiveLine);
            _pointVisual.Points = MergePoint3DCols(_pathCommittedPoints, _pathActivePoints);
        }

        private static Point3DCollection MergePoint3DCols(IEnumerable<Point3D> a_, IEnumerable<Point3D> b_) {
            Point3DCollection m = [];
            foreach (Point3D p in a_) m.Add(p);
            foreach (Point3D p in b_) m.Add(p);
            return m;
        }

        public Boolean UpdateLineDuringDrag(
            ModelPartScanner.ScanResult scan_,
            Double spacingInch_,
            Point3D currentHit_,
            Double maxDistance_) {
            _ = maxDistance_;
            return DrawCenterlineFromSeedPoint(scan_, spacingInch_, currentHit_, DEFAULT_NORMAL_GROW_ANGLE_DEG, PathOverlayMode.DragAccumulate);
        }

        private Boolean DrawCenterlineFromSeedPoint(
            ModelPartScanner.ScanResult scan_,
            Double spacingInch_,
            Point3D seedPoint_,
            Double normalAngleDeg_ = DEFAULT_NORMAL_GROW_ANGLE_DEG,
            PathOverlayMode overlay_ = PathOverlayMode.FullReplace) {
            if (!TraceSeamCenterPathFromSeed(scan_, seedPoint_, normalAngleDeg_, out List<Int32>? triPath) || triPath is null || triPath.Count < 2)
                return false;
            return DrawFromOrderedTriPath(scan_, spacingInch_, triPath, seedPoint_, null, null, overlay_);
        }

        private static Int32 NearestSampleIndex(IReadOnlyList<Point3D> samples_, Point3D p_) {
            Int32 best = 0;
            Double bestD2 = Double.MaxValue;
            for (Int32 i = 0; i < samples_.Count; i++) {
                Vector3D d = samples_[i] - p_;
                Double d2 = d.LengthSquared;
                if (d2 < bestD2) {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
        }

        private static Boolean IsPointNearTransitionRegion(
            ModelPartScanner.ScanResult scan_,
            Int32 regionIndex_,
            Point3D p_,
            Double maxD2_) {
            IReadOnlyList<Int32> region = scan_.TransitionRegionTriangles[regionIndex_];
            foreach (Int32 t in region) {
                if (t < 0 || t >= scan_.TriCenters.Count) continue;
                Vector3D d = scan_.TriCenters[t] - p_;
                if (d.LengthSquared <= maxD2_)
                    return true;
            }
            return false;
        }

        public void SetProbeEnabled(Boolean enabled_) {
            _probeEnabled = enabled_;
            if (!_probeEnabled) {
                _probeXVisual.Points = [];
                _probeYVisual.Points = [];
                _probeZVisual.Points = [];
                _probeBodyVisual.Content = null;
                return;
            }
            if (_lastProbePosition is Point3D p)
                UpdateProbeVisual(p, _lastProbeDirection, _lastProbeDiag);
        }

        public Boolean DrawClosestTransitionCenterline(ModelPartScanner.ScanResult scan_, Double spacingInch_, Point3D approxPoint_) {
            return DrawCenterlineFromSeedPoint(scan_, spacingInch_, approxPoint_, DEFAULT_NORMAL_GROW_ANGLE_DEG, PathOverlayMode.FullReplace);
        }

        public Boolean DrawTransitionCenterlineFromControlPoints(
            ModelPartScanner.ScanResult scan_,
            Double spacingInch_,
            Point3D startPoint_,
            Point3D middlePoint_,
            Point3D endPoint_) {
            if (!TraceSeamCenterPathBetweenTargets(scan_, middlePoint_, startPoint_, endPoint_, DEFAULT_NORMAL_GROW_ANGLE_DEG, out List<Int32>? triPath) ||
                triPath is null ||
                triPath.Count < 2)
                return false;
            return DrawFromOrderedTriPath(scan_, spacingInch_, triPath, middlePoint_, null, null, PathOverlayMode.FullReplace, useEdgeBiasedPoints_: false);
        }

        public Boolean DrawClosestTransitionCenterlineIfNear(ModelPartScanner.ScanResult scan_, Double spacingInch_, Point3D approxPoint_, Double maxDistance_) {
            if (scan_.TriCenters.Count == 0 || maxDistance_ <= 0)
                return false;
            Double bestD2 = Double.MaxValue;
            for (Int32 t = 0; t < scan_.TriCenters.Count; t++) {
                Vector3D d = scan_.TriCenters[t] - approxPoint_;
                Double d2 = d.LengthSquared;
                if (d2 < bestD2) {
                    bestD2 = d2;
                }
            }

            if (bestD2 > (maxDistance_ * maxDistance_))
                return false;
            return DrawCenterlineFromSeedPoint(scan_, spacingInch_, approxPoint_, DEFAULT_NORMAL_GROW_ANGLE_DEG, PathOverlayMode.FullReplace);
        }

        private static Boolean TraceSeamCenterPathFromSeed(
            ModelPartScanner.ScanResult scan_,
            Point3D seedPoint_,
            Double maxNormalAngleDeg_,
            out List<Int32>? triPath_) {
            triPath_ = null;
            if (scan_.TriCenters.Count == 0 || scan_.TriNormals.Count == 0 || scan_.TriAreas.Count == 0 || scan_.TriAdjacency.Count == 0)
                return false;
            if (scan_.TriCenters.Count != scan_.TriNormals.Count || scan_.TriCenters.Count != scan_.TriAreas.Count || scan_.TriCenters.Count != scan_.TriAdjacency.Count)
                return false;

            Int32 seedTri = 0;
            Double bestD2 = Double.MaxValue;
            for (Int32 i = 0; i < scan_.TriCenters.Count; i++) {
                Vector3D d = scan_.TriCenters[i] - seedPoint_;
                Double d2 = d.LengthSquared;
                if (d2 < bestD2) {
                    bestD2 = d2;
                    seedTri = i;
                }
            }

            Double cosThreshold = Math.Cos(Math.Clamp(maxNormalAngleDeg_, 0, 89.9) * (Math.PI / 180.0));
            HashSet<Int32> seedUsed = [seedTri];
            Int32 firstNeighbor = SelectBestSeamNeighbor(scan_, -1, seedTri, seedUsed, cosThreshold);
            if (firstNeighbor < 0)
                return false;
            Int32 secondNeighbor = SelectBestSeamNeighbor(scan_, -1, seedTri, seedUsed, cosThreshold, firstNeighbor);

            List<Int32> backward = TraceDirection(scan_, seedTri, -1, maxNormalAngleDeg_, 220, firstNeighbor);
            List<Int32> forward = secondNeighbor >= 0
                ? TraceDirection(scan_, seedTri, -1, maxNormalAngleDeg_, 220, secondNeighbor)
                : [seedTri];

            // Remove duplicated seed in the forward half.
            if (forward.Count > 0)
                forward.RemoveAt(0);
            backward.Reverse();
            triPath_ = [.. backward, .. forward];
            return triPath_.Count > 1;
        }

        private static Boolean TraceSeamCenterPathBetweenTargets(
            ModelPartScanner.ScanResult scan_,
            Point3D middlePoint_,
            Point3D startPoint_,
            Point3D endPoint_,
            Double maxNormalAngleDeg_,
            out List<Int32>? triPath_) {
            triPath_ = null;
            if (scan_.TriCenters.Count == 0 || scan_.TriAdjacency.Count == 0)
                return false;

            Int32 midTri = FindNearestTriangle(scan_, middlePoint_);
            Int32 startTri = FindNearestTriangle(scan_, startPoint_);
            Int32 endTri = FindNearestTriangle(scan_, endPoint_);
            if (midTri < 0 || startTri < 0 || endTri < 0)
                return false;

            if (!TryFindGuidedShortestTrianglePath(scan_, startTri, midTri, startPoint_, middlePoint_, out List<Int32>? toMiddle) ||
                toMiddle is null ||
                toMiddle.Count < 1)
                return false;

            if (!TryFindGuidedShortestTrianglePath(scan_, midTri, endTri, middlePoint_, endPoint_, out List<Int32>? toEnd) ||
                toEnd is null ||
                toEnd.Count < 1)
                return false;

            if (toEnd.Count > 0)
                toEnd.RemoveAt(0);
            triPath_ = [.. toMiddle, .. toEnd];
            return triPath_.Count > 1;
        }

        private static Boolean TryFindGuidedShortestTrianglePath(
            ModelPartScanner.ScanResult scan_,
            Int32 startTri_,
            Int32 targetTri_,
            Point3D guideStart_,
            Point3D guideEnd_,
            out List<Int32>? path_) {
            path_ = null;
            Int32 n = scan_.TriCenters.Count;
            if (startTri_ < 0 || startTri_ >= n || targetTri_ < 0 || targetTri_ >= n || scan_.TriAdjacency.Count != n)
                return false;
            if (startTri_ == targetTri_) {
                path_ = [startTri_];
                return true;
            }

            Double[] dist = Enumerable.Repeat(Double.PositiveInfinity, n).ToArray();
            Int32[] prev = Enumerable.Repeat(-1, n).ToArray();
            PriorityQueue<Int32, Double> open = new();
            dist[startTri_] = 0;
            open.Enqueue(startTri_, 0);

            while (open.TryDequeue(out Int32 cur, out Double curDist)) {
                if (curDist > dist[cur] + 1e-12)
                    continue;
                if (cur == targetTri_)
                    break;

                foreach (Int32 next in scan_.TriAdjacency[cur]) {
                    if (next < 0 || next >= n)
                        continue;

                    Double stepCost = GuidedTriangleStepCost(scan_, cur, next, guideStart_, guideEnd_);
                    if (!Double.IsFinite(stepCost) || stepCost <= 0)
                        continue;

                    Double nd = curDist + stepCost;
                    if (nd >= dist[next])
                        continue;

                    dist[next] = nd;
                    prev[next] = cur;
                    open.Enqueue(next, nd);
                }
            }

            if (!Double.IsFinite(dist[targetTri_]))
                return false;

            List<Int32> rev = [];
            for (Int32 cur = targetTri_; cur >= 0; cur = prev[cur]) {
                rev.Add(cur);
                if (cur == startTri_)
                    break;
            }
            if (rev[^1] != startTri_)
                return false;

            rev.Reverse();
            path_ = rev;
            return path_.Count > 0;
        }

        private static Double GuidedTriangleStepCost(
            ModelPartScanner.ScanResult scan_,
            Int32 fromTri_,
            Int32 toTri_,
            Point3D guideStart_,
            Point3D guideEnd_) {
            Point3D from = scan_.TriCenters[fromTri_];
            Point3D to = scan_.TriCenters[toTri_];
            Vector3D step = to - from;
            Double stepLen = step.Length;
            if (stepLen < 1e-12)
                return Double.PositiveInfinity;

            Vector3D guide = guideEnd_ - guideStart_;
            Double guideLen = guide.Length;
            if (guideLen < 1e-12)
                return stepLen;

            step.Normalize();
            guide.Normalize();

            Double forwardPenalty = Math.Pow(Math.Max(0, 1.0 - Vector3D.DotProduct(step, guide)), 2.0);
            Double lateral = DistancePointToSegment(to, guideStart_, guideEnd_);
            Double guideScale = Math.Max(guideLen * 0.05, stepLen * 4.0);
            Double lateralPenalty = Math.Min(4.0, lateral / Math.Max(guideScale, 1e-9));

            return stepLen * (1.0 + (0.75 * forwardPenalty) + (0.35 * lateralPenalty));
        }

        private static Double DistancePointToSegment(Point3D p_, Point3D a_, Point3D b_) {
            Vector3D ab = b_ - a_;
            Double ab2 = ab.LengthSquared;
            if (ab2 < 1e-18)
                return (p_ - a_).Length;
            Double t = Vector3D.DotProduct(p_ - a_, ab) / ab2;
            t = Math.Clamp(t, 0.0, 1.0);
            Point3D q = a_ + (ab * t);
            return (p_ - q).Length;
        }

        private static Int32 FindNearestTriangle(ModelPartScanner.ScanResult scan_, Point3D point_) {
            if (scan_.TriCenters.Count == 0)
                return -1;
            Int32 best = 0;
            Double bestD2 = Double.MaxValue;
            for (Int32 i = 0; i < scan_.TriCenters.Count; i++) {
                Vector3D d = scan_.TriCenters[i] - point_;
                Double d2 = d.LengthSquared;
                if (d2 < bestD2) {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
        }

        private static List<Int32> TraceDirectionTowardTarget(
            ModelPartScanner.ScanResult scan_,
            Int32 startTri_,
            Int32 targetTri_,
            Double maxNormalAngleDeg_,
            Int32 maxSteps_) {
            if (startTri_ == targetTri_)
                return [startTri_];

            Double cosThreshold = Math.Cos(Math.Clamp(maxNormalAngleDeg_, 0, 89.9) * (Math.PI / 180.0));
            HashSet<Int32> used = [startTri_];
            List<Int32> path = [startTri_];
            Int32 prev = -1;
            Int32 cur = startTri_;
            Double bestToTarget = (scan_.TriCenters[cur] - scan_.TriCenters[targetTri_]).LengthSquared;

            for (Int32 step = 0; step < maxSteps_; step++) {
                if (cur == targetTri_)
                    break;
                Int32 next = SelectBestSeamNeighborTowardTarget(scan_, prev, cur, targetTri_, used, cosThreshold);
                if (next < 0)
                    break;

                Double d2 = (scan_.TriCenters[next] - scan_.TriCenters[targetTri_]).LengthSquared;
                // Prevent long detours that move away from chosen end triangle.
                if (d2 > (bestToTarget * 1.8) + 1e-12)
                    break;

                path.Add(next);
                used.Add(next);
                prev = cur;
                cur = next;
                if (d2 < bestToTarget)
                    bestToTarget = d2;
            }
            return path;
        }

        private static Int32 SelectBestSeamNeighborTowardTarget(
            ModelPartScanner.ScanResult scan_,
            Int32 prevTri_,
            Int32 curTri_,
            Int32 targetTri_,
            IReadOnlySet<Int32> used_,
            Double cosThreshold_) {
            Vector3D nCur = scan_.TriNormals[curTri_];
            if (nCur.LengthSquared < 1e-18) return -1;
            nCur.Normalize();

            Point3D pCur = scan_.TriCenters[curTri_];
            Vector3D targetDir = scan_.TriCenters[targetTri_] - pCur;
            if (targetDir.LengthSquared < 1e-18)
                return targetTri_;
            targetDir.Normalize();

            Vector3D forward = new();
            Boolean hasForward = prevTri_ >= 0;
            if (hasForward) {
                forward = pCur - scan_.TriCenters[prevTri_];
                if (forward.LengthSquared < 1e-18) hasForward = false;
                else forward.Normalize();
            }

            Int32 best = -1;
            Double bestScore = Double.NegativeInfinity;
            foreach (Int32 n in scan_.TriAdjacency[curTri_]) {
                if (n < 0 || n >= scan_.TriCenters.Count || used_.Contains(n))
                    continue;

                Vector3D nNbr = scan_.TriNormals[n];
                if (nNbr.LengthSquared < 1e-18)
                    continue;
                nNbr.Normalize();
                Double cos = Vector3D.DotProduct(nCur, nNbr);
                if (cos < cosThreshold_)
                    continue;

                Double curArea = scan_.TriAreas[curTri_];
                Double nbrArea = scan_.TriAreas[n];
                if (curArea <= 1e-14 || nbrArea <= 1e-14)
                    continue;
                Double areaRatio = nbrArea / curArea;
                if (areaRatio < MIN_NEIGHBOR_AREA_RATIO || areaRatio > MAX_NEIGHBOR_AREA_RATIO)
                    continue;

                Vector3D step = scan_.TriCenters[n] - pCur;
                if (step.LengthSquared < 1e-18)
                    continue;
                step.Normalize();

                Double continuation = hasForward ? Math.Max(0, Vector3D.DotProduct(step, forward)) : 0.5;
                Double towardTarget = Math.Max(0, Vector3D.DotProduct(step, targetDir));
                Double targetDistance = (scan_.TriCenters[n] - scan_.TriCenters[targetTri_]).Length;
                Double targetCloseness = 1.0 / (1.0 + targetDistance);
                Double smoothNormal = (cos + 1.0) * 0.5;
                Double areaSimilarity = 1.0 - Math.Clamp(Math.Abs(Math.Log(areaRatio, 2.0)) / 2.0, 0.0, 1.0);
                Double score = (smoothNormal * 0.20) + (continuation * 0.20) + (towardTarget * 0.35) + (targetCloseness * 0.20) + (areaSimilarity * 0.05);

                if (n == targetTri_)
                    score += 100.0;

                if (score > bestScore) {
                    bestScore = score;
                    best = n;
                }
            }
            return best;
        }

        private static List<Int32> TraceDirection(
            ModelPartScanner.ScanResult scan_,
            Int32 seedTri_,
            Int32 blockedTri_,
            Double maxNormalAngleDeg_,
            Int32 maxSteps_,
            Int32 forcedFirstNeighbor_) {
            Double cosThreshold = Math.Cos(Math.Clamp(maxNormalAngleDeg_, 0, 89.9) * (Math.PI / 180.0));
            HashSet<Int32> used = [seedTri_];
            List<Int32> path = [seedTri_];
            Int32 prev = blockedTri_;
            Int32 cur = seedTri_;

            if (forcedFirstNeighbor_ >= 0) {
                path.Add(forcedFirstNeighbor_);
                used.Add(forcedFirstNeighbor_);
                prev = seedTri_;
                cur = forcedFirstNeighbor_;
            }

            for (Int32 step = 0; step < maxSteps_; step++) {
                Int32 next = SelectBestSeamNeighbor(scan_, prev, cur, used, cosThreshold);
                if (next < 0)
                    break;
                path.Add(next);
                used.Add(next);
                prev = cur;
                cur = next;
            }
            return path;
        }

        private static Int32 SelectBestSeamNeighbor(
            ModelPartScanner.ScanResult scan_,
            Int32 prevTri_,
            Int32 curTri_,
            IReadOnlySet<Int32> used_,
            Double cosThreshold_,
            Int32 blockedNeighbor_ = -1) {
            Vector3D nCur = scan_.TriNormals[curTri_];
            if (nCur.LengthSquared < 1e-18) return -1;
            nCur.Normalize();

            Point3D pCur = scan_.TriCenters[curTri_];
            Vector3D forward = new();
            Boolean hasForward = prevTri_ >= 0;
            if (hasForward) {
                forward = pCur - scan_.TriCenters[prevTri_];
                if (forward.LengthSquared < 1e-18) hasForward = false;
                else forward.Normalize();
            }

            Int32 best = -1;
            Double bestScore = Double.NegativeInfinity;
            foreach (Int32 n in scan_.TriAdjacency[curTri_]) {
                if (n < 0 || n >= scan_.TriCenters.Count || used_.Contains(n) || n == blockedNeighbor_)
                    continue;

                Vector3D nNbr = scan_.TriNormals[n];
                if (nNbr.LengthSquared < 1e-18)
                    continue;
                nNbr.Normalize();
                Double cos = Vector3D.DotProduct(nCur, nNbr);
                if (cos < cosThreshold_)
                    continue;

                // Area compatibility gate to avoid jumping between very different triangle scales.
                Double curArea = scan_.TriAreas[curTri_];
                Double nbrArea = scan_.TriAreas[n];
                if (curArea <= 1e-14 || nbrArea <= 1e-14)
                    continue;
                Double areaRatio = nbrArea / curArea;
                if (areaRatio < MIN_NEIGHBOR_AREA_RATIO || areaRatio > MAX_NEIGHBOR_AREA_RATIO)
                    continue;

                Vector3D step = scan_.TriCenters[n] - pCur;
                if (step.LengthSquared < 1e-18)
                    continue;
                step.Normalize();

                // Prefer moving along current seam direction.
                Double continuation = hasForward ? Math.Max(0, Vector3D.DotProduct(step, forward)) : 0.5;

                // Prefer triangles that lie on a local "normal-break ridge",
                // i.e. they have at least one adjacent hard normal change.
                Double hardBreak = MaxNeighborNormalBreakCos(scan_, n);
                Double ridgeSignal = Math.Max(0, (cosThreshold_ - hardBreak) / Math.Max(1e-6, 1.0 - cosThreshold_));

                // Keep movement smooth and narrow.
                Double smoothNormal = (cos + 1.0) * 0.5;
                Double areaSimilarity = 1.0 - Math.Clamp(Math.Abs(Math.Log(areaRatio, 2.0)) / 2.0, 0.0, 1.0);
                Double score = (smoothNormal * 0.30) + (continuation * 0.30) + (ridgeSignal * 0.25) + (areaSimilarity * 0.15);
                if (score > bestScore) {
                    bestScore = score;
                    best = n;
                }
            }
            return best;
        }

        private static Double MaxNeighborNormalBreakCos(ModelPartScanner.ScanResult scan_, Int32 tri_) {
            Vector3D nTri = scan_.TriNormals[tri_];
            if (nTri.LengthSquared < 1e-18) return 1.0;
            nTri.Normalize();
            Double minCos = 1.0;
            foreach (Int32 n in scan_.TriAdjacency[tri_]) {
                if (n < 0 || n >= scan_.TriNormals.Count)
                    continue;
                Vector3D nn = scan_.TriNormals[n];
                if (nn.LengthSquared < 1e-18)
                    continue;
                nn.Normalize();
                Double cos = Vector3D.DotProduct(nTri, nn);
                if (cos < minCos) minCos = cos;
            }
            return minCos;
        }

        private Boolean DrawLocalOneSidedRegion(
            ModelPartScanner.ScanResult scan_,
            Double spacingInch_,
            IReadOnlyList<Int32> region_,
            Point3D approxPoint_,
            Int32? segmentIndexA_,
            Int32? segmentIndexB_,
            PathOverlayMode overlay_) {
            if (scan_.MeshSlots.Count == 0 || region_.Count < 2) return false;
            if (!TryOrderTransitionCentersAlongStrip(region_, scan_, out List<Int32>? triPath) || triPath is null || triPath.Count < 2)
                return false;
            return DrawFromOrderedTriPath(scan_, spacingInch_, triPath, approxPoint_, segmentIndexA_, segmentIndexB_, overlay_);
        }

        private Boolean DrawFromOrderedTriPath(
            ModelPartScanner.ScanResult scan_,
            Double spacingInch_,
            IReadOnlyList<Int32> triPath_,
            Point3D approxPoint_,
            Int32? segmentIndexA_,
            Int32? segmentIndexB_,
            PathOverlayMode overlay_,
            Boolean useEdgeBiasedPoints_ = true) {
            List<Point3D> centroidsInOrder = useEdgeBiasedPoints_ ? BuildEdgeBiasedPathPoints(scan_, triPath_) : [];
            if (centroidsInOrder.Count < 2)
                centroidsInOrder = [.. triPath_.Select(t => scan_.TriCenters[t])];
            List<Vector3D> normalsInOrder = [.. triPath_.Select(t => scan_.TriNormals[t])];
            if (!TryBuildPolylineFromOrderedCenters(centroidsInOrder, Math.Max(0.01, spacingInch_), out List<Point3D>? samples) || samples is null || samples.Count < 2)
                return false;

            Int32 lo, hi;
            if (segmentIndexA_ is not null && segmentIndexB_ is not null) {
                lo = Math.Min(segmentIndexA_.Value, segmentIndexB_.Value);
                hi = Math.Max(segmentIndexA_.Value, segmentIndexB_.Value);
                if (lo < 0) lo = 0;
                if (hi >= samples.Count) hi = samples.Count - 1;
                if (hi - lo + 1 < 2) {
                    if (lo > 0) lo--;
                    else if (hi < samples.Count - 1) hi++;
                }
            } else {
                lo = 0;
                hi = samples.Count - 1;
            }
            if (hi - lo + 1 < 2) return false;
            List<Point3D> local = [.. samples.GetRange(lo, hi - lo + 1)];
            if (local.Count < 2) return false;

            Point3D globalCenter = scan_.ModelCenter;
            Rect3D bounds = scan_.MeshSlots[0].OwnerModel.Bounds;
            Double diag = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ);
            Double lift = Math.Max(0.0002, diag * 0.00003);
            List<Vector3D> localDirections = [];
            for (Int32 i = 0; i < local.Count; i++) {
                Vector3D dir = GetOutsideDirectionForSample(local[i], centroidsInOrder, normalsInOrder, globalCenter, scan_.ModelAxis);
                local[i] = MovePointOutsideSurfaceWithClearance(
                    local[i],
                    centroidsInOrder,
                    normalsInOrder,
                    globalCenter,
                    scan_.ModelAxis,
                    lift,
                    Math.Max(lift * 1.5, diag * 0.00008));
                localDirections.Add(dir);
            }

            Int32 localNearestIdx = 0;
            Double localNearestD2 = Double.MaxValue;
            for (Int32 i = 0; i < local.Count; i++) {
                Vector3D d = local[i] - approxPoint_;
                Double d2 = d.LengthSquared;
                if (d2 < localNearestD2) {
                    localNearestD2 = d2;
                    localNearestIdx = i;
                }
            }

            _lastFiveAxisSamples = BuildFiveAxisSamples(local, localDirections, localNearestIdx);
            _allSensorAxisSamples.Clear();
            for (Int32 i = 0; i < local.Count; i++)
                _allSensorAxisSamples.Add(ToAxisSample(i + 1, local[i], localDirections[i]));
            if (_probeEnabled) {
                // Place probe on the selected line at the clicked-nearest sample.
                UpdateProbeVisual(local[localNearestIdx], localDirections[localNearestIdx], diag);
            } else {
                _probeXVisual.Points = [];
                _probeYVisual.Points = [];
                _probeZVisual.Points = [];
                _probeBodyVisual.Content = null;
            }

            _lastPathSpacingInch = Math.Max(0.01, spacingInch_);
            Point3DCollection lineSegments = [];
            AddPolylineAsSegments(lineSegments, local);
            // One point per resampled vertex on the current segment (same as polyline, after lift).
            Point3DCollection pointCollection = [];
            foreach (Point3D p in local)
                pointCollection.Add(p);

            if (overlay_ == PathOverlayMode.FullReplace) {
                _pathActiveLine = [];
                _pathActivePoints = [];
                _pathCommittedLine.Clear();
                _pathCommittedPoints.Clear();
                foreach (Point3D p in lineSegments)
                    _pathCommittedLine.Add(p);
                foreach (Point3D p in pointCollection)
                    _pathCommittedPoints.Add(p);
                _lineVisual.Points = new Point3DCollection(_pathCommittedLine);
                _pointVisual.Points = new Point3DCollection(_pathCommittedPoints);
            } else {
                _pathActiveLine = lineSegments;
                _pathActivePoints = pointCollection;
                RefreshMergedPathVisuals();
            }
            if (!useEdgeBiasedPoints_)
                UpdateSideExpansionDebugVisual(scan_, triPath_, diag, lift);
            return lineSegments.Count >= 2;
        }

        private void UpdateSideExpansionDebugVisual(
            ModelPartScanner.ScanResult scan_,
            IReadOnlyList<Int32> triPath_,
            Double modelDiag_,
            Double lift_) {
            Point3DCollection vectors = [];
            Point3DCollection points = [];
            if (triPath_.Count < 2 || scan_.TriNormals.Count != scan_.TriCenters.Count || scan_.TriAdjacency.Count != scan_.TriCenters.Count) {
                _sideExpandVectorVisual.Points = vectors;
                _sideExpandPointVisual.Points = points;
                return;
            }

            HashSet<Int32> pathSet = [.. triPath_];
            Double drawLift = Math.Max(lift_ * 2.2, modelDiag_ * 0.00012);
            for (Int32 i = 0; i < triPath_.Count; i++) {
                Int32 tri = triPath_[i];
                if (tri < 0 || tri >= scan_.TriCenters.Count)
                    continue;

                Vector3D flow = TrianglePathFlow(scan_, triPath_, i);
                Vector3D normal = scan_.TriNormals[tri];
                if (!TryNormalize(ref flow) || !TryNormalize(ref normal))
                    continue;

                Vector3D side = Vector3D.CrossProduct(normal, flow);
                if (!TryNormalize(ref side))
                    continue;

                Point3D seed = LiftTriangleCenter(scan_, tri, drawLift);
                points.Add(seed);
                AddSideExpansionDirection(scan_, tri, side, pathSet, drawLift, vectors, points);
                AddSideExpansionDirection(scan_, tri, -side, pathSet, drawLift, vectors, points);
            }

            _sideExpandVectorVisual.Points = vectors;
            _sideExpandPointVisual.Points = points;
        }

        private static Vector3D TrianglePathFlow(ModelPartScanner.ScanResult scan_, IReadOnlyList<Int32> triPath_, Int32 index_) {
            Int32 cur = triPath_[index_];
            if (index_ == 0)
                return scan_.TriCenters[triPath_[1]] - scan_.TriCenters[cur];
            if (index_ == triPath_.Count - 1)
                return scan_.TriCenters[cur] - scan_.TriCenters[triPath_[index_ - 1]];
            return scan_.TriCenters[triPath_[index_ + 1]] - scan_.TriCenters[triPath_[index_ - 1]];
        }

        private static void AddSideExpansionDirection(
            ModelPartScanner.ScanResult scan_,
            Int32 seedTri_,
            Vector3D sideDir_,
            IReadOnlySet<Int32> pathSet_,
            Double lift_,
            Point3DCollection vectors_,
            Point3DCollection points_) {
            HashSet<Int32> used = [seedTri_];
            Double cosLimit = Math.Cos(SIDE_EXPAND_MAX_NORMAL_ANGLE_DEG * (Math.PI / 180.0));
            List<Int32> frontier = [seedTri_];

            for (Int32 depth = 0; depth < SIDE_EXPAND_MAX_STEPS && frontier.Count > 0; depth++) {
                List<Int32> nextFrontier = [];
                foreach (Int32 cur in frontier) {
                    List<Int32> neighbors = CollectSimilarSideNeighbors(scan_, seedTri_, cur, sideDir_, used, pathSet_, cosLimit);
                    foreach (Int32 next in neighbors) {
                        if (!used.Add(next))
                            continue;

                        Point3D from = LiftTriangleCenter(scan_, cur, lift_);
                        Point3D to = LiftTriangleCenter(scan_, next, lift_);
                        vectors_.Add(from);
                        vectors_.Add(to);
                        points_.Add(to);
                        nextFrontier.Add(next);
                    }
                }

                if (nextFrontier.Count == 0)
                    break;
                frontier = nextFrontier;
            }
        }

        private static List<Int32> CollectSimilarSideNeighbors(
            ModelPartScanner.ScanResult scan_,
            Int32 seedTri_,
            Int32 curTri_,
            Vector3D sideDir_,
            IReadOnlySet<Int32> used_,
            IReadOnlySet<Int32> pathSet_,
            Double cosLimit_) {
            Vector3D seedNormal = scan_.TriNormals[seedTri_];
            Vector3D curNormal = scan_.TriNormals[curTri_];
            if (!TryNormalize(ref seedNormal) || !TryNormalize(ref curNormal))
                return [];

            Double seedArea = scan_.TriAreas.Count > seedTri_ ? scan_.TriAreas[seedTri_] : 0;
            Double curArea = scan_.TriAreas.Count > curTri_ ? scan_.TriAreas[curTri_] : 0;
            if (seedArea <= 1e-14 || curArea <= 1e-14)
                return [];

            Point3D curCenter = scan_.TriCenters[curTri_];
            List<(Int32 tri, Double score)> accepted = [];
            foreach (Int32 n in scan_.TriAdjacency[curTri_]) {
                if (n < 0 || n >= scan_.TriCenters.Count || used_.Contains(n) || pathSet_.Contains(n))
                    continue;

                Vector3D nNormal = scan_.TriNormals[n];
                if (!TryNormalize(ref nNormal))
                    continue;
                Double seedCos = Vector3D.DotProduct(seedNormal, nNormal);
                Double curCos = Vector3D.DotProduct(curNormal, nNormal);
                if (seedCos < cosLimit_ || curCos < cosLimit_)
                    continue;

                Double nArea = scan_.TriAreas.Count > n ? scan_.TriAreas[n] : 0;
                if (nArea <= 1e-14)
                    continue;
                Double seedAreaRatio = nArea / seedArea;
                Double curAreaRatio = nArea / curArea;
                if (seedAreaRatio < SIDE_EXPAND_MIN_AREA_RATIO || seedAreaRatio > SIDE_EXPAND_MAX_AREA_RATIO ||
                    curAreaRatio < SIDE_EXPAND_MIN_AREA_RATIO || curAreaRatio > SIDE_EXPAND_MAX_AREA_RATIO)
                    continue;

                Vector3D step = scan_.TriCenters[n] - curCenter;
                Double stepLen = step.Length;
                if (stepLen < 1e-12)
                    continue;
                step.Normalize();

                Double sideAlign = Vector3D.DotProduct(step, sideDir_);
                if (sideAlign <= SIDE_EXPAND_MIN_SIDE_ALIGN)
                    continue;

                Double normalSimilarity = ((seedCos + curCos) * 0.5 + 1.0) * 0.5;
                Double areaSimilarity = 1.0 - Math.Clamp(Math.Abs(Math.Log(curAreaRatio, 2.0)) / 2.0, 0.0, 1.0);
                Double score = (sideAlign * 0.55) + (normalSimilarity * 0.30) + (areaSimilarity * 0.15);
                accepted.Add((n, score));
            }
            accepted.Sort((a, b) => b.score.CompareTo(a.score));
            return [.. accepted.Select(x => x.tri)];
        }

        private static Point3D LiftTriangleCenter(ModelPartScanner.ScanResult scan_, Int32 tri_, Double lift_) {
            Point3D p = scan_.TriCenters[tri_];
            Vector3D n = tri_ >= 0 && tri_ < scan_.TriNormals.Count ? scan_.TriNormals[tri_] : new Vector3D(0, 0, 1);
            if (!TryNormalize(ref n))
                n = new Vector3D(0, 0, 1);
            return p + (n * lift_);
        }

        private static Boolean TryNormalize(ref Vector3D v_) {
            if (v_.LengthSquared < 1e-18)
                return false;
            v_.Normalize();
            return true;
        }

        private static List<Point3D> BuildEdgeBiasedPathPoints(ModelPartScanner.ScanResult scan_, IReadOnlyList<Int32> triPath_) {
            if (triPath_.Count < 2)
                return [];

            List<Point3D> centers = [.. triPath_.Select(t => scan_.TriCenters[t])];
            List<Point3D> left = [];
            List<Point3D> right = [];
            for (Int32 i = 0; i < triPath_.Count; i++) {
                Int32 tri = triPath_[i];
                if (!TryGetTriangleVertices(scan_, tri, out Point3D p0, out Point3D p1, out Point3D p2)) {
                    left.Add(scan_.TriCenters[tri]);
                    right.Add(scan_.TriCenters[tri]);
                    continue;
                }

                Vector3D tangent;
                if (i == 0) tangent = centers[1] - centers[0];
                else if (i == triPath_.Count - 1) tangent = centers[^1] - centers[^2];
                else tangent = centers[i + 1] - centers[i - 1];
                if (tangent.LengthSquared < 1e-18)
                    tangent = new Vector3D(1, 0, 0);
                tangent.Normalize();

                Vector3D normal = scan_.TriNormals[tri];
                if (normal.LengthSquared < 1e-18)
                    normal = new Vector3D(0, 0, 1);
                normal.Normalize();

                Vector3D side = Vector3D.CrossProduct(normal, tangent);
                if (side.LengthSquared < 1e-18)
                    side = new Vector3D(1, 0, 0);
                side.Normalize();

                Point3D c = scan_.TriCenters[tri];
                (Point3D lpt, Point3D rpt) = PickExtremePointsBySide(c, side, p0, p1, p2);
                left.Add(lpt);
                right.Add(rpt);
            }

            Double leftLen = PolylineLength(left);
            Double rightLen = PolylineLength(right);
            return leftLen >= rightLen ? left : right;
        }

        private static (Point3D left, Point3D right) PickExtremePointsBySide(
            Point3D center_,
            Vector3D side_,
            Point3D a_,
            Point3D b_,
            Point3D c_) {
            Point3D[] pts = [a_, b_, c_];
            Int32 li = 0, ri = 0;
            Double maxProj = Double.NegativeInfinity;
            Double minProj = Double.PositiveInfinity;
            for (Int32 i = 0; i < pts.Length; i++) {
                Double proj = Vector3D.DotProduct(pts[i] - center_, side_);
                if (proj > maxProj) {
                    maxProj = proj;
                    li = i;
                }
                if (proj < minProj) {
                    minProj = proj;
                    ri = i;
                }
            }
            return (pts[li], pts[ri]);
        }

        private static Double PolylineLength(IReadOnlyList<Point3D> pts_) {
            if (pts_.Count < 2) return 0;
            Double s = 0;
            for (Int32 i = 1; i < pts_.Count; i++)
                s += (pts_[i] - pts_[i - 1]).Length;
            return s;
        }

        private static Boolean TryGetTriangleVertices(
            ModelPartScanner.ScanResult scan_,
            Int32 globalTri_,
            out Point3D p0_,
            out Point3D p1_,
            out Point3D p2_) {
            p0_ = p1_ = p2_ = new Point3D();
            if (globalTri_ < 0) return false;
            foreach (ModelPartScanner.MeshSlot slot in scan_.MeshSlots) {
                Int32 start = slot.FirstGlobalTri;
                Int32 end = start + slot.TriCount;
                if (globalTri_ < start || globalTri_ >= end)
                    continue;
                Int32 localTri = globalTri_ - start;
                if (!TryTriVerts(slot.Mesh, localTri, out Int32 a, out Int32 b, out Int32 c) || slot.Mesh.Positions is null)
                    return false;
                p0_ = slot.Mesh.Positions[a];
                p1_ = slot.Mesh.Positions[b];
                p2_ = slot.Mesh.Positions[c];
                return true;
            }
            return false;
        }

        private static Boolean TryTriVerts(MeshGeometry3D mesh_, Int32 tri_, out Int32 a_, out Int32 b_, out Int32 c_) {
            a_ = b_ = c_ = 0;
            Int32Collection? idx = mesh_.TriangleIndices;
            if (idx is { Count: > 0 }) {
                Int32 i3 = tri_ * 3;
                if (i3 + 2 >= idx.Count) return false;
                a_ = idx[i3];
                b_ = idx[i3 + 1];
                c_ = idx[i3 + 2];
                return true;
            }
            if (mesh_.Positions is null) return false;
            Int32 i = tri_ * 3;
            if (i + 2 >= mesh_.Positions.Count) return false;
            a_ = i;
            b_ = i + 1;
            c_ = i + 2;
            return true;
        }

        private Boolean DrawRegions(ModelPartScanner.ScanResult scan_, Double spacingInch_, IEnumerable<IReadOnlyList<Int32>> regions_) {
            ClearTransitionCenterline();
            if (scan_.MeshSlots.Count == 0 || scan_.TransitionGlobalTriangles.Count == 0) return false;
            if (scan_.TriCenters.Count == 0 || scan_.TriNormals.Count == 0 || scan_.TriAdjacency.Count == 0) return false;
            Point3DCollection lineSegments = [];
            Point3DCollection sampledPoints = [];
            Point3D globalCenter = scan_.ModelCenter;
            Rect3D bounds = scan_.MeshSlots.Count > 0 ? scan_.MeshSlots[0].OwnerModel.Bounds : new Rect3D();
            Double diag = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ);
            // Keep probe path nearly on-surface: tiny outward clearance only.
            Double lift = Math.Max(0.0002, diag * 0.00003);

            foreach (IReadOnlyList<Int32> region in regions_) {
                if (region.Count < 2) continue;
                if (!TryOrderTransitionCentersAlongStrip(region, scan_, out List<Int32>? triPath) || triPath is null)
                    continue;
                List<Point3D> centroidsInOrder = [.. triPath.Select(t => scan_.TriCenters[t])];
                List<Vector3D> normalsInOrder = [.. triPath.Select(t => scan_.TriNormals[t])];
                if (!TryBuildPolylineFromOrderedCenters(centroidsInOrder, Math.Max(0.01, spacingInch_), out List<Point3D>? samples))
                    continue;
                for (Int32 i = 0; i < samples!.Count; i++) {
                    samples[i] = MovePointOutsideSurfaceWithClearance(
                        samples[i],
                        centroidsInOrder,
                        normalsInOrder,
                        globalCenter,
                        scan_.ModelAxis,
                        lift,
                        Math.Max(lift * 1.5, diag * 0.00008));
                }
                AddPolylineAsSegments(lineSegments, samples!);
                foreach (Point3D p in samples!)
                    sampledPoints.Add(p);
            }

            if (lineSegments.Count >= 2 && sampledPoints.Count > 0) {
                _pathActiveLine = [];
                _pathActivePoints = [];
                _pathCommittedLine.Clear();
                _pathCommittedPoints.Clear();
                foreach (Point3D p in lineSegments)
                    _pathCommittedLine.Add(p);
                foreach (Point3D p in sampledPoints)
                    _pathCommittedPoints.Add(p);
                _lineVisual.Points = new Point3DCollection(_pathCommittedLine);
                _pointVisual.Points = new Point3DCollection(_pathCommittedPoints);
            }
            return lineSegments.Count >= 2 && sampledPoints.Count > 0;
        }

        private static Point3D MovePointOutsideSurfaceWithClearance(
            Point3D sample_,
            IReadOnlyList<Point3D> orderedCenters_,
            IReadOnlyList<Vector3D> orderedNormals_,
            Point3D modelCenter_,
            Vector3D modelAxis_,
            Double lift_,
            Double minSurfaceClearance_) {
            Vector3D outward = GetOutsideDirectionForSample(sample_, orderedCenters_, orderedNormals_, modelCenter_, modelAxis_);
            Point3D moved = sample_ + (outward * lift_);

            // Guard 1: do not move inward toward shaft axis.
            Double r0 = RadialDistanceFromAxis(sample_, modelCenter_, modelAxis_);
            Double r1 = RadialDistanceFromAxis(moved, modelCenter_, modelAxis_);
            if (r1 <= r0 + 1e-9) {
                Vector3D radialOut = ComputeRadialOutward(sample_, modelCenter_, modelAxis_);
                moved = sample_ + (radialOut * lift_);
                outward = radialOut;
            }

            // Guard 2: keep point in front of nearest local surface center along outward direction.
            if (orderedCenters_.Count > 0) {
                Int32 nearest = 0;
                Double bestD2 = Double.MaxValue;
                for (Int32 i = 0; i < orderedCenters_.Count; i++) {
                    Vector3D d = orderedCenters_[i] - sample_;
                    Double d2 = d.LengthSquared;
                    if (d2 < bestD2) {
                        bestD2 = d2;
                        nearest = i;
                    }
                }
                Point3D nearestCenter = orderedCenters_[nearest];
                Double alongOut = Vector3D.DotProduct(moved - nearestCenter, outward);
                if (alongOut < minSurfaceClearance_)
                    moved += outward * (minSurfaceClearance_ - alongOut);
            }
            return moved;
        }

        public Boolean TryGetDisplayedPointSpacing(out Double spacingInch_) {
            spacingInch_ = 0;
            if (_lastPathSpacingInch > 0) {
                spacingInch_ = _lastPathSpacingInch;
                return true;
            }
            Point3DCollection? pts = _pointVisual.Points;
            if (pts is null || pts.Count < 2) return false;

            List<Double> deltas = [];
            for (Int32 i = 1; i < pts.Count; i++) {
                Double d = (pts[i] - pts[i - 1]).Length;
                if (d > 1e-9) deltas.Add(d);
            }

            if (deltas.Count == 0) return false;
            deltas.Sort();

            // Keep only the lower-step band to avoid large jumps between separately drawn line chunks.
            Int32 keepCount = Math.Max(1, (Int32)Math.Ceiling(deltas.Count * 0.7));
            List<Double> smallSteps = deltas.GetRange(0, keepCount);
            Int32 m = smallSteps.Count / 2;
            spacingInch_ = (smallSteps.Count % 2) == 1 ? smallSteps[m] : ((smallSteps[m - 1] + smallSteps[m]) * 0.5);
            return Double.IsFinite(spacingInch_) && spacingInch_ > 0;
        }

        /// <summary>Orders transition triangle <em>centroids</em> so the polyline follows the curved band on the surface:
        /// first walk along true mesh adjacency (same adjacency the scanner used); if the strip is not a simple path, fall back
        /// to nearest-neighbor chaining with jump guards (avoids connecting across blade body).
        /// </summary>
        private static Boolean TryOrderTransitionCentersAlongStrip(
            IReadOnlyList<Int32> region_,
            ModelPartScanner.ScanResult scan_,
            out List<Int32>? orderedTriIds_) {
            orderedTriIds_ = null;
            IReadOnlyList<Point3D> cAll = scan_.TriCenters;
            IReadOnlyList<IReadOnlyList<Int32>> triAdj = scan_.TriAdjacency;
            if (region_.Count < 2 || cAll.Count == 0 || triAdj.Count == 0) return false;
            Int32 nTri = cAll.Count;
            HashSet<Int32> rSet = [.. region_.Where(t => t >= 0 && t < nTri)];

            if (rSet.Count < 2) return false;
            if (TryOrderByMeshStripWalk(rSet, cAll, triAdj, out List<Int32>? triPath) && triPath is not null) {
                orderedTriIds_ = triPath;
                return true;
            }
            if (TryOrderByNearestNeighborSurface(rSet, cAll, out orderedTriIds_) && orderedTriIds_ is not null)
                return true;
            return false;
        }

        /// <summary>Walk the strip using mesh edges. Prefers a simple path; otherwise greedily extends using forward direction.
        /// </summary>
        private static Boolean TryOrderByMeshStripWalk(
            HashSet<Int32> rSet_,
            IReadOnlyList<Point3D> cAll_,
            IReadOnlyList<IReadOnlyList<Int32>> triAdj_,
            out List<Int32>? path_) {
            path_ = null;
            if (rSet_.Count < 2) return false;
            static Int32 NbrInR(Int32 t_, HashSet<Int32> r_, IReadOnlyList<IReadOnlyList<Int32>> a_) {
                Int32 c = 0;
                if (t_ < 0 || t_ >= a_.Count) return 0;
                foreach (Int32 n in a_[t_])
                    if (r_.Contains(n)) c++;
                return c;
            }
            static List<Int32> NeighborsInRegion(Int32 t_, HashSet<Int32> rSet_, IReadOnlyList<IReadOnlyList<Int32>> triAdj_) {
                if (t_ < 0 || t_ >= triAdj_.Count) return [];
                List<Int32> out_ = [];
                foreach (Int32 n in triAdj_[t_]) {
                    if (rSet_.Contains(n)) out_.Add(n);
                }
                return out_;
            }
            static Boolean BuildFromStart(
                Int32 start_,
                HashSet<Int32> rSet_,
                IReadOnlyList<Point3D> cAll_,
                IReadOnlyList<IReadOnlyList<Int32>> triAdj_,
                out List<Int32>? path_) {
                path_ = [start_];
                HashSet<Int32> vis = [start_];
                Int32 cur = start_;
                while (vis.Count < rSet_.Count) {
                    List<Int32> cand = [.. NeighborsInRegion(cur, rSet_, triAdj_).Where(n => !vis.Contains(n))];
                    if (cand.Count == 0) {
                        path_ = null;
                        return false;
                    }
                    Int32? next;
                    if (cand.Count == 1) {
                        next = cand[0];
                    } else {
                        Vector3D forward;
                        if (path_.Count >= 2) {
                            forward = cAll_[cur] - cAll_[path_[^2]];
                        } else {
                            forward = cAll_[cand[0]] - cAll_[cur];
                        }
                        if (forward.LengthSquared < 1e-18) {
                            next = cand.OrderBy(n => n).First();
                        } else {
                            forward.Normalize();
                            next = cand.OrderByDescending(n => {
                                Vector3D step = cAll_[n] - cAll_[cur];
                                Double l = step.Length;
                                if (l < 1e-12) return -2.0;
                                step = step * (1.0 / l);
                                return Vector3D.DotProduct(step, forward);
                            }).First();
                        }
                    }
                    path_.Add(next.Value);
                    vis.Add(next.Value);
                    cur = next.Value;
                }
                return true;
            }
            List<Int32> endCandidates = [.. rSet_.Where(t => NbrInR(t, rSet_, triAdj_) == 1)];
            List<Int32> tryStarts;
            if (endCandidates.Count > 0) {
                tryStarts = [.. endCandidates.Distinct().Take(4)];
            } else {
                Int32 minDeg = rSet_.Min(t => NbrInR(t, rSet_, triAdj_));
                tryStarts = [.. rSet_.Where(t => NbrInR(t, rSet_, triAdj_) == minDeg).OrderBy(t => t).Take(3)];
            }
            if (tryStarts.Count == 0)
                tryStarts = [rSet_.OrderBy(t => t).First()];
            foreach (Int32 st in tryStarts) {
                if (BuildFromStart(st, rSet_, cAll_, triAdj_, out path_) && path_ is not null && path_.Count == rSet_.Count)
                    return true;
            }
            path_ = null;
            return false;
        }

        private static Boolean TryOrderByNearestNeighborSurface(
            HashSet<Int32> rSet_,
            IReadOnlyList<Point3D> cAll_,
            out List<Int32>? ordered_) {
            ordered_ = null;
            if (rSet_.Count < 2) return false;

            List<Int32> nodes = [.. rSet_];
            List<Double> nearestDists = [];
            foreach (Int32 a in nodes) {
                Double best = Double.MaxValue;
                Point3D pa = cAll_[a];
                foreach (Int32 b in nodes) {
                    if (a == b) continue;
                    Double d = (cAll_[b] - pa).Length;
                    if (d < best) best = d;
                }
                if (Double.IsFinite(best) && best < Double.MaxValue)
                    nearestDists.Add(best);
            }
            Double typicalStep = nearestDists.Count > 0 ? Median(nearestDists) : 0.0;
            Double maxJump = typicalStep > 1e-9 ? typicalStep * 2.8 : Double.MaxValue;

            // Start from one end of dominant spread, then greedily connect to nearest unvisited point.
            List<Point3D> pts = [.. nodes.Select(t => cAll_[t])];
            if (!TryDominantPca3(pts, out Vector3D axisW)) return false;
            Point3D mean = new(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
            Int32 start = nodes.OrderBy(t => Vector3D.DotProduct(cAll_[t] - mean, axisW)).First();

            ordered_ = [start];
            HashSet<Int32> unvisited = [.. nodes];
            unvisited.Remove(start);
            Int32 cur = start;
            while (unvisited.Count > 0) {
                Point3D pCur = cAll_[cur];
                Int32 nearest = -1;
                Double nearestDist = Double.MaxValue;
                Int32 guarded = -1;
                Double guardedDist = Double.MaxValue;
                foreach (Int32 cand in unvisited) {
                    Double d = (cAll_[cand] - pCur).Length;
                    if (d < nearestDist) {
                        nearestDist = d;
                        nearest = cand;
                    }
                    if (d <= maxJump && d < guardedDist) {
                        guardedDist = d;
                        guarded = cand;
                    }
                }
                Int32 next = guarded >= 0 ? guarded : nearest;
                if (next < 0) return false;
                ordered_.Add(next);
                unvisited.Remove(next);
                cur = next;
            }
            return true;
        }

        private static Double Median(IReadOnlyList<Double> vals_) {
            if (vals_.Count == 0) return 0;
            List<Double> a = [.. vals_.OrderBy(v => v)];
            Int32 m = a.Count / 2;
            return (a.Count % 2) == 1 ? a[m] : ((a[m - 1] + a[m]) * 0.5);
        }

        private static Vector3D GetOutsideDirectionForSample(
            Point3D sample_,
            IReadOnlyList<Point3D> orderedCenters_,
            IReadOnlyList<Vector3D> orderedNormals_,
            Point3D modelCenter_,
            Vector3D modelAxis_) {
            if (orderedCenters_.Count == 0 || orderedNormals_.Count == 0) {
                return ComputeRadialOutward(sample_, modelCenter_, modelAxis_);
            }

            Int32 best = 0;
            Double bestD2 = Double.MaxValue;
            for (Int32 i = 0; i < orderedCenters_.Count; i++) {
                Vector3D d = orderedCenters_[i] - sample_;
                Double d2 = d.LengthSquared;
                if (d2 < bestD2) {
                    bestD2 = d2;
                    best = i;
                }
            }

            Vector3D n = best < orderedNormals_.Count ? orderedNormals_[best] : new Vector3D(0, 0, 1);
            if (n.LengthSquared < 1e-18) n = new Vector3D(0, 0, 1);
            n.Normalize();

            // Enforce outward trend from shaft axis (probe must stay outside hub/blade body).
            Vector3D radialOut = ComputeRadialOutward(orderedCenters_[best], modelCenter_, modelAxis_);
            if (Vector3D.DotProduct(n, radialOut) < 0)
                n = -n;

            Vector3D mixed = n + (radialOut * 1.25);
            if (mixed.LengthSquared < 1e-18)
                return radialOut;
            mixed.Normalize();
            return mixed;
        }

        private static Double RadialDistanceFromAxis(Point3D p_, Point3D center_, Vector3D axis_) {
            Vector3D ax = axis_;
            if (ax.LengthSquared < 1e-18) ax = new Vector3D(0, 0, 1);
            ax.Normalize();
            Vector3D d = p_ - center_;
            Double axial = Vector3D.DotProduct(d, ax);
            Vector3D rv = d - (ax * axial);
            return rv.Length;
        }

        private static Vector3D ComputeRadialOutward(Point3D p_, Point3D center_, Vector3D axis_) {
            Vector3D ax = axis_;
            if (ax.LengthSquared < 1e-18) ax = new Vector3D(0, 0, 1);
            ax.Normalize();
            Vector3D d = p_ - center_;
            Double axial = Vector3D.DotProduct(d, ax);
            Vector3D rv = d - (ax * axial);
            if (rv.LengthSquared < 1e-18) {
                Vector3D fallback = p_ - center_;
                if (fallback.LengthSquared < 1e-18) fallback = new Vector3D(0, 0, 1);
                fallback.Normalize();
                return fallback;
            }
            rv.Normalize();
            return rv;
        }

        private static Boolean TryDominantPca3(IReadOnlyList<Point3D> points_, out Vector3D axisW_) {
            axisW_ = new Vector3D(1, 0, 0);
            if (points_.Count < 2) return false;
            Double mx = points_.Average(p => p.X), my = points_.Average(p => p.Y), mz = points_.Average(p => p.Z);
            Double c00 = 0, c11 = 0, c22 = 0, c01 = 0, c02 = 0, c12 = 0;
            Int32 n = points_.Count;
            foreach (Point3D p in points_) {
                Double x = p.X - mx, y = p.Y - my, z = p.Z - mz;
                c00 += x * x; c11 += y * y; c22 += z * z; c01 += x * y; c02 += x * z; c12 += y * z;
            }
            c00 /= n; c11 /= n; c22 /= n; c01 /= n; c02 /= n; c12 /= n;
            Vector3D v = new(1, 0, 0);
            for (Int32 it = 0; it < 32; it++) {
                Double vx = v.X, vy = v.Y, vz = v.Z;
                Double nx = (c00 * vx) + (c01 * vy) + (c02 * vz);
                Double ny = (c01 * vx) + (c11 * vy) + (c12 * vz);
                Double nz = (c02 * vx) + (c12 * vy) + (c22 * vz);
                v = new Vector3D(nx, ny, nz);
                Double len = v.Length;
                if (len < 1e-12) { axisW_ = new Vector3D(1, 0, 0); return true; }
                v = v * (1.0 / len);
            }
            axisW_ = v;
            return true;
        }

        private static Boolean TryBuildPolylineFromOrderedCenters(
            IReadOnlyList<Point3D> centroidsInOrder_,
            Double spacingInch_,
            out List<Point3D>? samples_) {
            samples_ = null;
            if (centroidsInOrder_.Count < 2) return false;
            Int32 smoothRadius = Math.Clamp(centroidsInOrder_.Count / 18, 1, 3);
            List<Point3D> smooth = SmoothPathPreservingEndpoints(centroidsInOrder_, smoothRadius);
            smooth = SmoothPathPreservingEndpoints(smooth, smoothRadius);
            if (!TryResamplePath(smooth, spacingInch_, out samples_))
                return false;
            // Resampling + smoothing must not pull the visible line off the strip ends (blade root / tip edge points).
            samples_![0] = centroidsInOrder_[0];
            samples_[samples_.Count - 1] = centroidsInOrder_[centroidsInOrder_.Count - 1];
            return samples_.Count > 1;
        }

        private static void AddPolylineAsSegments(Point3DCollection out_, IReadOnlyList<Point3D> line_) {
            for (Int32 i = 0; i + 1 < line_.Count; i++) {
                out_.Add(line_[i]);
                out_.Add(line_[i + 1]);
            }
        }

        /// <summary>
        /// Moving-average smooth along the strip while keeping the first and last points fixed so the drawn path
        /// still begins and ends on the actual root/tip edge samples (see <see cref="BuildEdgeBiasedPathPoints"/>).
        /// </summary>
        private static List<Point3D> SmoothPathPreservingEndpoints(IReadOnlyList<Point3D> pts_, Int32 radius_) {
            if (pts_.Count <= 2 || radius_ <= 0) return [.. pts_];
            Int32 last = pts_.Count - 1;
            List<Point3D> outPts = [];
            for (Int32 i = 0; i < pts_.Count; i++) {
                if (i == 0 || i == last) {
                    outPts.Add(pts_[i]);
                    continue;
                }
                Int32 i0 = Math.Max(0, i - radius_);
                Int32 i1 = Math.Min(last, i + radius_);
                Double sx = 0, sy = 0, sz = 0;
                Int32 n = 0;
                for (Int32 j = i0; j <= i1; j++) {
                    sx += pts_[j].X; sy += pts_[j].Y; sz += pts_[j].Z; n++;
                }
                outPts.Add(new Point3D(sx / n, sy / n, sz / n));
            }
            return outPts;
        }

        private static Boolean TryResamplePath(IReadOnlyList<Point3D> path_, Double spacing_, out List<Point3D>? sampled_) {
            sampled_ = null;
            if (path_.Count < 2 || spacing_ <= 0) return false;
            List<Double> cum = [0];
            Double total = 0;
            for (Int32 i = 1; i < path_.Count; i++) {
                total += (path_[i] - path_[i - 1]).Length;
                cum.Add(total);
            }
            if (total < 1e-6) return false;
            sampled_ = [];
            for (Double s = 0; s <= total + 1e-9; s += spacing_) {
                Int32 seg = 0;
                while (seg + 1 < cum.Count && cum[seg + 1] < s) seg++;
                if (seg + 1 >= cum.Count) {
                    sampled_.Add(path_[^1]);
                    continue;
                }
                Double segLen = Math.Max(1e-9, cum[seg + 1] - cum[seg]);
                Double t = (s - cum[seg]) / segLen;
                Point3D a = path_[seg];
                Point3D b = path_[seg + 1];
                sampled_.Add(new Point3D(
                    a.X + ((b.X - a.X) * t),
                    a.Y + ((b.Y - a.Y) * t),
                    a.Z + ((b.Z - a.Z) * t)));
            }
            return sampled_.Count > 1;
        }

        private static List<SensorAxisSample> BuildFiveAxisSamples(IReadOnlyList<Point3D> points_, IReadOnlyList<Vector3D> dirs_, Int32 centerIdx_) {
            if (points_.Count == 0 || dirs_.Count == 0) return [];
            Int32 take = Math.Min(5, Math.Min(points_.Count, dirs_.Count));
            Int32 start = Math.Max(0, centerIdx_ - (take / 2));
            Int32 end = Math.Min(points_.Count, start + take);
            if (end - start < take) start = Math.Max(0, end - take);
            List<SensorAxisSample> list = [];
            Int32 outIdx = 1;
            for (Int32 i = start; i < end; i++) {
                list.Add(ToAxisSample(outIdx++, points_[i], dirs_[i]));
            }
            return list;
        }

        private static SensorAxisSample ToAxisSample(Int32 index_, Point3D point_, Vector3D dir_) {
            Vector3D n = dir_;
            if (n.LengthSquared < 1e-18) n = new Vector3D(0, 0, 1);
            n.Normalize();

            // Two orientation axes (A/B) derived from the probe direction vector.
            Double a = Math.Atan2(n.Y, n.Z) * (180.0 / Math.PI);
            Double b = Math.Atan2(-n.X, Math.Sqrt((n.Y * n.Y) + (n.Z * n.Z))) * (180.0 / Math.PI);

            return new SensorAxisSample(index_, point_.X, point_.Y, point_.Z, a, b);
        }

        private void AddSensorSampleIfNew(SensorAxisSample sample_) {
            const Double posTol2 = 1e-8;
            foreach (SensorAxisSample s in _allSensorAxisSamples) {
                Double dx = s.X - sample_.X;
                Double dy = s.Y - sample_.Y;
                Double dz = s.Z - sample_.Z;
                if (((dx * dx) + (dy * dy) + (dz * dz)) <= posTol2)
                    return;
            }
            _allSensorAxisSamples.Add(sample_ with { Index = _allSensorAxisSamples.Count + 1 });
        }

        private void UpdateProbeVisual(Point3D pos_, Vector3D dir_, Double modelDiag_) {
            _lastProbePosition = pos_;
            _lastProbeDirection = dir_;
            _lastProbeDiag = modelDiag_;
            Vector3D z = dir_;
            if (z.LengthSquared < 1e-18) z = new Vector3D(0, 0, 1);
            z.Normalize();
            Vector3D helper = Math.Abs(z.X) < 0.8 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
            Vector3D x = Vector3D.CrossProduct(helper, z);
            if (x.LengthSquared < 1e-18) x = new Vector3D(1, 0, 0);
            x.Normalize();
            Vector3D y = Vector3D.CrossProduct(z, x);
            y.Normalize();

            Double axisLen = Math.Max(0.02, modelDiag_ * 0.015);
            _probeXVisual.Points = [pos_, pos_ + (x * axisLen)];
            _probeYVisual.Points = [pos_, pos_ + (y * axisLen)];
            _probeZVisual.Points = [pos_, pos_ + (z * axisLen)];

            // Quarter-cylinder probe body using physical-ish dimensions based on model scale.
            Double radius = Math.Max(0.01, modelDiag_ * 0.008);
            Double length = Math.Max(0.06, modelDiag_ * 0.045);
            Point3D bodyOrigin = pos_ + (z * (radius * 0.7));
            MeshGeometry3D bodyMesh = BuildQuarterCylinderMesh(bodyOrigin, x, y, z, radius, length, 24);
            _probeBodyVisual.Content = new GeometryModel3D {
                Geometry = bodyMesh,
                Material = MaterialHelper.CreateMaterial(Color.FromRgb(0xFF, 0x8C, 0x00)),
                BackMaterial = MaterialHelper.CreateMaterial(Color.FromRgb(0xCC, 0x6E, 0x00))
            };
        }

        private static MeshGeometry3D BuildQuarterCylinderMesh(
            Point3D origin_,
            Vector3D xAxis_,
            Vector3D yAxis_,
            Vector3D zAxis_,
            Double radius_,
            Double length_,
            Int32 arcSegs_) {
            Vector3D x = xAxis_;
            Vector3D y = yAxis_;
            Vector3D z = zAxis_;
            if (x.LengthSquared < 1e-18) x = new Vector3D(1, 0, 0);
            if (y.LengthSquared < 1e-18) y = new Vector3D(0, 1, 0);
            if (z.LengthSquared < 1e-18) z = new Vector3D(0, 0, 1);
            x.Normalize(); y.Normalize(); z.Normalize();

            Point3D L(Double px, Double py, Double pz) => origin_ + (x * px) + (y * py) + (z * pz);
            Point3DCollection pos = [];
            Int32Collection idx = [];
            static void AddTri(Point3DCollection p_, Int32Collection i_, Point3D a_, Point3D b_, Point3D c_) {
                Int32 s = p_.Count;
                p_.Add(a_); p_.Add(b_); p_.Add(c_);
                i_.Add(s); i_.Add(s + 1); i_.Add(s + 2);
            }
            static void AddQuad(Point3DCollection p_, Int32Collection i_, Point3D a_, Point3D b_, Point3D c_, Point3D d_) {
                AddTri(p_, i_, a_, b_, c_);
                AddTri(p_, i_, a_, c_, d_);
            }
            Int32 segs = Math.Max(4, arcSegs_);
            Double step = (Math.PI * 0.5) / segs;

            // Curved shell.
            for (Int32 i = 0; i < segs; i++) {
                Double a0 = i * step;
                Double a1 = (i + 1) * step;
                Point3D p00 = L(Math.Cos(a0) * radius_, Math.Sin(a0) * radius_, 0);
                Point3D p01 = L(Math.Cos(a0) * radius_, Math.Sin(a0) * radius_, length_);
                Point3D p10 = L(Math.Cos(a1) * radius_, Math.Sin(a1) * radius_, 0);
                Point3D p11 = L(Math.Cos(a1) * radius_, Math.Sin(a1) * radius_, length_);
                AddQuad(pos, idx, p00, p10, p11, p01);
            }

            // Side planes at x=0 and y=0.
            AddQuad(pos, idx, L(0, 0, 0), L(0, radius_, 0), L(0, radius_, length_), L(0, 0, length_));
            AddQuad(pos, idx, L(0, 0, 0), L(radius_, 0, 0), L(radius_, 0, length_), L(0, 0, length_));

            // End caps (quarter disks).
            Point3D c0 = L(0, 0, 0);
            Point3D c1 = L(0, 0, length_);
            for (Int32 i = 0; i < segs; i++) {
                Double a0 = i * step;
                Double a1 = (i + 1) * step;
                Point3D s0 = L(Math.Cos(a0) * radius_, Math.Sin(a0) * radius_, 0);
                Point3D s1 = L(Math.Cos(a1) * radius_, Math.Sin(a1) * radius_, 0);
                Point3D e0 = L(Math.Cos(a0) * radius_, Math.Sin(a0) * radius_, length_);
                Point3D e1 = L(Math.Cos(a1) * radius_, Math.Sin(a1) * radius_, length_);
                AddTri(pos, idx, c0, s1, s0);
                AddTri(pos, idx, c1, e0, e1);
            }

            return new MeshGeometry3D { Positions = pos, TriangleIndices = idx };
        }

    }
}
