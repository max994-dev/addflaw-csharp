using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using AddFlaw.Models;
using HelixToolkit.Wpf;

namespace AddFlaw.Managers
{
    public class LineManager
    {
        private HelixViewport3D viewport;
        // Lines created
        private List<LineMarker> lines;

        private LineMarker selectedLine;
        
        private LineMarker currentLine;
        
        private Color currentColor = Colors.Red;

        private int nextId = 1;

        public int Count => lines.Count;
        public bool HasSelection => selectedLine != null;

        public IEnumerable<LineMarker> Lines => lines;
        public LineMarker CurrentLine => currentLine;
        public LineManager(HelixViewport3D viewport)
        {
            this.viewport = viewport;
            lines = new List<LineMarker>();
        }

        private DispatcherTimer sphereGrowTimer;
        private LineMarker growingLine;
        private double maxSphereRadius = 100;  // tune


        private void EnsureSphereTimer()
        {
            if (sphereGrowTimer != null) return;

            sphereGrowTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(10) // grow speed
            };
            sphereGrowTimer.Tick += SphereGrowTimer_Tick;
        }

        private void SphereGrowTimer_Tick(object? sender, EventArgs e)
        {
            if (growingLine?.SphereVisual == null) return;

            double r = growingLine.SphereVisual.Radius;
            double newR = r + growingLine.BaseSphereRadius * 0.2; // 20% of base per tick

            if (newR > maxSphereRadius)
                newR = maxSphereRadius;

            growingLine.SphereVisual.Radius = newR;
            
        }

        public void StartHighlightSphere(LineMarker line)
        {
            if (line?.SphereVisual == null) return;

            EnsureSphereTimer();

            growingLine = line;
            // reset radius to base before growth
            line.SphereVisual.Radius = line.BaseSphereRadius;
            line.SphereVisual.Visible = true;
            sphereGrowTimer.Start();
        }

        public void StopHighlightSphere(LineMarker line)
        {
            if (sphereGrowTimer != null)
                sphereGrowTimer.Stop();

            if (line?.SphereVisual != null)
            {
                line.SphereVisual.Visible = false;
                // restore original size
                line.SphereVisual.Radius = line.BaseSphereRadius;
            }

            growingLine = null;
        }


        public void SetLineColor(Color color)
        {
            currentColor = color;
        }

        public void StartLine(Point3D start)
        {
            currentLine = new LineMarker(nextId, start, start, currentColor, label: "");
            currentLine.Start(start);
            currentLine.UpdateMarkerSizes(viewport);
            viewport.Children.Add(currentLine.ModelVisual);
        }

        public bool EndLine(Point3D end)
        {
            if (currentLine == null)
                return false;

            if (currentLine.StartPoint.Equals(end))
            {
                viewport.Children.Remove(currentLine.ModelVisual);
                currentLine = null;
                return false;
            }

            currentLine.End(end); // Force visual update
            currentLine.UpdateMarkerSizes(viewport);
            lines.Add(currentLine);
            currentLine = null;
            nextId++;
            return true;
        }

        public void CancelLine()
        {
            viewport.Children.Remove(currentLine?.ModelVisual);
            currentLine = null;
        }

        public void MoveLine(Point3D newEnd)
        {
            currentLine?.UpdateEndPoint(newEnd, false);
            if (currentLine != null)
            {
                currentLine.UpdateMarkerSizes(viewport);
            }
        }

        public void SelectLine(LineMarker line)
        {
            DeselectLine();
            selectedLine = line;
            selectedLine?.Select();
        }
        public void DeselectLine()
        {
            selectedLine?.Deselect();
            selectedLine = null;
        }

        public void DeleteSelectedLine()
        {
            if (selectedLine == null)
                return;

            // Remove visuals from viewport
            viewport.Children.Remove(selectedLine.ModelVisual);

            // Remove from list
            lines.Remove(selectedLine);

            // Clear selection
            selectedLine = null;
        }

        public void UpdateSelectedLabel(string label)
        {
            selectedLine?.SetLabel(label);
        }

        public void UpdateSelectedLineMarkerSizes()
        {
            if (selectedLine != null)
            {
                selectedLine.UpdateMarkerSizes(viewport);
            }
        }

        // Save lines to file (JSON)
        public void SaveLines(string path)
        {
            var data = lines.Select(l => l.ToData()).ToList();
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(path, json);
        }

        // Load lines from file (JSON). Existing lines are cleared.
        public void LoadLines(string path)
        {
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<List<LineData>>(json);
            if (data == null) return;

            // Remove existing visuals that were created by this manager
            foreach (var l in lines)
            {
                viewport.Children.Remove(l.ModelVisual);
            }
            lines.Clear();
            DeselectLine();
            CancelLine();
            
            // Create and add visuals
            foreach (var d in data)
            {
                var lm = LineMarker.FromData(d);
                lm.UpdateMarkerSizes(viewport); // Set size based on current camera when loading
                lines.Add(lm);
                viewport.Children.Add(lm.ModelVisual);
            }
            nextId = lines.Count > 0 ? lines.Max(l => l.Id) + 1 : 1;
        }
        
        public LineMarker? GetLineByModel(Model3D model)
        {
            LineMarker? lineMarker = null;
            lines.ForEach(line =>
            {
                if (line.LineVisual.Content == model)
                {
                    lineMarker = line;
                }
            });
            return lineMarker;
        }
    }
}