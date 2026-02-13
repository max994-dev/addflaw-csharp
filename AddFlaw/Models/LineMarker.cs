using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using HelixToolkit.Wpf;

namespace AddFlaw.Models
{
    public class LineMarker
    {
        private bool isEnd = false;
        private bool isSelected = false;
     
        public int Id { get; }
        public string Label { get; private set; }

        public Point3D StartPoint { get; set; }
        public Point3D EndPoint { get; set; }
        public Color Color { get; set; }

        public Point3D MidPoint => new Point3D(
        (StartPoint.X + EndPoint.X) * 0.5,
        (StartPoint.Y + EndPoint.Y) * 0.5,
        (StartPoint.Z + EndPoint.Z) * 0.5);

        public double BaseSphereRadius { get; set; } = 0.5;

        public SolidColorBrush ColorBrush
        {
            get { return new SolidColorBrush(Color); }
        }

        // Visual pieces
        public LinesVisual3D LineVisual { get; private set; }
        public CubeVisual3D StartVisual { get; }
        public CubeVisual3D EndVisual { get; private set; }
        public SphereVisual3D SphereVisual { get; set; }

        public BillboardTextVisual3D TextVisual { get; }

        public ModelVisual3D ModelVisual { get; }

        public double ScreenFraction { get; set; } = 0.015;

        public LineMarker(int id, Point3D start, Point3D end, Color color, string label = "")
        {
            Id = id;
            Label = label ?? string.Empty;

            StartPoint = start;
            EndPoint = end;
            Color = color;
            var mat = MaterialHelper.CreateMaterial(Color);

            LineVisual = new LinesVisual3D
            {
                Color = Color,
                Thickness = 2.0,
                Points = new Point3DCollection { StartPoint, EndPoint }
            };


            StartVisual = new CubeVisual3D
            {
                Center = StartPoint,
                SideLength = .02,
                Material = mat,
                
            };

            
            TextVisual = new BillboardTextVisual3D
            {
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
            ModelVisual.Children.Add(LineVisual);
            ModelVisual.Children.Add(StartVisual);

            //ModelVisual.Children.Add(TextVisual);
            
        }

        private string BuildDisplayText()
        {
            if (string.IsNullOrWhiteSpace(Label))
                return Id.ToString();

            return $"Marker {Id}: {Label}";
        }

        private Point3D GetLabelPosition()
        {
            var mid = new Point3D(
                StartPoint.X * 0 + EndPoint.X * 1,
                StartPoint.Y * 0 + EndPoint.Y * 1,
                StartPoint.Z * 0 + EndPoint.Z * 1);

            // Tiny offset so text doesn't intersect the surface
            return new Point3D(mid.X, mid.Y + .7, mid.Z);
        }
        public void SetLabel(string label)
        {
            Label = label ?? string.Empty;
            TextVisual.Text = BuildDisplayText();
        }

        public void Start(Point3D newEnd)
        {
            isSelected = true;
            UpdateEndPoint(newEnd, false);
        }
        public void End(Point3D newEnd)
        {
            isSelected = false;
            UpdateEndPoint(newEnd, true);
        }

        public void UpdateEndPoint(Point3D newEnd, bool isEnd)
        {
            EndPoint = newEnd;
            this.isEnd = isEnd;
            if(isEnd)
            {
                var mat = MaterialHelper.CreateMaterial(Color);

                EndVisual = new CubeVisual3D
                {
                    Center = EndPoint,
                    SideLength = .02,
                    Material = mat
                };

                SphereVisual = new SphereVisual3D
                {
                    Center = MidPoint,
                    Radius = BaseSphereRadius,
                    Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(100, Color.R, Color.G, Color.B))),
                };
                SphereVisual.Visible = false;

                ModelVisual.Children.Add(EndVisual);
                ModelVisual.Children.Add(SphereVisual);

            }
            TextVisual.Position = GetLabelPosition();
            UpdateVisual();
        }

