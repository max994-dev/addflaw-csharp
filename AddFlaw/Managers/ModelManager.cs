using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using HelixToolkit.Wpf;

namespace AddFlaw.Managers
{
    public class ModelManager
    {
        private HelixViewport3D viewport;
        
        private ModelVisual3D modelVisual;
        
        private Color color = Colors.Blue;
        
        private byte emissionAlpha = 20;
        
        private byte baseAlpha = 255;

        private LinesVisual3D xAxisVisual;
        private LinesVisual3D yAxisVisual;
        private LinesVisual3D zAxisVisual;
        private TruncatedConeVisual3D xAxisArrow;
        private TruncatedConeVisual3D yAxisArrow;
        private TruncatedConeVisual3D zAxisArrow;
        private ModelVisual3D axisVisual;
        private bool axisVisible = true;
        private double axisLength = 5; // change this value to set length of axis lines

        // Target sphere visualization
        private SphereVisual3D targetSphere;
        private ModelVisual3D targetSphereVisual;
        private double targetSphereRadius = 0;


        private const double TargetSphereMaxRadius = 10.0; // change this value to set max radius
        
        private const double TargetSphereGrowRate = 3; // Logical growth speed per second (radius units / second)

        public ModelVisual3D ModelVisual => modelVisual;

        public ModelManager(HelixViewport3D viewport, ModelVisual3D model)
        {
            this.viewport = viewport;
            this.modelVisual = model;
            CreateAxisLines();
            InitializeTargetSphere();
        }

        public void LoadModel(string filePath)
        {
            if (modelVisual != null)
            {
                viewport.Children.Remove(modelVisual);
            }

            ModelImporter importer = new ModelImporter();
            Model3D? model = importer.Load(filePath);
            ApplyColor(model);

            modelVisual.Content = model;
            viewport.Children.Add(modelVisual);
            
            // Calculate appropriate axis length based on model bounds
            if (model != null)
            {
                Rect3D bounds = model.Bounds;
                double maxDimension = Math.Max(Math.Max(bounds.SizeX, bounds.SizeY), bounds.SizeZ);
                if (maxDimension > 0)
                {
                    // Set axis length to of the largest model dimension
                    SetAxisLength(maxDimension);
                }
            }
            
            ShowAxisLines(axisVisible);
            
            viewport.ZoomExtents();
        }

        public void SetBaseAlpha(byte alpha)
        {
            baseAlpha = alpha;
            ApplyColor(modelVisual.Content);
        }

        public void SetEmissionAlpha(byte emissionAlpha)
        {
            this.emissionAlpha = emissionAlpha;
            ApplyColor(modelVisual.Content);
        }

        public void SetModelColor(Color color)
        {
            this.color = color;
            ApplyColor(modelVisual.Content);
        }


