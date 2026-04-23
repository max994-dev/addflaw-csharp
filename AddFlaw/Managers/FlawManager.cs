using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using AddFlaw.Models;
using HelixToolkit.Maths;
using HelixToolkit.Wpf;
using Color = System.Windows.Media.Color;

namespace AddFlaw.Managers {
    /// <summary>
    /// Manages creation, selection, visualization, serialization, and highlighting of 3D flaw markers
    /// within a <see cref="HelixViewport3D"/>.
    /// </summary>
    public class FlawManager {
        private readonly HelixViewport3D _viewport;
        private readonly List<FlawMarker> _flaws;
        private FlawMarker? _selectedFlaw;
        private Color _currentColor = Colors.Red;
        private Int32 _nextId = 1;
        public Int32 Count => _flaws.Count;                                         // Total number of flaws managed
        public Boolean HasSelection => _selectedFlaw != null;                       // True if a flaw is currently selected
        public IEnumerable<FlawMarker> Flaws => _flaws;                             // All flaws tracked by the manager
        public FlawMarker? CurrentFlaw { get; private set; }                        // The flaw currently being drawn/edited
        /// <summary>
        /// Constructs a new manager bound to the provided viewport.
        /// </summary>
        /// <param name="viewport_">Target Helix viewport.</param>
        public FlawManager(HelixViewport3D viewport_) {
            this._viewport = viewport_;
            _flaws = [];
        }
        private DispatcherTimer? _sphereGrowTimer;
        private FlawMarker? _growingFlaw;
        private readonly Double _maxSphereRadius = 100;  // tune
        private void EnsureSphereTimer() {
            if (_sphereGrowTimer != null) return;

            _sphereGrowTimer = new DispatcherTimer {
                Interval = TimeSpan.FromMilliseconds(10) // grow speed
            };
            _sphereGrowTimer.Tick += SphereGrowTimer_Tick;
        }

        private void SphereGrowTimer_Tick(Object? sender_, EventArgs evt_) {
            if (_growingFlaw?.SphereVisual == null)
                return;
            Double curRadius = _growingFlaw.SphereVisual.Radius;
            Double newRadius = (curRadius * 1.02) + (_growingFlaw.BaseSphereRadius * 1);       // Amount it grows with each tick
            if (newRadius > _maxSphereRadius)
                newRadius = _maxSphereRadius;
            _growingFlaw.SphereVisual.Radius = newRadius;
        }

        /// <summary>
        /// Starts growing the highlight sphere for the given flaw and makes it visible.
        /// </summary>
        /// <param name="flaw_">Flaw marker to highlight.</param>
        public void StartHighlightSphere(FlawMarker flaw_) {
            if (flaw_?.SphereVisual == null)
                return;
            EnsureSphereTimer();
            _growingFlaw = flaw_;
            flaw_.SphereVisual.Radius = flaw_.BaseSphereRadius;                     // reset radius to base before growth
            flaw_.SphereVisual.Visible = true;
            if (_sphereGrowTimer is null)
                EnsureSphereTimer();
            _sphereGrowTimer?.Start();
        }

        /// <summary>
        /// Stops growing the highlight sphere and hides it, restoring base radius.
        /// </summary>
        /// <param name="flaw_">Flaw marker to stop highlighting.</param>
        public void StopHighlightSphere(FlawMarker flaw_) {
            _sphereGrowTimer?.Stop();
            if (flaw_?.SphereVisual != null) {
                flaw_.SphereVisual.Visible = false;
                flaw_.SphereVisual.Radius = flaw_.BaseSphereRadius;                 // restore original size
            }
            _growingFlaw = null;
        }

        /// <summary>Sets the color to use for newly created flaws.</summary>
        /// <param name="color_">New color.</param>
        public void SetFlawColor(Color color_) => _currentColor = color_;

        /// <summary>
        /// Begins a new flaw at the given start point and adds its visuals to the viewport.
        /// </summary>
        /// <param name="start_">Start point.</param>
        public void StartFlaw(Point3D start_) {
            CurrentFlaw = new FlawMarker(_nextId, start_, start_, _currentColor, label_: "");
            CurrentFlaw.Start(start_);
            CurrentFlaw.UpdateMarkerSizes(_viewport);
            _viewport.Children.Add(CurrentFlaw.ModelVisual);
        }

