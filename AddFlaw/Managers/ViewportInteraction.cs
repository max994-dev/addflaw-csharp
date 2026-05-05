using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using AddFlaw.Models;
using HelixToolkit.Wpf;

namespace AddFlaw.Managers {
    /// <summary>
    /// Provides interaction helpers for a <see cref="HelixViewport3D"/> including hit testing,
    /// camera focusing/animation, panning, field-of-view zoom, and position/target adjustments.
    /// </summary>
    public class ViewportInteraction {
        private readonly HelixViewport3D _viewport;
        
        /// <summary>
        /// Initializes a new instance bound to a Helix viewport.
        /// </summary>
        /// <param name="viewport_">The target <see cref="HelixViewport3D"/>.</param>
        public ViewportInteraction(HelixViewport3D viewport_) => _viewport = viewport_;

        /// <summary>
        /// Performs a hit test at the given mouse position and returns the first 3D point that lies on a model
        /// managed by the provided <paramref name="modelManager_"/>.
        /// </summary>
        /// <param name="mousePos_">Mouse position in viewport coordinates.</param>
        /// <param name="modelManager_">Model manager used to validate hits.</param>
        /// <returns>
        /// A tuple: (hitFound, hitPosition). If no valid model is hit, returns (false, null).
        /// </returns>
        public (Boolean, Point3D?) Get3DPointInModel(Point mousePos_, ModelManager modelManager_) {
            var hits = Viewport3DHelper.FindHits(_viewport.Viewport, mousePos_);
            if (hits is null || hits.Count == 0)
                return (false, null);
            PointHitResult? hit = hits.FirstOrDefault();
            if ((hit is null) || (hit.Model is null) || (!modelManager_.ContainsModel(hit.Model)))
                return (false, null);
            return (true, hit.Position);
        }

        /// <summary>
        /// Performs a hit test at the mouse position and returns the first hit that corresponds to a flaw marker
        /// (i.e., not part of the main model set).
        /// </summary>
        /// <param name="mousePos_">Mouse position in viewport coordinates.</param>
        /// <param name="modelManager_">Model manager used to exclude main scene models.</param>
        /// <param name="flawManager_">Flaw manager used to resolve a marker from a hit model.</param>
        /// <returns>
        /// A tuple: (hitFound, hitPosition, flawMarker). If no flaw is hit, returns (false, null, null).
        /// </returns>
        public (Boolean, Point3D?, FlawMarker?) Get3DPointInFlaws(Point mousePos_, ModelManager modelManager_, FlawManager flawManager_) {
            var hits = Viewport3DHelper.FindHits(_viewport.Viewport, mousePos_);
            if (hits is null || hits.Count == 0)
                return (false, null, null);

            while (hits.Count > 0) {
                PointHitResult? hit = hits.FirstOrDefault();
                hits.RemoveAt(0);
                if (!modelManager_.ContainsModel(hit?.Model)) {
                    FlawMarker? flaw = flawManager_.GetFlawByModel(hit?.Model);
                    return (true, hit?.Position, flaw);
                }
            }
            return (false, null, null);
        }

        /// <summary>
        /// Smoothly animates the camera to focus on the given flaw marker, adjusting the distance based on FOV
        /// so the flaw flaw is clearly visible.
        /// </summary>
        /// <param name="marker_">The flaw marker to focus on.</param>
        public void FocusCameraOnFlawAnimated(FlawMarker marker_) {
            if (_viewport.Camera is not PerspectiveCamera cam)
                return;

            // 1) Flaw info
            Vector3D flawVec = marker_.EndPoint - marker_.StartPoint;
            Double flawLength = flawVec.Length;
            if (flawLength < 1e-6)
                flawLength = 1.0; // avoid degenerate

            // Midpoint of the flaw
            var target = new Point3D(
                (marker_.StartPoint.X + marker_.EndPoint.X) * 0.5,
                (marker_.StartPoint.Y + marker_.EndPoint.Y) * 0.5,
                (marker_.StartPoint.Z + marker_.EndPoint.Z) * 0.5);

            // 2) Compute a good distance based on FOV so flaw is clearly visible
            // We approximate that we want the flaw to take 50% of the viewport height.
            Double fovDeg = cam.FieldOfView;
            Double fovRad = fovDeg * Math.PI / 180.0;

            Double desiredScreenFraction = 0.5; // 0..1, how much of vertical screen the flaw should occupy
            Double effectiveAngle = fovRad * desiredScreenFraction;

            // distance so the flaw fits that angle: h = 2 * d * tan(theta/2) => d = h / (2 * tan(theta/2))
            Double d = flawLength / (2.0 * Math.Tan(effectiveAngle / 2.0));

            // Safety clamps: not too close, not too far
            Double minDist = flawLength * 0.8;   // a bit closer than length
            Double maxDist = flawLength * 10.0;  // don't go crazy far
            Double targetDistance = Math.Max(minDist, Math.Min(d, maxDist));
            if (Double.IsNaN(targetDistance) || targetDistance < 1.0)
                targetDistance = Math.Max(flawLength * 2.0, 10.0);

            // 3) Use current view direction but adjust distance
            Vector3D currentDir = cam.LookDirection;
            if (currentDir.LengthSquared < 1e-6)
                currentDir = new Vector3D(0, 0, -1);

            currentDir.Normalize();
            Vector3D newLookDir = currentDir * targetDistance;
            Point3D newPos = target - newLookDir;

            // 4) Animate Position + LookDirection for a smooth zoom
            var duration = TimeSpan.FromMilliseconds(400);

            var posAnim = new Point3DAnimation {
                From = cam.Position,
                To = newPos,
                Duration = duration,
                AccelerationRatio = 0.3,
                DecelerationRatio = 0.3
            };

            Vector3DAnimation lookAnim = new() {
                From = cam.LookDirection,
                To = newLookDir,
                Duration = duration,
                AccelerationRatio = 0.3,
                DecelerationRatio = 0.3
            };

            posAnim.Completed += (s_, evt_) => {
                cam.BeginAnimation(ProjectionCamera.PositionProperty, null);
                cam.Position = newPos;

                cam.BeginAnimation(ProjectionCamera.LookDirectionProperty, null);
                cam.LookDirection = newLookDir;
            };
            cam.BeginAnimation(ProjectionCamera.PositionProperty, posAnim);
            cam.BeginAnimation(ProjectionCamera.LookDirectionProperty, lookAnim);
        }

