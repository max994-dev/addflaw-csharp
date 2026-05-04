using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AddFlaw.Geometry {
    public static class ModelPartScanner {
        private const Double HUB_QMIN = 0.42;
        private const Double HUB_QMAX = 0.74;
        private const Double SEAM_MIN_FACTOR = 0.90;
        private const Double SEAM_MAX_FACTOR = 1.03;
        private const Int32 MAX_TRANSITION_REGIONS = 16;
        private const Int32 MAX_BLADE_COMPONENTS = 12;

        public sealed class MeshSlot {
            public required Int32 SlotIndex { get; init; }
            public required MeshGeometry3D Mesh { get; init; }
            public required GeometryModel3D OwnerModel { get; init; }
            public Model3DGroup? ParentGroup { get; init; }
            public required Int32 ParentChildIndex { get; init; }
            public required Int32 FirstGlobalTri { get; init; }
            public required Int32 TriCount { get; init; }
        }

        public sealed record ScanResult(
            Int32 TotalTriangles,
            Int32 HubTriangles,
            Int32 BladeTriangles,
            Int32 BladeRegions,
            Int32 TransitionTriangles,
            Int32 TransitionRegions,
            IReadOnlyList<Int32> HubGlobalTriangles,
            IReadOnlyList<IReadOnlyList<Int32>> BladeRegionTriangles,
            IReadOnlyList<IReadOnlyList<Int32>> TransitionRegionTriangles,
            IReadOnlyList<Int32> HubSideTransitionGlobalTriangles,
            IReadOnlyList<Int32> BladeSideTransitionGlobalTriangles,
            IReadOnlyList<Int32> TransitionGlobalTriangles,
            IReadOnlyList<MeshSlot> MeshSlots,
            IReadOnlyList<Point3D> TriCenters,
            IReadOnlyList<Vector3D> TriNormals,
            IReadOnlyList<Double> TriAreas,
            IReadOnlyList<IReadOnlyList<Int32>> TriAdjacency,
            Point3D ModelCenter,
            Vector3D ModelAxis);

        private sealed class TriData {
            public required Point3D Center;
            public required Vector3D Normal;
            public required Double Radial;
            public required Double Area;
        }

        public static ScanResult Scan(Model3D? root_) {
            if (root_ is null)
                return new ScanResult(0, 0, 0, 0, 0, 0, [], [], [], [], [], [], [], [], [], [], [], new Point3D(), new Vector3D(0, 0, 1));

            List<MeshSlot> slots = BuildSlots(root_);
            if (slots.Count == 0)
                return new ScanResult(0, 0, 0, 0, 0, 0, [], [], [], [], [], [], [], [], [], [], [], new Point3D(), new Vector3D(0, 0, 1));

            if (!BuildTriangles(slots, out List<TriData> tris, out Point3D center))
                return new ScanResult(0, 0, 0, 0, 0, 0, [], [], [], [], [], [], slots, [], [], [], [], new Point3D(), new Vector3D(0, 0, 1));

            Rect3D bounds = root_.Bounds;
            Vector3D axis = SmallestAxis(bounds);
            IReadOnlyList<Point3D> triCenterList = [.. tris.Select(t => t.Center)];
            IReadOnlyList<Vector3D> triNormalList = [.. tris.Select(t => t.Normal)];
            IReadOnlyList<Double> triAreaList = [.. tris.Select(t => t.Area)];
            foreach (TriData t in tris) {
                Vector3D d = t.Center - center;
                Double axial = Vector3D.DotProduct(d, axis);
                Vector3D rv = d - (axis * axial);
                t.Radial = rv.Length;
            }

            Double[] radialSorted = [.. tris.Select(t => t.Radial).OrderBy(v => v)];
            Double fallbackHub = Quantile(radialSorted, 0.70);
            Double hubRadius = EstimateHubRadiusByCircularCoverage(tris, center, axis, radialSorted, fallbackHub);
            hubRadius = Math.Clamp(hubRadius, Quantile(radialSorted, HUB_QMIN), Quantile(radialSorted, HUB_QMAX));

            Double diagonal = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ);
            Double eps = Math.Max(diagonal * 1e-5, 1e-7);
            List<Int32>[] adj = BuildAdjacency(slots, tris.Count, eps);

            // Hub = central circularly-continuous cylinder region found by radial scan from center.
            Boolean[] inHub = new Boolean[tris.Count];
            List<List<Int32>> hubCandidates = BuildComponents(tris.Count, adj, i => tris[i].Radial <= hubRadius);
            if (hubCandidates.Count > 0) {
                Int32 centerSeed = Enumerable.Range(0, tris.Count).OrderBy(i => tris[i].Radial).First();
                List<Int32>? picked = hubCandidates.FirstOrDefault(c => c.Contains(centerSeed));
                if (picked is null) {
                    picked = hubCandidates
                        .OrderBy(c => c.Average(i => tris[i].Radial))
                        .ThenByDescending(c => c.Count)
                        .First();
                }
                foreach (Int32 t in picked) inHub[t] = true;
            }

            List<List<Int32>> nonHub = BuildComponents(tris.Count, adj, i => !inHub[i]);
            List<IReadOnlyList<Int32>> bladeRegions = SelectBladeRegions(nonHub, tris, hubRadius, MAX_BLADE_COMPONENTS);
            Boolean[] inBlade = new Boolean[tris.Count];
            foreach (IReadOnlyList<Int32> b in bladeRegions)
                foreach (Int32 t in b) inBlade[t] = true;

            Boolean[] isTransition = new Boolean[tris.Count];
            Double seamMin = hubRadius * SEAM_MIN_FACTOR;
            Double seamMax = hubRadius * SEAM_MAX_FACTOR;
            for (Int32 i = 0; i < tris.Count; i++) {
                if (tris[i].Radial < seamMin || tris[i].Radial > seamMax) continue;
                Boolean touchHub = false, touchBlade = false;
                foreach (Int32 n in adj[i]) {
                    if (inHub[n]) touchHub = true;
                    if (inBlade[n]) touchBlade = true;
                    if (touchHub && touchBlade) break;
                }
                if ((inHub[i] && touchBlade) || (inBlade[i] && touchHub) || (!inHub[i] && !inBlade[i] && touchHub && touchBlade))
                    isTransition[i] = true;
            }
            for (Int32 i = 0; i < tris.Count; i++) {
                if (isTransition[i]) continue;
                if (tris[i].Radial < seamMin || tris[i].Radial > seamMax) continue;
                Int32 transNbr = 0;
                foreach (Int32 n in adj[i]) if (isTransition[n]) transNbr++;
                if (transNbr >= 2) isTransition[i] = true;
            }

            List<List<Int32>> transComps = BuildComponents(tris.Count, adj, i => isTransition[i]);
            transComps = SelectTransitionPerBlade(transComps, bladeRegions, inHub, adj);
            transComps = KeepLongCurvedLinesOnly(transComps, tris, center, axis, adj, tris.Count);
            if (transComps.Count > MAX_TRANSITION_REGIONS)
                transComps = [.. transComps.OrderByDescending(c => c.Count).Take(MAX_TRANSITION_REGIONS)];
            HashSet<Int32> transSet = [];
            foreach (List<Int32> c in transComps)
                foreach (Int32 t in c) transSet.Add(t);

            // Ensure transition includes both hub-edge and blade-edge triangles.
            HashSet<Int32> hubSideTransition = [];
            HashSet<Int32> bladeSideTransition = [];
            foreach (Int32 t in transSet.ToArray()) {
                foreach (Int32 n in adj[t]) {
                    if (inHub[n] && tris[n].Radial >= hubRadius * 0.86 && tris[n].Radial <= hubRadius * 1.03)
                        hubSideTransition.Add(n);
                    if (inBlade[n] && tris[n].Radial >= hubRadius * 0.90 && tris[n].Radial <= hubRadius * 1.08)
                        bladeSideTransition.Add(n);
                }
            }

            // Move blade-side transition one ring further into blade interior.
            HashSet<Int32> bladeSideInner = [];
            foreach (Int32 t in bladeSideTransition) {
                foreach (Int32 n in adj[t]) {
                    if (!inBlade[n] || inHub[n]) continue;
                    if (tris[n].Radial < hubRadius * 0.92 || tris[n].Radial > hubRadius * 1.14) continue;
                    // Prefer triangles that are no longer directly on hub contact.
                    Boolean touchesHub = false;
                    foreach (Int32 m in adj[n]) {
                        if (inHub[m]) {
                            touchesHub = true;
                            break;
                        }
                    }
                    if (!touchesHub) bladeSideInner.Add(n);
                }
            }
            if (bladeSideInner.Count > 0)
                bladeSideTransition = bladeSideInner;

            foreach (Int32 t in hubSideTransition) transSet.Add(t);
            foreach (Int32 t in bladeSideTransition) transSet.Add(t);

            List<Int32> hubTriangles = [];
            List<Int32> bladeTriangles = [];
            for (Int32 i = 0; i < tris.Count; i++) {
                if (inHub[i]) hubTriangles.Add(i);
                if (inBlade[i]) bladeTriangles.Add(i);
            }

            IReadOnlyList<IReadOnlyList<Int32>> triAdj = [.. adj.Select(n => (IReadOnlyList<Int32>)n)];
            return new ScanResult(
                tris.Count,
                hubTriangles.Count,
                bladeTriangles.Count,
                bladeRegions.Count,
                transSet.Count,
                transComps.Count,
                [.. hubTriangles],
                bladeRegions,
                [.. transComps],
                [.. hubSideTransition.OrderBy(i => i)],
                [.. bladeSideTransition.OrderBy(i => i)],
                [.. transSet.OrderBy(i => i)],
                slots,
                triCenterList,
                triNormalList,
                triAreaList,
                triAdj,
                center,
                axis);
        }

        private static List<List<Int32>> SelectTransitionPerBlade(
            IReadOnlyList<List<Int32>> transComps_,
            IReadOnlyList<IReadOnlyList<Int32>> bladeRegions_,
            IReadOnlyList<Boolean> inHub_,
            IReadOnlyList<List<Int32>> adj_) {
            if (transComps_.Count == 0 || bladeRegions_.Count == 0)
                return [.. transComps_];

            Int32 triCount = inHub_.Count;
            Int32[] bladeIdByTri = new Int32[triCount];
            Array.Fill(bladeIdByTri, -1);
            for (Int32 b = 0; b < bladeRegions_.Count; b++) {
                foreach (Int32 t in bladeRegions_[b]) {
                    if (t >= 0 && t < triCount) bladeIdByTri[t] = b;
                }
            }

            // Build candidate component list per blade with score.
            List<(Int32 compId, Double score)>[] candidates = new List<(Int32 compId, Double score)>[bladeRegions_.Count];
            for (Int32 b = 0; b < candidates.Length; b++) candidates[b] = [];

            for (Int32 ci = 0; ci < transComps_.Count; ci++) {
                List<Int32> comp = transComps_[ci];
                if (comp.Count < 4) continue;
                Dictionary<Int32, Int32> bladeTouches = [];
                Int32 hubTouch = 0;
                foreach (Int32 t in comp) {
                    foreach (Int32 n in adj_[t]) {
                        if (inHub_[n]) hubTouch++;
                        Int32 bid = bladeIdByTri[n];
                        if (bid < 0) continue;
                        bladeTouches[bid] = bladeTouches.TryGetValue(bid, out Int32 c) ? c + 1 : 1;
                    }
                }
                if (hubTouch == 0) continue; // must include hub-end contact
                foreach ((Int32 bid, Int32 touch) in bladeTouches) {
                    // stronger blade contact + some hub contact + compact size
                    Double score = (touch * 4.0) + Math.Min(40, hubTouch) + Math.Min(20, comp.Count);
                    candidates[bid].Add((ci, score));
                }
            }

            foreach (List<(Int32 compId, Double score)> list in candidates)
                list.Sort((a, b) => b.score.CompareTo(a.score));

            // Greedy unique matching: one transition component per blade.
            HashSet<Int32> usedCompIds = [];
            List<List<Int32>> selected = [];
            for (Int32 b = 0; b < candidates.Length; b++) {
                (Int32 compId, Double score)? picked = null;
                foreach ((Int32 compId, Double score) c in candidates[b]) {
                    if (usedCompIds.Contains(c.compId)) continue;
                    picked = c;
                    break;
                }
                if (picked is null) continue;
                usedCompIds.Add(picked.Value.compId);
                selected.Add(transComps_[picked.Value.compId]);
            }

            if (selected.Count == 0)
                return [.. transComps_.OrderByDescending(c => c.Count).Take(bladeRegions_.Count)];

            return selected;
        }

        private static List<IReadOnlyList<Int32>> SelectBladeRegions(
            IReadOnlyList<List<Int32>> nonHub_,
            IReadOnlyList<TriData> tris_,
            Double hubRadius_,
            Int32 maxComponents_) {
            if (nonHub_.Count == 0) return [];
            List<(List<Int32> comp, Double meanR)> candidates = [];
            foreach (List<Int32> c in nonHub_) {
                if (c.Count < 20) continue;
                Double meanR = c.Average(i => tris_[i].Radial);
                if (meanR <= hubRadius_ * 1.02) continue;
                candidates.Add((c, meanR));
            }
            if (candidates.Count == 0) return [];
            candidates = [.. candidates.OrderByDescending(x => x.comp.Count).Take(Math.Max(1, maxComponents_))];
            Double medianCount = Median(candidates.Select(c => (Double)c.comp.Count));
            List<IReadOnlyList<Int32>> result = [];
            foreach ((List<Int32> comp, _) in candidates) {
                if (comp.Count >= medianCount * 0.45 && comp.Count <= medianCount * 2.3)
                    result.Add(comp);
            }
            if (result.Count == 0)
                result = [.. candidates.Select(c => (IReadOnlyList<Int32>)c.comp).Take(Math.Min(8, Math.Max(1, maxComponents_)))];
            return result;
        }

        private static List<List<Int32>> KeepLongCurvedLinesOnly(
            IReadOnlyList<List<Int32>> transComps_,
            IReadOnlyList<TriData> tris_,
            Point3D center_,
            Vector3D axis_,
            List<Int32>[] adj_,
            Int32 totalTriCount_) {
            if (transComps_.Count == 0) return [.. transComps_];

            (Vector3D u, Vector3D v) = BuildPerpendicularBasis(axis_);
            List<List<Int32>> result = [];

            foreach (List<Int32> comp in transComps_) {
                if (comp.Count < 10) {
                    result.Add(comp);
                    continue;
                }

                // Detect loop-like transition by angular wrap span.
                List<Double> angles = [];
                List<Double> axials = [];
                foreach (Int32 t in comp) {
                    Vector3D d = tris_[t].Center - center_;
                    Double x = Vector3D.DotProduct(d, u);
                    Double y = Vector3D.DotProduct(d, v);
                    Double a = Math.Atan2(y, x);
                    if (a < 0) a += 2.0 * Math.PI;
                    angles.Add(a);
                    axials.Add(Vector3D.DotProduct(d, axis_));
                }
                angles.Sort();
                Double maxGap = 0;
                for (Int32 i = 0; i < angles.Count; i++) {
                    Double a0 = angles[i];
                    Double a1 = (i == angles.Count - 1) ? angles[0] + (2.0 * Math.PI) : angles[i + 1];
                    maxGap = Math.Max(maxGap, a1 - a0);
                }
                Double span = (2.0 * Math.PI) - maxGap;
                Boolean loopLike = span > 5.1; // near-closed contour

                if (!loopLike) {
                    result.Add(comp);
                    continue;
                }

                // Remove short connector sides: keep only two long axial-side lines.
                Double[] axialSorted = [.. axials.OrderBy(v => v)];
                Double low = Quantile(axialSorted, 0.25);
                Double high = Quantile(axialSorted, 0.75);

                HashSet<Int32> keep = [];
                foreach (Int32 t in comp) {
                    Vector3D d = tris_[t].Center - center_;
                    Double a = Vector3D.DotProduct(d, axis_);
                    if (a <= low || a >= high)
                        keep.Add(t);
                }

                if (keep.Count < 6) {
                    result.Add(comp);
                    continue;
                }

                List<List<Int32>> split = BuildComponents(totalTriCount_, adj_, i => keep.Contains(i));
                split = [.. split.Where(c => c.Count >= 4).OrderByDescending(c => c.Count).Take(2)];
                if (split.Count == 0) {
                    result.Add(comp);
                } else if (split.Count == 1) {
                    result.Add(split[0]);
                } else {
                    // Two axial bands (typical: upper and lower edge of the hub–blade blend strip around the part).
                    // Keep the "top" band only: higher mean offset along the model axis.
                    List<Int32> topBand = split
                        .OrderByDescending(c => MeanAxialOffset(tris_, c, center_, axis_))
                        .First();
                    result.Add(topBand);
                }
            }

            return result;
        }

        /// <summary>Mean offset of triangle centers along <paramref name="axis_"/> (from <paramref name="center_"/>), used to pick the upper axial line when the transition splits into two.</summary>
        private static Double MeanAxialOffset(IReadOnlyList<TriData> tris_, IReadOnlyList<Int32> comp_, Point3D center_, Vector3D axis_) {
            if (comp_.Count == 0) return 0;
            Vector3D ax = axis_;
            if (ax.LengthSquared > 1e-30) ax.Normalize();
            Double sum = 0;
            foreach (Int32 t in comp_) {
                Vector3D d = tris_[t].Center - center_;
                sum += Vector3D.DotProduct(d, ax);
            }
            return sum / comp_.Count;
        }

        private static Boolean BuildTriangles(IReadOnlyList<MeshSlot> slots_, out List<TriData> tris_, out Point3D center_) {
            tris_ = [];
            center_ = new Point3D();
            Vector3D acc = new();
            Double areaSum = 0;
            foreach (MeshSlot slot in slots_) {
                for (Int32 t = 0; t < slot.TriCount; t++) {
                    if (!TryTriVerts(slot.Mesh, t, out Int32 a, out Int32 b, out Int32 c) || slot.Mesh.Positions is null)
                        continue;
                    Point3D p0 = slot.Mesh.Positions[a];
                    Point3D p1 = slot.Mesh.Positions[b];
                    Point3D p2 = slot.Mesh.Positions[c];
                    Vector3D n = Vector3D.CrossProduct(p1 - p0, p2 - p0);
                    Double area = 0.5 * n.Length;
                    if (area < 1e-10) continue;
                    n.Normalize();
                    Point3D ctr = new((p0.X + p1.X + p2.X) / 3.0, (p0.Y + p1.Y + p2.Y) / 3.0, (p0.Z + p1.Z + p2.Z) / 3.0);
                    acc += (Vector3D)ctr * area;
                    areaSum += area;
                    tris_.Add(new TriData { Center = ctr, Normal = n, Radial = 0, Area = area });
                }
            }
            if (tris_.Count == 0 || areaSum <= 0) return false;
            center_ = (Point3D)(acc / areaSum);
            return true;
        }

        private static List<MeshSlot> BuildSlots(Model3D root_) {
            List<MeshSlot> slots = [];
            Int32 slotIndex = 0;
            Int32 globalStart = 0;
            void Walk(Model3D? node_, Model3DGroup? parent_, Int32 childIndex_) {
                if (node_ is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh) {
                    Int32 tc = TriangleCount(mesh);
                    if (tc <= 0) return;
                    slots.Add(new MeshSlot {
                        SlotIndex = slotIndex++,
                        Mesh = mesh,
                        OwnerModel = gm,
                        ParentGroup = parent_,
                        ParentChildIndex = childIndex_,
                        FirstGlobalTri = globalStart,
                        TriCount = tc
                    });
                    globalStart += tc;
                } else if (node_ is Model3DGroup g) {
                    for (Int32 i = 0; i < g.Children.Count; i++)
                        Walk(g.Children[i], g, i);
                }
            }
            Walk(root_, null, -1);
            return slots;
        }

        private static List<List<Int32>> BuildComponents(Int32 total_, List<Int32>[] adj_, Func<Int32, Boolean> include_) {
            List<List<Int32>> comps = [];
            Boolean[] seen = new Boolean[total_];
            for (Int32 i = 0; i < total_; i++) {
                if (seen[i] || !include_(i)) continue;
                Queue<Int32> q = new();
                List<Int32> comp = [];
                seen[i] = true;
                q.Enqueue(i);
                while (q.Count > 0) {
                    Int32 u = q.Dequeue();
                    comp.Add(u);
                    foreach (Int32 v in adj_[u]) {
                        if (seen[v] || !include_(v)) continue;
                        seen[v] = true;
                        q.Enqueue(v);
                    }
                }
                comps.Add(comp);
            }
            return comps;
        }

        private static List<Int32>[] BuildAdjacency(IReadOnlyList<MeshSlot> slots_, Int32 triCount_, Double eps_) {
            List<Int32>[] adj = new List<Int32>[triCount_];
            for (Int32 i = 0; i < triCount_; i++) adj[i] = [];

            Dictionary<(QuantPoint a, QuantPoint b), List<Int32>> edges = [];
            Int32 global = 0;
            foreach (MeshSlot slot in slots_) {
                MeshGeometry3D mesh = slot.Mesh;
                for (Int32 t = 0; t < slot.TriCount; t++, global++) {
                    if (!TryTriVerts(mesh, t, out Int32 a, out Int32 b, out Int32 c) || mesh.Positions is null) continue;
                    AddEdge(edges, QuantPoint.From(mesh.Positions[a], eps_), QuantPoint.From(mesh.Positions[b], eps_), global);
                    AddEdge(edges, QuantPoint.From(mesh.Positions[b], eps_), QuantPoint.From(mesh.Positions[c], eps_), global);
                    AddEdge(edges, QuantPoint.From(mesh.Positions[c], eps_), QuantPoint.From(mesh.Positions[a], eps_), global);
                }
            }

            foreach ((_, List<Int32> list) in edges) {
                if (list.Count < 2) continue;
                for (Int32 i = 0; i < list.Count; i++)
                    for (Int32 j = i + 1; j < list.Count; j++) {
                        Int32 u = list[i], v = list[j];
                        if (!adj[u].Contains(v)) adj[u].Add(v);
                        if (!adj[v].Contains(u)) adj[v].Add(u);
                    }
            }
            return adj;
        }

        private static void AddEdge(Dictionary<(QuantPoint a, QuantPoint b), List<Int32>> edges_, QuantPoint u_, QuantPoint v_, Int32 tri_) {
            if (u_.CompareTo(v_) > 0) (u_, v_) = (v_, u_);
            (QuantPoint a, QuantPoint b) key = (u_, v_);
            if (!edges_.TryGetValue(key, out List<Int32>? list)) {
                list = [];
                edges_[key] = list;
            }
            list.Add(tri_);
        }

        private readonly struct QuantPoint(Int64 x_, Int64 y_, Int64 z_) : IComparable<QuantPoint> {
            public Int64 X { get; } = x_;
            public Int64 Y { get; } = y_;
            public Int64 Z { get; } = z_;
            public static QuantPoint From(Point3D p_, Double eps_) => new(
                (Int64)Math.Round(p_.X / eps_),
                (Int64)Math.Round(p_.Y / eps_),
                (Int64)Math.Round(p_.Z / eps_));
            public Int32 CompareTo(QuantPoint other_) {
                Int32 cx = X.CompareTo(other_.X);
                if (cx != 0) return cx;
                Int32 cy = Y.CompareTo(other_.Y);
                if (cy != 0) return cy;
                return Z.CompareTo(other_.Z);
            }
        }

        private static Int32 TriangleCount(MeshGeometry3D mesh_) {
            Int32Collection? idx = mesh_.TriangleIndices;
            if (idx is { Count: > 0 }) return idx.Count / 3;
            return mesh_.Positions?.Count / 3 ?? 0;
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

        private static Vector3D SmallestAxis(Rect3D b_) {
            if (b_.SizeX <= b_.SizeY && b_.SizeX <= b_.SizeZ) return new Vector3D(1, 0, 0);
            if (b_.SizeY <= b_.SizeX && b_.SizeY <= b_.SizeZ) return new Vector3D(0, 1, 0);
            return new Vector3D(0, 0, 1);
        }

        private static Double Quantile(Double[] sorted_, Double q_) {
            if (sorted_.Length == 0) return 0;
            Double x = Math.Clamp(q_, 0, 1) * (sorted_.Length - 1);
            Int32 i0 = (Int32)Math.Floor(x);
            Int32 i1 = (Int32)Math.Ceiling(x);
            if (i0 == i1) return sorted_[i0];
            Double t = x - i0;
            return sorted_[i0] * (1.0 - t) + sorted_[i1] * t;
        }

        private static Double Median(IEnumerable<Double> vals_) {
            Double[] a = [.. vals_.OrderBy(v => v)];
            if (a.Length == 0) return 0;
            Int32 m = a.Length / 2;
            return (a.Length % 2) == 1 ? a[m] : ((a[m - 1] + a[m]) * 0.5);
        }

        private static Double EstimateHubRadiusByCircularCoverage(
            IReadOnlyList<TriData> tris_,
            Point3D center_,
            Vector3D axis_,
            Double[] sortedRadial_,
            Double fallback_) {
            if (tris_.Count == 0 || sortedRadial_.Length == 0) return fallback_;
            (Vector3D u, Vector3D v) = BuildPerpendicularBasis(axis_);
            Double rMax = Quantile(sortedRadial_, 0.95);
            if (rMax <= 1e-9) return fallback_;

            const Int32 radialBins = 40;
            const Int32 angularBins = 96;
            Boolean[,] occ = new Boolean[radialBins, angularBins];
            Int32[] samples = new Int32[radialBins];
            foreach (TriData t in tris_) {
                if (t.Radial <= 1e-9 || t.Radial > rMax) continue;
                Int32 rb = Math.Min(radialBins - 1, (Int32)(t.Radial / rMax * radialBins));
                Vector3D d = t.Center - center_;
                Double x = Vector3D.DotProduct(d, u);
                Double y = Vector3D.DotProduct(d, v);
                Double a = Math.Atan2(y, x);
                if (a < 0) a += 2 * Math.PI;
                Int32 ab = Math.Min(angularBins - 1, (Int32)(a / (2.0 * Math.PI) * angularBins));
                occ[rb, ab] = true;
                samples[rb]++;
            }

            // Center-out circular scan:
            // - keep rings while they remain closed/continuous around 360 degrees
            // - stop when rings become open (blade region)
            Int32 lastClosed = -1;
            Boolean started = false;
            Int32 badStreak = 0;
            for (Int32 rb = 0; rb < radialBins; rb++) {
                if (samples[rb] < 10) {
                    if (started) badStreak++;
                    if (started && badStreak >= 2) break;
                    continue;
                }
                Int32 covered = 0;
                for (Int32 ab = 0; ab < angularBins; ab++) if (occ[rb, ab]) covered++;
                Double cover = (Double)covered / angularBins;
                Double largestGap = (Double)LargestFalseRunCircular(occ, rb, angularBins) / angularBins;
                Boolean closed = cover >= 0.78 && largestGap <= 0.18;

                if (closed) {
                    started = true;
                    lastClosed = rb;
                    badStreak = 0;
                } else if (started) {
                    badStreak++;
                    // if ring is clearly open, stop immediately
                    if (largestGap >= 0.28 || badStreak >= 2)
                        break;
                }
            }
            if (lastClosed < 0) return fallback_;
            Double c = ((lastClosed + 1.0) / radialBins) * rMax;
            return Math.Clamp(c, Quantile(sortedRadial_, 0.45), Quantile(sortedRadial_, 0.82));
        }

        private static Int32 LargestFalseRunCircular(Boolean[,] occ_, Int32 radialBin_, Int32 angularBins_) {
            Int32 longest = 0;
            Int32 run = 0;
            for (Int32 i = 0; i < angularBins_ * 2; i++) {
                Int32 ab = i % angularBins_;
                if (!occ_[radialBin_, ab]) {
                    run++;
                    if (run > longest) longest = run;
                } else {
                    run = 0;
                }
            }
            return Math.Min(longest, angularBins_);
        }

        private static (Vector3D u, Vector3D v) BuildPerpendicularBasis(Vector3D axis_) {
            Vector3D helper = Math.Abs(axis_.X) < 0.8 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
            Vector3D u = Vector3D.CrossProduct(axis_, helper);
            if (u.LengthSquared < 1e-12) {
                helper = new Vector3D(0, 0, 1);
                u = Vector3D.CrossProduct(axis_, helper);
            }
            u.Normalize();
            Vector3D v = Vector3D.CrossProduct(axis_, u);
            v.Normalize();
            return (u, v);
        }
    }
}