        /// <summary>
        /// Completes the current flaw at the specified end point. Returns false if degenerate.
        /// </summary>
        /// <param name="end_">End point.</param>
        /// <returns>True if the flaw was added; false if canceled.</returns>
        public Boolean EndFlaw(Point3D end_) {
            if (CurrentFlaw == null)
                return false;

            if (CurrentFlaw.StartPoint.Equals(end_)) {
                _ = _viewport.Children.Remove(CurrentFlaw.ModelVisual);
                CurrentFlaw = null;
                return false;
            }
            CurrentFlaw.End(end_); // Force visual update
            CurrentFlaw.UpdateMarkerSizes(_viewport);
            _flaws.Add(CurrentFlaw);
            CurrentFlaw = null;
            _nextId++;
            return true;
        }

        /// <summary>
        /// Cancels the current in-progress flaw and removes its visuals from the viewport.
        /// </summary>
        public void CancelFlaw() {
            _ = _viewport.Children.Remove(CurrentFlaw?.ModelVisual);
            CurrentFlaw = null;
        }

        /// <summary>
        /// Updates the end point of the current in-progress flaw without finalizing it.
        /// </summary>
        /// <param name="newEnd_">New end point.</param>
        public void MoveFlaw(Point3D newEnd_) {
            CurrentFlaw?.UpdateEndPoint(newEnd_, false);
            CurrentFlaw?.UpdateMarkerSizes(_viewport);
        }

        /// <summary>
        /// Selects the given flaw, updating its visuals.
        /// </summary>
        /// <param name="flaw_">Flaw to select.</param>
        public void SelectFlaw(FlawMarker? flaw_) {
            DeselectFlaw();
            _selectedFlaw = flaw_;
            _selectedFlaw?.Select();
        }
        /// <summary>
        /// Clears the current selection and updates visuals.
        /// </summary>
        public void DeselectFlaw() {
            _selectedFlaw?.Deselect();
            _selectedFlaw = null;
        }

        /// <summary>
        /// Deletes the currently selected flaw from both the viewport and internal list.
        /// </summary>
        public void DeleteSelectedFlaw() {
            if (_selectedFlaw == null)
                return;
            _ = _viewport.Children.Remove(_selectedFlaw.ModelVisual);         // Remove visuals from viewport
            _ = _flaws.Remove(_selectedFlaw);                                  // Remove from list
            _selectedFlaw = null;                                                   // Clear selection
        }

        /// <summary>
        /// Updates the label of the selected flaw.
        /// </summary>
        /// <param name="label_">New label text.</param>
        public void UpdateSelectedLabel(String label_) => _selectedFlaw?.SetLabel(label_);

        public void UpdateSelectedFlawMarkerSizes() => _selectedFlaw?.UpdateMarkerSizes(_viewport);

        // Save flaws to file (JSON)
        private static readonly JsonSerializerOptions _cachedJsonOptions = new() { WriteIndented = true };

        /// <summary>
        /// Saves all flaws to a JSON file.
        /// </summary>
        /// <param name="path_">Destination file path.</param>
        public void SaveFlaws(String path_) {
            String json = JsonSerializer.Serialize((List<FlawData>)[.. _flaws.Select(flawMarker => flawMarker.ToData())], _cachedJsonOptions);
            File.WriteAllText(path_, json);
        }

        /// <summary>
        /// Loads flaws from a JSON file, clearing existing ones and rebuilding visuals.
        /// </summary>
        /// <param name="path_">Source file path.</param>
        public void LoadFlaws(String path_) {
            if (!File.Exists(path_))
                return;
            String json = File.ReadAllText(path_);
            var data = JsonSerializer.Deserialize<List<FlawData>>(json);
            if (data == null) return;
            foreach (FlawMarker flawMarker in _flaws) {
                _ = _viewport.Children.Remove(flawMarker.ModelVisual);        // Remove existing visuals that were created by this manager
            }
            _flaws.Clear();
            DeselectFlaw();
            CancelFlaw();
            foreach (FlawData flawData in data) {                                   // Create and add visuals
                var lm = FlawMarker.FromData(flawData);
                lm.UpdateMarkerSizes(_viewport);                           // Ensure sizes are correct for current viewport
                _flaws.Add(lm);
                _viewport.Children.Add(lm.ModelVisual);
            }
            _nextId = _flaws.Count > 0 ? _flaws.Max(l => l.Id) + 1 : 1;
        }

        /// <summary>
        /// Finds a flaw marker associated with the given model instance (hit-test helper).
        /// </summary>
        /// <param name="model_">Model3D to match.</param>
        /// <returns>The corresponding flaw marker or null.</returns>
        public FlawMarker? GetFlawByModel(Model3D? model_) {
            FlawMarker? flawMarker = null;
            _flaws.ForEach(flaw => {
                if (flaw.FlawVisual.Content == model_) {
                    flawMarker = flaw;
                }
            });
            return flawMarker;
        }
    }
}