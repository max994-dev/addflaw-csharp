using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Color = System.Windows.Media.Color;

namespace AddFlaw.Managers {
    /// <summary>
    /// Manages loading and coloring of a 3D model within a Helix viewport, including diffuse and emissive
    /// materials for both front and back faces and utilities to check model containment.
    /// </summary>
    public class ModelManager {
        private readonly HelixViewport3D _viewport;                                 // Target viewport to host the model
        private Color _color = Colors.Blue, _backColor = Colors.Green;              // Front/back base colors
        private Byte _emissionAlpha = 20;                                           // Emissive alpha component
        private Byte _baseAlpha = 255;                                              // Diffuse/base alpha component
        
        private readonly LinesVisual3D _xAxisVisual;
        private readonly LinesVisual3D _yAxisVisual;
        private readonly LinesVisual3D _zAxisVisual;
        private readonly TruncatedConeVisual3D _xAxisArrow;
        private readonly TruncatedConeVisual3D _yAxisArrow;
        private readonly TruncatedConeVisual3D _zAxisArrow;
        private readonly ModelVisual3D _axisVisual;
        private Boolean _axisVisible = true;
        private Double _axisLength = 5; // change this value to set length of axis lines

        // Target sphere visualization
        private readonly SphereVisual3D _targetSphere;
        private readonly ModelVisual3D _targetSphereVisual;
        private Double _targetSphereRadius;
        private const Double TARGET_SPHERE_MAX_RADIUS = 10.0;                       // change this value to set max radius
        private const Double TARGET_SPHERE_GROW_RATE = 3;                           // Logical growth speed per second (radius units / second)

        public ModelVisual3D ModelVisual { get; }                                   // Root visual for the loaded model

