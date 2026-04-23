using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using Color = System.Windows.Media.Color;

namespace AddFlaw.Models {
    /// <summary>
    /// Represents a visual flaw marker in 3D space, including start/end points, color, label,
    /// and associated WPF/HelixToolkit visuals used to render the flaw and handles.
    /// </summary>
    public class FlawMarker {
        private Boolean _isEnd;
        private Boolean _isSelected;

        public Int32 Id { get; }                                                                        // Unique identifier for the marker
        public String Label { get; private set; }                                                       // Optional label displayed alongside the marker

        public Point3D StartPoint { get; set; }                                                         // World-space start point of the marker flaw
        public Point3D EndPoint { get; set; }                                                           // World-space end point of the marker flaw
        public Color Color { get; set; }                                                                // Color of the marker

        public Point3D MidPoint => new(                                                           // Midpoint between StartPoint and EndPoint
        (StartPoint.X + EndPoint.X) * 0.5,
        (StartPoint.Y + EndPoint.Y) * 0.5,
        (StartPoint.Z + EndPoint.Z) * 0.5);

        public Double BaseSphereRadius { get; set; } = 0.01;                                            // Base / Starting radius for the optional highlight sphere placed at MidPoint

        public SolidColorBrush ColorBrush => new(Color);                              // Convenience brush created from Color

        // Visual pieces
        public LinesVisual3D FlawVisual { get; private set; }                                           // Flaw visual representing the flaw
        public CubeVisual3D StartVisual { get; }                                                        // Cube visual at the start point
        public CubeVisual3D? EndVisual { get; private set; }                                            // Cube visual at the end point (created on completion)
        public SphereVisual3D? SphereVisual { get; set; }                                               // Optional sphere visual placed at the midpoint

        public BillboardTextVisual3D TextVisual { get; }                                                // Billboard text visual used to render the label

        public ModelVisual3D ModelVisual { get; }                                                       // Root visual that groups all parts of this marker

        public Double ScreenFraction { get; set; } = 0.015;                                             // Desired fraction of the screen used for sizing visuals (e.g., text scaling)

        /// <summary>
        /// Creates a new flaw marker with start and end points, color, and an optional label.
        /// Initializes visuals for flaw, start cube, and text.
        /// </summary>
        /// <param name="id_">Unique id.</param>
        /// <param name="start_">Start point.</param>
        /// <param name="end_">End point.</param>
        /// <param name="color_">Marker color.</param>
        /// <param name="label_">Optional label text.</param>
        public FlawMarker(Int32 id_, Point3D start_, Point3D end_, Color color_, String? label_ = "") {
            Id = id_;
            Label = label_ ?? String.Empty;
            StartPoint = start_;
            EndPoint = end_;
            Color = color_;
            var mat = MaterialHelper.CreateMaterial(Color);
            FlawVisual = new LinesVisual3D { Color = Color, Thickness = 2.0, Points = [StartPoint, EndPoint] };
            StartVisual = new CubeVisual3D { Center = StartPoint, SideLength = .02, Material = mat, };
            TextVisual = new BillboardTextVisual3D {
                Text = BuildDisplayText(),
                Foreground = Brushes.Yellow,
                Background = Brushes.Transparent,
                FontSize = 20,
                Padding = new Thickness(2, 1, 2, 1),
                Position = GetLabelPosition(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ModelVisual = new ModelVisual3D();
            ModelVisual.Children.Add(FlawVisual);
            ModelVisual.Children.Add(StartVisual);
        }

        /// <summary>
        /// Builds the display text for the marker combining id and label.
        /// </summary>
        /// <returns>Formatted display text.</returns>
        private String BuildDisplayText() => String.IsNullOrWhiteSpace(Label) ? Id.ToString() : $"Marker {Id}: {Label}";

        /// <summary>
        /// Calculates the label position near the end point with a slight vertical offset.
        /// </summary>
        /// <returns>World-space label position.</returns>
        private Point3D GetLabelPosition() {
            Point3D mid = new((StartPoint.X * 0) + (EndPoint.X * 1), (StartPoint.Y * 0) + (EndPoint.Y * 1), (StartPoint.Z * 0) + (EndPoint.Z * 1));
            return new Point3D(mid.X, mid.Y + .7, mid.Z);                                       // Tiny offset so text doesn't intersect the surface
        }

        /// <summary>
        /// Updates the label text and refreshes the text visual.
        /// </summary>
        /// <param name="label_">New label text.</param>
        public void SetLabel(String label_) {
            Label = label_ ?? String.Empty;
            TextVisual.Text = BuildDisplayText();
        }

        /// <summary>
        /// Marks the marker as selected and updates the end point without completing the marker.
        /// </summary>
        /// <param name="newEnd_">New end point.</param>
        public void Start(Point3D newEnd_) {
            _isSelected = true;
            UpdateEndPoint(newEnd_, false);
        }

        /// <summary>
        /// Marks the marker as not selected and completes the marker at the given end point.
        /// </summary>
        /// <param name="newEnd_">Final end point.</param>
        public void End(Point3D newEnd_) {
            _isSelected = false;
            UpdateEndPoint(newEnd_, true);
        }

        /// <summary>
        /// Updates the end point, optionally finalizing visuals (end cube and sphere),
        /// and refreshes text and visuals.
        /// </summary>
        /// <param name="newEnd_">New end point.</param>
        /// <param name="isEnd_">True to finalize and create end/sphere visuals.</param>
        public void UpdateEndPoint(Point3D newEnd_, Boolean isEnd_) {
            EndPoint = newEnd_;
            this._isEnd = isEnd_;
            if (isEnd_) {
                var mat = MaterialHelper.CreateMaterial(Color);
                EndVisual = new CubeVisual3D { Center = EndPoint, SideLength = .02, Material = mat };
                SphereVisual = new SphereVisual3D { Center = MidPoint, Radius = BaseSphereRadius, Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(100, Color.R, Color.G, Color.B))), Visible = false };
                ModelVisual.Children.Add(EndVisual);
                ModelVisual.Children.Add(SphereVisual);
            }
            TextVisual.Position = GetLabelPosition();
            UpdateVisual();
        }