        // pan: move position and target together
        /// <summary>
        /// Pans the camera by moving both position and target together in screen-space directions.
        /// </summary>
        /// <param name="dx_">Horizontal pan amount (screen space, right positive).</param>
        /// <param name="dy_">Vertical pan amount (screen space, up positive).</param>
        public void PanCamera(Double dx_, Double dy_) {
            if (_viewport.Camera is not ProjectionCamera cam)
                return;
            Vector3D look = cam.LookDirection;                                                             // World directions based on camera orientation
            if (look.LengthSquared < 1e-6)
                return;
            look.Normalize();

            Vector3D up = cam.UpDirection;
            up.Normalize();

            Vector3D right = Vector3D.CrossProduct(look, up);
            if (right.LengthSquared < 1e-6)
                return;
            right.Normalize();

            Double distance = cam.LookDirection.Length;
            Double panScale = distance * 0.1; // tweak sensitivity
            Vector3D delta = ((-dx_ * right) + (dy_ * up)) * panScale;                                        // dx_, dy_ are in "screen space": right and up
            Point3D pos = cam.Position;
            Point3D tgt = pos + cam.LookDirection;

            pos += delta;
            tgt += delta;

            cam.Position = pos;
            cam.LookDirection = tgt - pos;
        }

        /// <summary>
        /// FOV zoom. Changes the perspective camera field of view by the given delta, clamped to [5, 120] degrees.
        /// </summary>
        /// <param name="delta_">Delta to add to current field of view.</param>
        public void ChangeFov(Double delta_) {
            if (_viewport.Camera is PerspectiveCamera cam) {
                Double newFov = cam.FieldOfView + delta_;
                newFov = Math.Max(5.0, Math.Min(120.0, newFov));
                cam.FieldOfView = newFov;
            }
        }

        /// <summary>
        /// Computes a movement step size based on camera distance and FOV, used to keep motion perceptually consistent.
        /// </summary>
        /// <param name="cam_">The camera for which the step is computed.</param>
        /// <returns>Step size in world units.</returns>
        private static Double GetDynamicStep(ProjectionCamera cam_) {
            Double distance = cam_.LookDirection.Length / 2.0;                                           // Distance from camera to target
            if (distance < 1e-3)
                distance = 1.0;
            const Double screenFraction = 0.05;                                                         // Base fraction of screen height to move per key press: 5% of view height per key
            if (cam_ is PerspectiveCamera pc) {
                Double fovRad = pc.FieldOfView * Math.PI / 180.0;
                Double worldHeight = 2.0 * distance * Math.Tan(fovRad / 2.0);                        // Height of the view frustum at this distance
                return worldHeight * screenFraction;
            }
            return distance * screenFraction;                                                           // Fallback for other camera types
        }

        /// <summary>
        /// Moves the camera position in world space while keeping the current target fixed.
        /// </summary>
        /// <param name="dx_">World-space X movement multiplier.</param>
        /// <param name="dy_">World-space Y movement multiplier.</param>
        /// <param name="dz_">World-space Z movement multiplier.</param>
        public void MovePosition(Int32 dx_, Int32 dy_, Int32 dz_) {
            if (_viewport.Camera is not ProjectionCamera cam)
                return;
            Double step = GetDynamicStep(cam);

            // 1. Current position and target
            Point3D oldPos = cam.Position;
            Point3D oldTarget = oldPos + cam.LookDirection;

            // 2. Move position in world space
            Vector3D offset = new(dx_ * step, dy_ * step, dz_ * step);
            Point3D newPos = oldPos + offset;

            // 3. Keep target fixed → recompute LookDirection
            cam.Position = newPos;
            cam.LookDirection = oldTarget - newPos;
        }

        /// <summary>
        /// Moves the camera target in world space while keeping the current position fixed.
        /// </summary>
        /// <param name="dx_">World-space X movement multiplier.</param>
        /// <param name="dy_">World-space Y movement multiplier.</param>
        /// <param name="dz_">World-space Z movement multiplier.</param>
        public void MoveTarget(Int32 dx_, Int32 dy_, Int32 dz_) {
            if (_viewport.Camera is not ProjectionCamera cam)
                return;
            Double step = GetDynamicStep(cam);
            Point3D pos = cam.Position;
            Point3D target = pos + cam.LookDirection;
            Vector3D offset = new(dx_ * step, dy_ * step, dz_ * step);
            target += offset;
            cam.LookDirection = target - pos;                                                           // Position unchanged, only LookDirection changes
        }
    }
}