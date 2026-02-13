using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using AddFlaw.Models;
using HelixToolkit.Wpf;

namespace AddFlaw.Managers
{
    public class ViewportInteraction
    {
        private HelixViewport3D viewport;
        
        public ViewportInteraction(HelixViewport3D viewport)
        {
            this.viewport = viewport;
        }

        public (bool, Point3D?) Get3DPointInModel(Point mousePos, ModelManager modelManager)
        {
            Ray3D? ray = Viewport3DHelper.GetRay(viewport.Viewport, mousePos);
            var hits = Viewport3DHelper.FindHits(viewport.Viewport, mousePos);

            if (hits.Count < 0) return (false, null);
            
            PointHitResult? hit = hits.FirstOrDefault();
            
            if (!modelManager.ContainsModel(hit.Model)) return (false, null);
            return (true, hit.Position);
            
        }

        public (bool, Point3D?, LineMarker?) Get3DPointInLines(Point mousePos, ModelManager modelManager, LineManager lineManager)
        {
            Ray3D? ray = Viewport3DHelper.GetRay(viewport.Viewport, mousePos);
            var hits = Viewport3DHelper.FindHits(viewport.Viewport, mousePos);

            if (hits.Count < 0) return (false, null, null);

            while (hits.Count > 0)
            {
                PointHitResult? hit = hits.FirstOrDefault();
                hits.RemoveAt(0);
                if (!modelManager.ContainsModel(hit.Model))
                {
                    LineMarker? line = lineManager.GetLineByModel(hit.Model);
                    return (true, hit.Position, line);
                }
            }

            return (false, null, null);

        }
        public void FocusCameraOnLineAnimated(LineMarker marker)
        {
            if (viewport.Camera is not PerspectiveCamera cam)
                return;

            // 1) Line info
            Vector3D lineVec = marker.EndPoint - marker.StartPoint;
            double lineLength = lineVec.Length;
            if (lineLength < 1e-6)
                lineLength = 1.0; // avoid degenerate

            // Midpoint of the line
            var target = new Point3D(
                (marker.StartPoint.X + marker.EndPoint.X) * 0.5,
                (marker.StartPoint.Y + marker.EndPoint.Y) * 0.5,
                (marker.StartPoint.Z + marker.EndPoint.Z) * 0.5);

            // 2) Compute a good distance based on FOV so line is clearly visible
            // We approximate that we want the line to take 50% of the viewport height.
            double fovDeg = cam.FieldOfView;
            double fovRad = fovDeg * Math.PI / 180.0;

            double desiredScreenFraction = 0.5; // 0..1, how much of vertical screen the line should occupy
            double effectiveAngle = fovRad * desiredScreenFraction;

            // distance so the line fits that angle: h = 2 * d * tan(theta/2) => d = h / (2 * tan(theta/2))
            double d = lineLength / (2.0 * Math.Tan(effectiveAngle / 2.0));

            // Safety clamps: not too close, not too far
            double minDist = lineLength * 0.8;   // a bit closer than length
            double maxDist = lineLength * 10.0;  // don't go crazy far
            double targetDistance = Math.Max(minDist, Math.Min(d, maxDist));
            if (double.IsNaN(targetDistance) || targetDistance < 1.0)
                targetDistance = Math.Max(lineLength * 2.0, 10.0);

            // 3) Use current view direction but adjust distance
            Vector3D currentDir = cam.LookDirection;
            if (currentDir.LengthSquared < 1e-6)
                currentDir = new Vector3D(0, 0, -1);

            currentDir.Normalize();
            Vector3D newLookDir = currentDir * targetDistance;
            Point3D newPos = target - newLookDir;

            // 4) Animate Position + LookDirection for a smooth zoom
            var duration = TimeSpan.FromMilliseconds(400);

            var posAnim = new Point3DAnimation
            {
                From = cam.Position,
                To = newPos,
                Duration = duration,
                AccelerationRatio = 0.3,
                DecelerationRatio = 0.3
            };

            var lookAnim = new Vector3DAnimation
            {
                From = cam.LookDirection,
                To = newLookDir,
                Duration = duration,
                AccelerationRatio = 0.3,
                DecelerationRatio = 0.3
            };

            posAnim.Completed += (s, e) =>
            {
                cam.BeginAnimation(ProjectionCamera.PositionProperty, null);
                cam.Position = newPos;

                cam.BeginAnimation(ProjectionCamera.LookDirectionProperty, null);
                cam.LookDirection = newLookDir;
            };


            cam.BeginAnimation(ProjectionCamera.PositionProperty, posAnim);
            cam.BeginAnimation(ProjectionCamera.LookDirectionProperty, lookAnim);
        }