        private void UpdateVisual()
        {
            if (LineVisual != null)
            {
                Color showColor = isSelected ? Colors.Yellow : Color;
                
                LineVisual.Points = new Point3DCollection { StartPoint, EndPoint };
                LineVisual.Color = isSelected ? Colors.Yellow : Color;
                LineVisual.Thickness = isSelected ? 6 : 4;

                StartVisual.Center = StartPoint;    
                StartVisual.Material = MaterialHelper.CreateMaterial(showColor);
                StartVisual.Visible = isSelected;

                if (isEnd)
                {
                    EndVisual.Center = EndPoint;
                    EndVisual.Material = MaterialHelper.CreateMaterial(showColor);
                    EndVisual.Visible = isSelected;
                }

            }

        }

        
        // Updates the marker sizes based on camera distance 
        public void UpdateMarkerSizes(HelixViewport3D viewport)
        {
            if (viewport?.Camera is not PerspectiveCamera cam)
                return;

            // Calculate line length to base marker size on line size, not viewport
            Vector3D lineVec = EndPoint - StartPoint;
            double lineLength = lineVec.Length;
            if (lineLength < 1e-6) lineLength = 1.0; // Avoid division by zero

            // Calculate distance from camera to start and end points
            Vector3D toStart = StartPoint - cam.Position;
            Vector3D toEnd = EndPoint - cam.Position;
            double distStart = toStart.Length;
            double distEnd = toEnd.Length;

            if (distStart < 1e-6) distStart = 1.0;
            if (distEnd < 1e-6) distEnd = 1.0;

            // Calculate FOV-based world height at each distance for screen visibility
            double fovRad = cam.FieldOfView * Math.PI / 180.0;
            const double screenFraction = 0.01; // 1% of viewport height for minimum visibility
            
            double worldHeightStart = 2.0 * distStart * Math.Tan(fovRad / 2.0);
            double worldHeightEnd = 2.0 * distEnd * Math.Tan(fovRad / 2.0);
            
            double screenBasedSizeStart = worldHeightStart * screenFraction;
            double screenBasedSizeEnd = worldHeightEnd * screenFraction;

            // Calculate marker size as a fraction of line length (5% of line length)
            const double lineFraction = 0.05;
            double lineBasedSize = lineLength * lineFraction;

            // Use the smaller of: screen-based size (for visibility) or line-based size (to not exceed line)
            // This ensures markers are visible but never larger than a reasonable fraction of the line
            double sizeStart = Math.Min(screenBasedSizeStart, lineBasedSize);
            double sizeEnd = Math.Min(screenBasedSizeEnd, lineBasedSize);

            // Clamp to reasonable bounds
            sizeStart = Math.Max(0.0001, Math.Min(sizeStart, lineLength * 0.1)); // Max 10% of line length
            sizeEnd = Math.Max(0.0001, Math.Min(sizeEnd, lineLength * 0.1));

            // Update cube sizes
            StartVisual.SideLength = sizeStart;
            if (isEnd && EndVisual != null)
            {
                EndVisual.SideLength = sizeEnd;
            }
        }

        public void Select()
        {
            isSelected = true;
            UpdateVisual();
        }

        public void Deselect()
        {
            isSelected = false;
            UpdateVisual();
        }

        // Serialization helper
        public LineData ToData()
        {
            return new LineData
            {
                id = Id,
                label = Label,
                StartX = StartPoint.X,
                StartY = StartPoint.Y,
                StartZ = StartPoint.Z,
                EndX = EndPoint.X,
                EndY = EndPoint.Y,
                EndZ = EndPoint.Z,
                ColorArgb = (Color.A << 24) | (Color.R << 16) | (Color.G << 8) | Color.B
            };
        }

        public static LineMarker FromData(LineData d)
        {
            var color = Color.FromArgb(
                (byte)((d.ColorArgb >> 24) & 0xFF),
                (byte)((d.ColorArgb >> 16) & 0xFF),
                (byte)((d.ColorArgb >> 8) & 0xFF),
                (byte)(d.ColorArgb & 0xFF)
            );
            LineMarker line = new LineMarker(
                d.id,
                new Point3D(d.StartX, d.StartY, d.StartZ),
                new Point3D(d.EndX, d.EndY, d.EndZ),
                color,
                d.label
            );
            line.UpdateEndPoint(new Point3D(d.EndX, d.EndY, d.EndZ), true);
            return line;
        }
    }

    // DTO for JSON persistence
    public record LineData
    {
        public int id { get; init; }
        public string label { get; init; }
        public double StartX { get; init; }
        public double StartY { get; init; }
        public double StartZ { get; init; }
        public double EndX { get; init; }
        public double EndY { get; init; }
        public double EndZ { get; init; }
        public int ColorArgb { get; init; }
    }
}