        /// <summary>
        /// Constructs a model manager bound to a viewport and a model visual container.
        /// </summary>
        /// <param name="viewport_">Helix viewport instance.</param>
        /// <param name="model_">Model visual container to populate.</param>
        public ModelManager(HelixViewport3D viewport_, ModelVisual3D model_) {
            _viewport = viewport_;
            ModelVisual = model_;
            {                                                                       // Create axis Lines

                Point3DCollection xAxisPoints = [new Point3D(-_axisLength, 0, 0), new Point3D(_axisLength, 0, 0)];                        // Create X, Y, Z axis lines at origin with different colors
                Point3DCollection yAxisPoints = [new Point3D(0, -_axisLength, 0), new Point3D(0, _axisLength, 0)];
                Point3DCollection zAxisPoints = [new Point3D(0, 0, -_axisLength), new Point3D(0, 0, _axisLength)];

                _xAxisVisual = new LinesVisual3D {
                Points = xAxisPoints,
                Color = Colors.Cyan,
                Thickness = 3.0
            };

                _yAxisVisual = new LinesVisual3D {
                Points = yAxisPoints,
                Color = Colors.Magenta,
                Thickness = 3.0
            };

                _zAxisVisual = new LinesVisual3D {
                Points = zAxisPoints,
                    Color = Colors.DarkGoldenrod,
                Thickness = 3.0
            };

                Double arrowLength = _axisLength * 0.08;
                Double arrowRadius = _axisLength * 0.02;

                _xAxisArrow = new TruncatedConeVisual3D {
                BaseRadius = arrowRadius,
                TopRadius = 0.001, 
                Height = arrowLength,
                    Origin = new Point3D(_axisLength - (arrowLength / 2), 0, 0),
                Normal = new Vector3D(1, 0, 0),
                Material = MaterialHelper.CreateMaterial(Colors.Cyan)
            };

                _yAxisArrow = new TruncatedConeVisual3D {
                BaseRadius = arrowRadius,
                TopRadius = 0.001, 
                Height = arrowLength,
                    Origin = new Point3D(0, _axisLength - (arrowLength / 2), 0),
                Normal = new Vector3D(0, 1, 0),
                Material = MaterialHelper.CreateMaterial(Colors.Magenta)
            };

                _zAxisArrow = new TruncatedConeVisual3D {
                BaseRadius = arrowRadius,
                TopRadius = 0.001, 
                Height = arrowLength,
                    Origin = new Point3D(0, 0, _axisLength - (arrowLength / 2)),
                Normal = new Vector3D(0, 0, 1),
                    Material = MaterialHelper.CreateMaterial(Colors.DarkGoldenrod)
            };

                _axisVisual = new ModelVisual3D();
                _axisVisual.Children.Add(_xAxisVisual);
                _axisVisual.Children.Add(_yAxisVisual);
                _axisVisual.Children.Add(_zAxisVisual);
                _axisVisual.Children.Add(_xAxisArrow);
                _axisVisual.Children.Add(_yAxisArrow);
                _axisVisual.Children.Add(_zAxisArrow);
                _viewport.Children.Add(_axisVisual);
            }
            {                                                                       // Create target sphere visual
                _targetSphere = new SphereVisual3D {
                    Radius = _targetSphereRadius,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(180, 128, 0, 64))), // set color and alpha here
                Visible = false
            };
                _targetSphereVisual = new ModelVisual3D();
                _targetSphereVisual.Children.Add(_targetSphere);
                _viewport.Children.Add(_targetSphereVisual);
            }
        }

        /// <summary>
        /// Loads a 3D model from the specified file path, applies current color settings, and adds it to the viewport.
        /// </summary>
        /// <param name="filePath_">Path to the 3D model file.</param>
        public void LoadModel(String filePath_) {
            ModelImporter importer = new();
            Model3D? model = importer.Load(filePath_);
            if (model is not null) {
                ApplyColor(model);
                Rect3D bounds = model.Bounds;
                Double maxDimension = Math.Max(Math.Max(bounds.SizeX, bounds.SizeY), bounds.SizeZ);
                if (maxDimension > 0) {
                    // Set axis length to of the largest model dimension
                    SetAxisLength(maxDimension);
                }
            }
            if (ModelVisual is not null) {
                _ = _viewport.Children.Remove(ModelVisual);
                ModelVisual.Content = model;
                _viewport.Children.Add(ModelVisual);
                _viewport.ZoomExtents();
            }
        }

        /// <summary>
        /// Sets the base (diffuse) alpha used for material coloring and reapplies colors.
        /// </summary>
        /// <param name="alpha_">Diffuse alpha value.</param>
        public void SetBaseAlpha(Byte alpha_) {
            _baseAlpha = alpha_;
            ApplyColor(ModelVisual.Content);
        }

        /// <summary>
        /// Sets the emissive alpha used for material coloring and reapplies colors.
        /// </summary>
        /// <param name="emissionAlpha_">Emissive alpha value.</param>
        public void SetEmissionAlpha(Byte emissionAlpha_) {
            this._emissionAlpha = emissionAlpha_;
            ApplyColor(ModelVisual.Content);
        }

        /// <summary>
        /// Sets the front-face model color and reapplies materials.
        /// </summary>
        /// <param name="color_">New front-face color.</param>
        public void SetModelColor(Color color_) {
            this._color = color_;
            ApplyColor(ModelVisual.Content);
        }

        /// <summary>
        /// Applies current color settings recursively to the model and its children.
        /// </summary>
        /// <param name="model_">Model to color.</param>
        private void ApplyColor(Model3D model_) {
            if (model_ is Model3DGroup groupModel) {
                foreach (Model3D child in groupModel.Children) {
                    ApplyColor(child);
                }
            } else if (model_ is GeometryModel3D geometry) {
                MaterialGroup mg = new();
                mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(_baseAlpha, _color.R, _color.G, _color.B))));
                mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(_emissionAlpha, _color.R, _color.G, _color.B))));
                geometry.Material = mg;
                mg = new MaterialGroup();
                mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(_baseAlpha, _backColor.R, _backColor.G, _backColor.B))));
                mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(_emissionAlpha, _backColor.R, _backColor.G, _backColor.B))));
                geometry.BackMaterial = mg;
            }
        }

        /// <summary>
        /// Checks whether the given model instance is part of the loaded model tree.
        /// </summary>
        /// <param name="model_">Model instance to search for.</param>
        /// <returns>True if the model exists in the current content; otherwise false.</returns>
        public Boolean ContainsModel(Model3D? model_) {
            if (ModelVisual.Content is Model3DGroup groupModel) {
                foreach (Model3D child in groupModel.Children) {
                    if (child.Equals(model_))
                        return true;
                }
                return false;
            } else if (ModelVisual.Content is GeometryModel3D geometry) {
                return geometry.Equals(model_);
            }
            return false;
        }

        public void ShowAxisLines(Boolean show_) {
            _axisVisible = show_;
            if (_axisVisual != null) {
                if (show_) {
                    if (!_viewport.Children.Contains(_axisVisual)) {
                        _viewport.Children.Add(_axisVisual);
                    }
                } else {
                    _ = _viewport.Children.Remove(_axisVisual);
                }
            }
        }

        public void SetAxisLength(Double length_) {
            _axisLength = length_;

            if (_xAxisVisual != null) {
                Point3DCollection xAxisPoints = [new Point3D(-_axisLength, 0, 0), new Point3D(_axisLength, 0, 0)];
                _xAxisVisual.Points = xAxisPoints;
            }
            if (_yAxisVisual != null) {
                Point3DCollection yAxisPoints = [new Point3D(0, -_axisLength, 0), new Point3D(0, _axisLength, 0)];
                _yAxisVisual.Points = yAxisPoints;
            }
            if (_zAxisVisual != null) {
                Point3DCollection zAxisPoints = [new Point3D(0, 0, -_axisLength), new Point3D(0, 0, _axisLength)];
                _zAxisVisual.Points = zAxisPoints;
            }

            Double arrowLength = _axisLength * 0.08;
            Double arrowRadius = _axisLength * 0.02;

            if (_xAxisArrow != null) {
                _xAxisArrow.BaseRadius = arrowRadius;
                _xAxisArrow.TopRadius = 0.001;
                _xAxisArrow.Height = arrowLength;
                _xAxisArrow.Origin = new Point3D(_axisLength - (arrowLength / 2), 0, 0);
            }
            if (_yAxisArrow != null) {
                _yAxisArrow.BaseRadius = arrowRadius;
                _yAxisArrow.TopRadius = 0.001;
                _yAxisArrow.Height = arrowLength;
                _yAxisArrow.Origin = new Point3D(0, _axisLength - (arrowLength / 2), 0);
            }
            if (_zAxisArrow != null) {
                _zAxisArrow.BaseRadius = arrowRadius;
                _zAxisArrow.TopRadius = 0.001;
                _zAxisArrow.Height = arrowLength;
                _zAxisArrow.Origin = new Point3D(0, 0, _axisLength - (arrowLength / 2));
            }
        }

        public Double GetTargetSphereZoomScale() {
            if (_viewport.Camera is ProjectionCamera cam) {
                Double distance = cam.LookDirection.Length;
                if (distance < 1e-3)
                    distance = 1.0;

                const Double screenFraction = 0.01; // base reference size

                if (cam is PerspectiveCamera pc) {
                    Double fovRad = pc.FieldOfView * Math.PI / 180.0;
                    Double worldHeight = 2.0 * distance * Math.Tan(fovRad / 2.0);
                    return worldHeight * screenFraction;
                }

                // Orthographic or other cameras – scale linearly with distance
                return distance * screenFraction;
            }

            // Fallback
            return 0.1;
        }

        public void UpdateTargetSpherePosition() {
            if (_viewport.Camera is ProjectionCamera cam && _targetSphere != null) {
                Point3D target = cam.Position + cam.LookDirection;
                _targetSphere.Center = target;
            }
        }

        public void StartTargetSphereGrowing() {
            if (_targetSphere == null) return;

            _targetSphere.Visible = true;
            UpdateTargetSpherePosition();
        }

        public void ShowTargetSphere() {
            if (_targetSphere == null) return;
            _targetSphere.Visible = true;
            UpdateTargetSpherePosition();
        }

        public void HideTargetSphere() {
            if (_targetSphere == null) return;
            _targetSphere.Visible = false;
            _targetSphereRadius = 0;
            _targetSphere.Radius = _targetSphereRadius;
        }

        // Called once per rendered frame from MainWindow to grow sphere smoothly
        public void UpdateTargetSphereGrowth(Double deltaSeconds_) {
            if (_targetSphere == null || !_targetSphere.Visible)
                return;

            _targetSphereRadius += TARGET_SPHERE_GROW_RATE * deltaSeconds_;
            if (_targetSphereRadius > TARGET_SPHERE_MAX_RADIUS)
                _targetSphereRadius = TARGET_SPHERE_MAX_RADIUS;

            Double zoomScale = GetTargetSphereZoomScale();
            _targetSphere.Radius = _targetSphereRadius * zoomScale;

            UpdateTargetSpherePosition();
        }
    }
}