        // pan: move position and target together
        public void PanCamera(double dx, double dy)
        {
            if (viewport.Camera is not ProjectionCamera cam)
                return;

            // World directions based on camera orientation
            Vector3D look = cam.LookDirection;
            if (look.LengthSquared < 1e-6)
                return;
            look.Normalize();

            Vector3D up = cam.UpDirection;
            up.Normalize();

            Vector3D right = Vector3D.CrossProduct(look, up);
            if (right.LengthSquared < 1e-6)
                return;
            right.Normalize();

            double distance = cam.LookDirection.Length;
            double panScale = distance * 0.1; // tweak sensitivity

            // dx, dy are in "screen space": right and up
            Vector3D delta = (-dx * right + dy * up) * panScale;

            Point3D pos = cam.Position;
            Point3D tgt = pos + cam.LookDirection;

            pos += delta;
            tgt += delta;

            cam.Position = pos;
            cam.LookDirection = tgt - pos;
        }

        // FOV zoom
        public void ChangeFov(double delta)
        {
            if (viewport.Camera is PerspectiveCamera cam)
            {
                double newFov = cam.FieldOfView + delta;
                newFov = Math.Max(5.0, Math.Min(120.0, newFov));
                cam.FieldOfView = newFov;
            }
        }

        private double GetDynamicStep(ProjectionCamera cam)
        {
            // Distance from camera to target
            double distance = cam.LookDirection.Length / 2.0;
            if (distance < 1e-3)
                distance = 1.0;

            // Base fraction of screen height to move per key press
            const double screenFraction = 0.05; // 5% of view height per key

            if (cam is PerspectiveCamera pc)
            {
                double fovRad = pc.FieldOfView * Math.PI / 180.0;
                // Height of the view frustum at this distance
                double worldHeight = 2.0 * distance * Math.Tan(fovRad / 2.0);
                return worldHeight * screenFraction;
            }

            // Fallback for other camera types
            return distance * screenFraction;
        }


        public void MovePosition(int dx, int dy, int dz)
        {
            if (viewport.Camera is not ProjectionCamera cam)
                return;

            double step = GetDynamicStep(cam);

            // 1. Current position and target
            Point3D oldPos = cam.Position;
            Point3D oldTarget = oldPos + cam.LookDirection;

            // 2. Move position in world space
            Vector3D offset = new Vector3D(
                dx * step,
                dy * step,
                dz * step);

            Point3D newPos = oldPos + offset;

            // 3. Keep target fixed → recompute LookDirection
            cam.Position = newPos;
            cam.LookDirection = oldTarget - newPos;

        }

        
        public void MoveTarget(int dx, int dy, int dz)
        {
            if (viewport.Camera is not ProjectionCamera cam)
                return;

            double step = GetDynamicStep(cam);

            Point3D pos = cam.Position;
            Point3D target = pos + cam.LookDirection;

            Vector3D offset = new Vector3D(
                dx * step,
                dy * step,
                dz * step);

            target += offset;

            // Position unchanged, only LookDirection changes
            cam.LookDirection = target - pos;

        }



    }

}