        /// <summary>
        /// Updates visuals based on current state, including color, thickness, and visibility.
        /// </summary>
        private void UpdateVisual() {
            if (FlawVisual != null) {
                Color showColor = _isSelected ? Colors.Yellow : Color;
                FlawVisual.Points = [StartPoint, EndPoint];
                FlawVisual.Color = _isSelected ? Colors.Yellow : Color;
                FlawVisual.Thickness = _isSelected ? 6 : 4;
                StartVisual.Center = StartPoint;
                StartVisual.Material = MaterialHelper.CreateMaterial(showColor);
                StartVisual.Visible = _isSelected;
                if (_isEnd && EndVisual is not null) {
                    EndVisual.Center = EndPoint;
                    EndVisual.Material = MaterialHelper.CreateMaterial(showColor);
                    EndVisual.Visible = _isSelected;
                }
            }
        }

        /// <summary>
        /// Updates the marker sizes based on camera distance - only called during line creation
        /// </summary>
        public void UpdateMarkerSizes(HelixViewport3D viewport_) {
            if (viewport_?.Camera is not PerspectiveCamera cam)
                return;
            Vector3D lineVec = EndPoint - StartPoint;                                                   // Calculate line length to base marker size on line size, not viewport
            double lineLength = lineVec.Length;
            if (lineLength < 1e-6) lineLength = 1e-6;                                                   // Avoid division by zero
            Vector3D toStart = StartPoint - cam.Position;                                               // Calculate distance from camera to start and end points
            Vector3D toEnd = EndPoint - cam.Position;
            Double distStart = toStart.Length;
            Double distEnd = toEnd.Length;
            if (distStart < 1e-6) distStart = 1.0;
            if (distEnd < 1e-6) distEnd = 1.0;
            Double fovRad = cam.FieldOfView * Math.PI / 180.0;                                          // Calculate FOV-based world height at each distance for screen visibility
            const Double screenFraction = 0.01;                                                         // 1% of viewport height for minimum visibility
            Double worldHeightStart = 2.0 * distStart * Math.Tan(fovRad / 2.0);
            Double worldHeightEnd = 2.0 * distEnd * Math.Tan(fovRad / 2.0);
            double screenBasedSizeStart = worldHeightStart * screenFraction;
            double screenBasedSizeEnd = worldHeightEnd * screenFraction;
            const Double lineFraction = 0.05;                                                           // Calculate marker size as a fraction of line length (5% of line length)
            double lineBasedSize = lineLength * lineFraction;
            double sizeStart = Math.Min(screenBasedSizeStart, lineBasedSize);                           // Use the smaller of: screen-based size (for visibility) or line-based size (to not exceed line)
            double sizeEnd = Math.Min(screenBasedSizeEnd, lineBasedSize);                               // This ensures markers are visible but never larger than a reasonable fraction of the line
            sizeStart = Math.Max(0.0001, Math.Min(sizeStart, lineLength * 0.1));                        // Clamp to reasonable bounds   Max 10% of line length
            sizeEnd = Math.Max(0.0001, Math.Min(sizeEnd, lineLength * 0.1));
            StartVisual.SideLength = sizeStart;                                                         // Update cube sizes
            if (_isEnd && EndVisual != null)
                EndVisual.SideLength = sizeEnd;
        }