        private void ApplyColor(Model3D model)
        {
            if (model is Model3DGroup groupModel)
            {
                foreach (Model3D child in groupModel.Children)
                {
                    ApplyColor(child);
                }
            }
            else if (model is GeometryModel3D geometry)
            {
                MaterialGroup mg = new MaterialGroup();
                mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(baseAlpha, color.R, color.G, color.B))));
                mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(emissionAlpha, color.R, color.G, color.B))));

                geometry.Material = mg;
                geometry.BackMaterial = mg;
            }
        }

        
        public bool ContainsModel(Model3D model)
        {
            if (modelVisual.Content is Model3DGroup groupModel)
            {
                foreach (Model3D child in groupModel.Children)
                {
                    if (child.Equals(model)) return true;
                }
                return false;
            }
            else if (modelVisual.Content is GeometryModel3D geometry)
            {
                return geometry.Equals(model);
            }
            return false;
        }

        private void CreateAxisLines()
        {
            // Create X, Y, Z axis lines at origin with different colors
            Point3DCollection xAxisPoints = new Point3DCollection();
            xAxisPoints.Add(new Point3D(-axisLength, 0, 0));
            xAxisPoints.Add(new Point3D(axisLength, 0, 0));

            Point3DCollection yAxisPoints = new Point3DCollection();
            yAxisPoints.Add(new Point3D(0, -axisLength, 0));
            yAxisPoints.Add(new Point3D(0, axisLength, 0));

            Point3DCollection zAxisPoints = new Point3DCollection();
            zAxisPoints.Add(new Point3D(0, 0, -axisLength));
            zAxisPoints.Add(new Point3D(0, 0, axisLength));

            xAxisVisual = new LinesVisual3D
            {
                Points = xAxisPoints,
                Color = Colors.Cyan,
                Thickness = 3.0
            };

            yAxisVisual = new LinesVisual3D
            {
                Points = yAxisPoints,
                Color = Colors.Magenta,
                Thickness = 3.0
            };

            zAxisVisual = new LinesVisual3D
            {
                Points = zAxisPoints,
                Color = Colors.Yellow,
                Thickness = 3.0
            };

            double arrowLength = axisLength * 0.08;
            double arrowRadius = axisLength * 0.02;

            xAxisArrow = new TruncatedConeVisual3D
            {
                BaseRadius = arrowRadius,
                TopRadius = 0.001, 
                Height = arrowLength,
                Origin = new Point3D(axisLength - arrowLength / 2, 0, 0),
                Normal = new Vector3D(1, 0, 0),
                Material = MaterialHelper.CreateMaterial(Colors.Cyan)
            };

            yAxisArrow = new TruncatedConeVisual3D
            {
                BaseRadius = arrowRadius,
                TopRadius = 0.001, 
                Height = arrowLength,
                Origin = new Point3D(0, axisLength - arrowLength / 2 , 0),
                Normal = new Vector3D(0, 1, 0),
                Material = MaterialHelper.CreateMaterial(Colors.Magenta)
            };

            zAxisArrow = new TruncatedConeVisual3D
            {
                BaseRadius = arrowRadius,
                TopRadius = 0.001, 
                Height = arrowLength,
                Origin = new Point3D(0, 0, axisLength - arrowLength / 2),
                Normal = new Vector3D(0, 0, 1),
                Material = MaterialHelper.CreateMaterial(Colors.Yellow)
            };

            axisVisual = new ModelVisual3D();
            axisVisual.Children.Add(xAxisVisual);
            axisVisual.Children.Add(yAxisVisual);
            axisVisual.Children.Add(zAxisVisual);
            axisVisual.Children.Add(xAxisArrow);
            axisVisual.Children.Add(yAxisArrow);
            axisVisual.Children.Add(zAxisArrow);
            
            viewport.Children.Add(axisVisual);
        }

        public void ShowAxisLines(bool show)
        {
            axisVisible = show;
            if (axisVisual != null)
            {
                if (show)
                {
                    if (!viewport.Children.Contains(axisVisual))
                    {
                        viewport.Children.Add(axisVisual);
                    }
                }
                else
                {
                    viewport.Children.Remove(axisVisual);
                }
            }
        }

        public void SetAxisLength(double length)
        {
            axisLength = length;
            
            if (xAxisVisual != null)
            {
                var xAxisPoints = new Point3DCollection();
                xAxisPoints.Add(new Point3D(-axisLength, 0, 0));
                xAxisPoints.Add(new Point3D(axisLength, 0, 0));
                xAxisVisual.Points = xAxisPoints;
            }
            if (yAxisVisual != null)
            {
                var yAxisPoints = new Point3DCollection();
                yAxisPoints.Add(new Point3D(0, -axisLength, 0));
                yAxisPoints.Add(new Point3D(0, axisLength, 0));
                yAxisVisual.Points = yAxisPoints;
            }
            if (zAxisVisual != null)
            {
                var zAxisPoints = new Point3DCollection();
                zAxisPoints.Add(new Point3D(0, 0, -axisLength));
                zAxisPoints.Add(new Point3D(0, 0, axisLength));
                zAxisVisual.Points = zAxisPoints;
            }
            
            double arrowLength = axisLength * 0.08;
            double arrowRadius = axisLength * 0.02;
            
            if (xAxisArrow != null)
            {
                xAxisArrow.BaseRadius = arrowRadius;
                xAxisArrow.TopRadius = 0.001; 
                xAxisArrow.Height = arrowLength;
                xAxisArrow.Origin = new Point3D(axisLength - arrowLength / 2, 0, 0);
            }
            if (yAxisArrow != null)
            {
                yAxisArrow.BaseRadius = arrowRadius;
                yAxisArrow.TopRadius = 0.001; 
                yAxisArrow.Height = arrowLength;
                yAxisArrow.Origin = new Point3D(0, axisLength - arrowLength / 2, 0);
            }
            if (zAxisArrow != null)
            {
                zAxisArrow.BaseRadius = arrowRadius;
                zAxisArrow.TopRadius = 0.001; 
                zAxisArrow.Height = arrowLength;
                zAxisArrow.Origin = new Point3D(0, 0, axisLength - arrowLength / 2);
            }
        }

        private void InitializeTargetSphere()
        {
            // Create target sphere visual
            targetSphere = new SphereVisual3D
            {
                Radius = targetSphereRadius,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(180, 128, 0, 64))), // set color and alpha here
                Visible = false
            };

            targetSphereVisual = new ModelVisual3D();
            targetSphereVisual.Children.Add(targetSphere);
            viewport.Children.Add(targetSphereVisual);
        }

        public double GetTargetSphereZoomScale()
        {
            if (viewport.Camera is ProjectionCamera cam)
            {
                double distance = cam.LookDirection.Length;
                if (distance < 1e-3)
                    distance = 1.0;

                const double screenFraction = 0.01; // base reference size

                if (cam is PerspectiveCamera pc)
                {
                    double fovRad = pc.FieldOfView * Math.PI / 180.0;
                    double worldHeight = 2.0 * distance * Math.Tan(fovRad / 2.0);
                    return worldHeight * screenFraction;
                }

                // Orthographic or other cameras – scale linearly with distance
                return distance * screenFraction;
            }

            // Fallback
            return 0.1;
        }

        public void UpdateTargetSpherePosition()
        {
            if (viewport.Camera is ProjectionCamera cam && targetSphere != null)
            {
                Point3D target = cam.Position + cam.LookDirection;
                targetSphere.Center = target;
            }
        }

        public void StartTargetSphereGrowing()
        {
            if (targetSphere == null) return;

            targetSphere.Visible = true;
            UpdateTargetSpherePosition();
        }

        public void ShowTargetSphere()
        {
            if (targetSphere == null) return;
            targetSphere.Visible = true;
            UpdateTargetSpherePosition();
        }

        public void HideTargetSphere()
        {
            if (targetSphere == null) return;
            targetSphere.Visible = false;
            targetSphereRadius = 0;
            targetSphere.Radius = targetSphereRadius;
        }

        // Called once per rendered frame from MainWindow to grow sphere smoothly
        public void UpdateTargetSphereGrowth(double deltaSeconds)
        {
            if (targetSphere == null || !targetSphere.Visible)
                return;

            targetSphereRadius += TargetSphereGrowRate * deltaSeconds;
            if (targetSphereRadius > TargetSphereMaxRadius)
                targetSphereRadius = TargetSphereMaxRadius;

            double zoomScale = GetTargetSphereZoomScale();
            targetSphere.Radius = targetSphereRadius * zoomScale;

            UpdateTargetSpherePosition();
        }
    }
}