        /// <summary>
        /// Sets the marker to selected state and refreshes visuals.
        /// </summary>
        public void Select() {
            _isSelected = true;
            UpdateVisual();
        }

        /// <summary>
        /// Clears the selected state and refreshes visuals.
        /// </summary>
        public void Deselect() {
            _isSelected = false;
            UpdateVisual();
        }

        // Serialization helper
        /// <summary>
        /// Converts this marker to serializable data for persistence.
        /// </summary>
        /// <returns>Serializable <see cref="FlawData"/>.</returns>
        public FlawData ToData() {
            return new FlawData {
                Id = Id,
                Label = Label,
                StartX = StartPoint.X,
                StartY = StartPoint.Y,
                StartZ = StartPoint.Z,
                EndX = EndPoint.X,
                EndY = EndPoint.Y,
                EndZ = EndPoint.Z,
                ColorArgb = (Color.A << 24) | (Color.R << 16) | (Color.G << 8) | Color.B
            };
        }

        /// <summary>
        /// Reconstructs a <see cref="FlawMarker"/> from serialized data.
        /// </summary>
        /// <param name="flawData_">Serialized data.</param>
        /// <returns>Reconstructed marker.</returns>
        public static FlawMarker FromData(FlawData flawData_) {
            var color = Color.FromArgb(
                (Byte)((flawData_.ColorArgb >> 24) & 0xFF),
                (Byte)((flawData_.ColorArgb >> 16) & 0xFF),
                (Byte)((flawData_.ColorArgb >> 8) & 0xFF),
                (Byte)(flawData_.ColorArgb & 0xFF)
            );
            FlawMarker flaw = new(
                flawData_.Id,
                new Point3D(flawData_.StartX, flawData_.StartY, flawData_.StartZ),
                new Point3D(flawData_.EndX, flawData_.EndY, flawData_.EndZ),
                color,
                flawData_.Label
            );
            flaw.UpdateEndPoint(new Point3D(flawData_.EndX, flawData_.EndY, flawData_.EndZ), true);
            return flaw;
        }
    }

    // DTO for JSON persistence
    /// <summary>
    /// Serializable DTO for flaw markers used for JSON persistence.
    /// </summary>
    public record FlawData {
        /// <summary>Marker id.</summary>
        public Int32 Id { get; init; }
        /// <summary>Optional label.</summary>
        public String? Label { get; init; }
        /// <summary>Start point X.</summary>
        public Double StartX { get; init; }
        /// <summary>Start point Y.</summary>
        public Double StartY { get; init; }
        /// <summary>Start point Z.</summary>
        public Double StartZ { get; init; }
        /// <summary>End point X.</summary>
        public Double EndX { get; init; }
        /// <summary>End point Y.</summary>
        public Double EndY { get; init; }
        /// <summary>End point Z.</summary>
        public Double EndZ { get; init; }
        /// <summary>ARGB packed color.</summary>
        public Int32 ColorArgb { get; init; }
    }
}