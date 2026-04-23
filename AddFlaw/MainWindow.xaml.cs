using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AddFlaw.Managers;
using AddFlaw.Models;
using Microsoft.Win32;
using Windows.UI.Input.Inking;
using Color = System.Windows.Media.Color;
using Path = System.IO.Path;
using Point = System.Windows.Point;

namespace AddFlaw {
    /// <summary>
    /// Main application window orchestrating model loading, flaw creation/selection, camera interaction,
    /// and UI updates for the AddFlaw tool.
    /// </summary>
    public partial class MainWindow : Window {
        private readonly ModelManager _modelManager;                                // Manages 3D model load/color
        private readonly FlawManager _flawManager;                                  // Manages flaw markers
        private readonly ViewportInteraction _viewportInteraction;                  // Handles camera and hit tests
        private readonly UIStateManager _uiStateManager;                            // Updates UI state/labels
        private readonly Color[] _modelColors = [Colors.Blue, Colors.Gray];   // Available model colors
        private readonly Color[] _flawColors = [Colors.Red, Colors.Green];    // Available flaw colors
        private Boolean _waitEndPoint;                                              // Indicates awaiting 2nd point
        private Byte _emissionAlpha = 60;                                           // Current emissive alpha
        private Boolean _targetSphereGrowing = false;                                  // Target sphere render-loop state
        private DateTime _lastRenderTime = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class and sets up the main application window.
        /// </summary>
        /// <remarks>This constructor initializes the user interface components and configures the core managers responsible for 3D model handling, flaw management, viewport interaction, and UI state. 
        /// It also sets up event handlers required for viewport operations.</remarks>
        public MainWindow() {
            InitializeComponent();
            _modelManager = new ModelManager(viewport3D, modelRoot);
            _flawManager = new FlawManager(viewport3D);
            _viewportInteraction = new ViewportInteraction(viewport3D);
            _uiStateManager = new UIStateManager(addFlawButton, applyFlawLabelButton, statusLabel, flawsCountLabel, selectedFlawIdLabel, flawLabelTextBox);
            InitializeManagers();
            InitializeViewportEvents();
            CompositionTarget.Rendering += OnRendering;
        }

        /// <summary>
        /// Initializes manager-related UI state from current control values.
        /// </summary>
        private void InitializeManagers() {
            _emissionAlpha = (Byte)EmissionAlphaSlider.Value;
            EmissionAlphaLabel.Text = _emissionAlpha.ToString();
        }

        /// <summary>
        /// Subscribes to viewport interaction events.
        /// </summary>
        private void InitializeViewportEvents() {
            viewport3D.MouseDown += Viewport3D_MouseDown;
            viewport3D.MouseMove += Viewport3D_MouseMove;
            viewport3D.MouseUp += Viewport3D_MouseUp;
            viewport3D.LostFocus += Viewport3D_LostFocus;
            viewport3D.MouseWheel += Viewport3D_MouseWheel;
        }

        /// <summary>
        /// Cancels flaw placement when the viewport loses focus.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void Viewport3D_LostFocus(Object sender_, RoutedEventArgs evt_) {
            _waitEndPoint = false;
            _flawManager.CancelFlaw();
        }

        /// <summary>
        /// Handles the "walk up" control for moving camera target or position.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkUpButton_Click(Object sender_, RoutedEventArgs evt_) {                         // Y - Up / Towards Home
            evt_.Handled = true;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                _viewportInteraction.MoveTarget(0, -1, 0);
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                _viewportInteraction.MovePosition(0, -1, 0);
            else
                evt_.Handled = false;
        }

        /// <summary>
        /// Handles the "walk down" control for moving camera target or position.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkDownButton_Click(Object sender_, RoutedEventArgs evt_) {                       // Y - Down / Away from Home
            evt_.Handled = true;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                _viewportInteraction.MoveTarget(0, +1, 0);
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                _viewportInteraction.MovePosition(0, +1, 0);
            else
                evt_.Handled = false;
        }

        /// <summary>
        /// Handles the "walk left" control for moving camera target or position.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkLeftButton_Click(Object sender_, RoutedEventArgs evt_) {                       // X - Left / Away from Home
            evt_.Handled = true;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                _viewportInteraction.MoveTarget(+1, 0, 0);
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                _viewportInteraction.MovePosition(+1, 0, 0);
            else
                evt_.Handled = false;
        }

        /// <summary>
        /// Handles the "walk right" control for moving camera target or position.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkRightButton_Click(Object sender_, RoutedEventArgs evt_) {                      // X - Right / Towards Home
            evt_.Handled = true;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                _viewportInteraction.MoveTarget(-1, 0, 0);
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                _viewportInteraction.MovePosition(-1, 0, 0);
            else
                evt_.Handled = false;
        }

        /// <summary>
        /// Resets camera to show all content.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkCenterButton_Click(Object sender_, RoutedEventArgs evt_) {
            viewport3D.ZoomExtents();
            _flawManager.UpdateSelectedFlawMarkerSizes();
        }

        /// <summary>
        /// Moves camera inwards (Z+).
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkInButton_Click(Object sender_, RoutedEventArgs evt_) {                         // Z - In / Away from Home
            evt_.Handled = true;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                _viewportInteraction.MoveTarget(0, 0, +1);
            else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                _viewportInteraction.MovePosition(0, 0, +1);
            else
                evt_.Handled = false;
        }

        /// <summary>
        /// Moves camera outwards (Z-).
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void WalkOutButton_Click(Object sender_, RoutedEventArgs evt_) {                        // Z - Out / Towards Home
            evt_.Handled = true;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) {
                _viewportInteraction.MoveTarget(0, 0, -1);
                _flawManager.UpdateSelectedFlawMarkerSizes();
            } else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) {
                _viewportInteraction.MovePosition(0, 0, -1);
                _flawManager.UpdateSelectedFlawMarkerSizes();
            } else
                evt_.Handled = false;
        }

        /// <summary>
        /// Increases camera field of view.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void ExpandFieldOfView_Click(Object sender_, RoutedEventArgs evt_) {
            _viewportInteraction.ChangeFov(+1.0);
            _flawManager.UpdateSelectedFlawMarkerSizes();
        }

        /// <summary>
        /// Decreases camera field of view.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void NarrowFieldOfView_Click(Object sender_, RoutedEventArgs evt_) {
            _viewportInteraction.ChangeFov(-1.0);
            _flawManager.UpdateSelectedFlawMarkerSizes();
        }

        /// <summary>
        /// Refreshes the list UI binding to current flaws.
        /// </summary>
        private void RefreshFlawList() {
            if (flawListBox == null || _flawManager == null) return;

            flawListBox.ItemsSource = null;
            flawListBox.ItemsSource = _flawManager.Flaws;
        }

        /// <summary>
        /// Double-click handler to focus camera on the selected flaw.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void FlawListBox_MouseDoubleClick(Object sender_, MouseButtonEventArgs evt_) {
            if (flawListBox.SelectedItem is not FlawMarker marker)
                return;

            _viewportInteraction.FocusCameraOnFlawAnimated(marker);
        }

        /// <summary>
        /// Updates selection state when the flaw list selection changes.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Selection changed args.</param>
        private void FlawListBox_SelectionChanged(Object sender_, SelectionChangedEventArgs evt_) {
            _uiStateManager.SetStatus("Flaw placement canceled.");

            if (flawListBox.SelectedItem is FlawMarker flaw) {
                _flawManager.SelectFlaw(flaw);
                _uiStateManager.UpdateSelectedFlaw(flaw);
            } else {
                _flawManager.SelectFlaw(null);
                _uiStateManager.UpdateSelectedFlaw(null);
            }
        }

        private static Boolean IsWithin(DependencyObject? child_, DependencyObject target_) {
            while (child_ != null) {
                if (child_ == target_)
                    return true;
                child_ = VisualTreeHelper.GetParent(child_);
            }
            return false;
        }

        private void FlawListBox_LostFocus(Object sender_, RoutedEventArgs evt_) {
            if (Keyboard.FocusedElement is DependencyObject focused && (IsWithin(focused, flawLabelTextBox) || IsWithin(focused, applyFlawLabelButton)))        // Keep selection if focus moved to textbox or apply button
                return;
            flawListBox.SelectedItem = null;                                                                                                    // Otherwise, clear selection when the listbox loses focus
        }

        private void FlawLabelTextBox_LostFocus(Object sender_, RoutedEventArgs evt_) {
            if (Keyboard.FocusedElement is DependencyObject focused && (IsWithin(focused, flawListBox) || IsWithin(focused, applyFlawLabelButton)))             // Keep selection if focus moved to listbox or apply button
                return;
            flawListBox.SelectedItem = null;                                                                                                    // Otherwise, clear selection when the listbox loses focus
        }

        private void ApplyFlawLabelButton_LostFocus(Object sender_, RoutedEventArgs evt_) {
            if (Keyboard.FocusedElement is DependencyObject focused && (IsWithin(focused, flawListBox) || IsWithin(focused, flawLabelTextBox)))                 // Keep selection if focus moved to listbox or textbox
                return;
            flawListBox.SelectedItem = null;                                                                                                    // Otherwise, clear selection when the listbox loses focus
        }

        /// <summary>
        /// Applies the label from the text box to the selected flaw.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void ApplyFlawLabelButton_Click(Object sender_, RoutedEventArgs evt_) {
            if (!_flawManager.HasSelection)
                return;
            _flawManager.UpdateSelectedLabel(flawLabelTextBox.Text);
            RefreshFlawList();
            _uiStateManager.SetStatus($"Successfully Updated!");
        }

        /// <summary>
        /// Handles changes to the emission alpha slider to update model material.
        /// </summary>
        /// <param name="sender_">Slider sender.</param>
        /// <param name="evt_">Value changed args.</param>
        private void EmissionAlphaSlider_ValueChanged(Object sender_, RoutedPropertyChangedEventArgs<Double> evt_) {
            if (_modelManager == null) return; // Guard clause
            _emissionAlpha = (Byte)evt_.NewValue;
            if (EmissionAlphaLabel != null)
                EmissionAlphaLabel.Text = _emissionAlpha.ToString();

            _modelManager.SetEmissionAlpha(_emissionAlpha);
        }

        /// <summary>
        /// Opens a file dialog to load a model and updates UI state.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void LoadModelButton_Click(Object sender_, RoutedEventArgs evt_) {
            OpenFileDialog dialog = new() {
                Filter = "3D Model Files (*.obj;*.stl)|*.obj;*.stl|OBJ Files (*.obj)|*.obj|STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
                Title = "Open 3D Model"
            };

            if (dialog.ShowDialog() == true) {
                try {
                    _modelManager.LoadModel(dialog.FileName);
                    _modelManager.SetModelColor(_modelColors[0]);
                    filePathLabel.Text = Path.GetFileName(dialog.FileName);
                    _uiStateManager.SetStatus("Model loaded successfully");
                } catch (Exception ex) {
                    _ = MessageBox.Show($"Error loading model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    filePathLabel.Text = "Error loading file";
                }
            }
        }

        /// <summary>
        /// Saves flaws to a JSON file via a save dialog.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void SaveFlawsButton_Click(Object sender_, RoutedEventArgs evt_) {
            var dlg = new SaveFileDialog {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Save Flaws"
            };

            if (dlg.ShowDialog() == true) {
                try {
                    _flawManager.SaveFlaws(dlg.FileName);
                    _uiStateManager.SetStatus("Flaws saved");
                } catch (Exception ex) {
                    _ = MessageBox.Show($"Error saving flaws: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Loads flaws from a JSON file via an open dialog and refreshes UI.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void LoadFlawsButton_Click(Object sender_, RoutedEventArgs evt_) {
            var dlg = new OpenFileDialog {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Load Flaws"
            };

            if (dlg.ShowDialog() == true) {
                try {
                    _flawManager.LoadFlaws(dlg.FileName);
                    RefreshFlawList();
                    _uiStateManager.SetStatus("Flaws loaded");
                    _uiStateManager.UpdateFlawCount(_flawManager.Count);
                } catch (Exception ex) {
                    _ = MessageBox.Show($"Error loading flaws: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Toggles flaw adding mode.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void AddFlawButton_Click(Object sender_, RoutedEventArgs evt_) => _uiStateManager.ToggleFlawAddingMode();

        /// <summary>
        /// Changes the model color based on combo selection.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Selection changed args.</param>
        private void ModelColorCombo_SelectionChanged(Object sender_, SelectionChangedEventArgs evt_) {
            if (_modelManager == null) return; // Guard clause

            Color color = _modelColors[modelColorCombo.SelectedIndex];
            _modelManager.SetModelColor(color);
        }

        /// <summary>
        /// Changes the flaw color based on combo selection.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Selection changed args.</param>
        private void FlawColorCombo_SelectionChanged(Object sender_, SelectionChangedEventArgs evt_) {
            if (_flawManager == null) return; // Guard clause

            Color color = _flawColors[flawColorCombo.SelectedIndex];
            _flawManager.SetFlawColor(color);
        }

        private void ShowAxisCheckBox_Checked(Object sender_, RoutedEventArgs evt_) => _modelManager?.ShowAxisLines(true);

        private void ShowAxisCheckBox_Unchecked(Object sender_, RoutedEventArgs evt_) => _modelManager?.ShowAxisLines(false);

        /// <summary>
        /// Handles keyboard shortcuts for camera movement, FOV, and flaw placement cancellation.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evtArgs_">Key event args.</param>
        private void Window_PreviewKeyDown(Object sender_, KeyEventArgs evtArgs_) {
            evtArgs_.Handled = false;
            if (evtArgs_.Key == Key.Delete) {
                if (_flawManager != null && _flawManager.HasSelection) {                           // Handle Delete: remove selected flaw
                    _flawManager.DeleteSelectedFlaw();
                    _uiStateManager.UpdateFlawCount(_flawManager.Count);
                    RefreshFlawList();
                    _uiStateManager.UpdateSelectedFlaw(null);
                }
                evtArgs_.Handled = true;
                return;
            }
            if (evtArgs_.Key == Key.Escape) {                                                           // Handle Esc: cancel flaw placement states
                if (_waitEndPoint) {
                    _flawManager.CurrentFlaw?.UpdateEndPoint(_flawManager.CurrentFlaw.StartPoint, false);
                    _waitEndPoint = false;
                    _uiStateManager.SetStatus("Second point canceled. Press Esc again to exit flaw placement.");
                } else if (_flawManager.CurrentFlaw != null) {
                    _waitEndPoint = false;
                    _flawManager.CancelFlaw();
                    _uiStateManager.SetStatus("Flaw placement canceled.");
                }
                evtArgs_.Handled = true;
                return;
            }
            Boolean shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            Boolean ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (evtArgs_.Key == Key.O) {
                if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                    _viewportInteraction.ChangeFov(+1.0);
                else
                    _viewportInteraction.ChangeFov(-1.0);
                _flawManager.UpdateSelectedFlawMarkerSizes();
            } else if (ctrl) {
                switch (evtArgs_.Key) {
                    case Key.Left:
                        _viewportInteraction.MovePosition(-1, 0, 0);
                        evtArgs_.Handled = true;
                        break;
                    case Key.Right:
                        _viewportInteraction.MovePosition(+1, 0, 0);
                        evtArgs_.Handled = true;
                        break;
                    case Key.Up:
                        _viewportInteraction.MovePosition(0, +1, 0);
                        evtArgs_.Handled = true;
                        break;
                    case Key.Down:
                        _viewportInteraction.MovePosition(0, -1, 0);
                        evtArgs_.Handled = true;
                        break;
                    case Key.OemPlus:
                    case Key.Add:
                        _viewportInteraction.MovePosition(0, 0, +1);
                        evtArgs_.Handled = true;
                        break;
                    case Key.OemMinus:
                    case Key.Subtract:
                        _viewportInteraction.MovePosition(0, 0, -1);
                        evtArgs_.Handled = true;
                        break;
                    case Key.PageUp:
                        _viewportInteraction.ChangeFov(-0.5);
                        UpdateSelectedLineMarkerSizes();
                        evtArgs_.Handled = true;
                        break;
                    case Key.PageDown:
                        _viewportInteraction.ChangeFov(+0.5);
                        UpdateSelectedLineMarkerSizes();
                        evtArgs_.Handled = true;
                        break;
                }
                if (Keyboard.IsKeyDown(Key.T) && !evtArgs_.IsRepeat) {
                    _modelManager?.StartTargetSphereGrowing();
                    _targetSphereGrowing = true;
                    _lastRenderTime = DateTime.MinValue;
                    evtArgs_.Handled = true;
                return;
            }
            }
            if (shift) {
                switch (evtArgs_.Key) {
                    case Key.Left:
                        _viewportInteraction.MoveTarget(-1, 0, 0);
                        _modelManager?.UpdateTargetSpherePosition();
                        evtArgs_.Handled = true;
                        break;

                    case Key.Right:
                        _viewportInteraction.MoveTarget(+1, 0, 0);
                        evtArgs_.Handled = true;
                        _modelManager?.UpdateTargetSpherePosition();
                        break;

                    case Key.Up:
                        _viewportInteraction.MoveTarget(0, +1, 0);
                        _modelManager?.UpdateTargetSpherePosition();
                        evtArgs_.Handled = true;
                        break;

                    case Key.Down:
                        _viewportInteraction.MoveTarget(0, -1, 0);
                        _modelManager?.UpdateTargetSpherePosition();
                        evtArgs_.Handled = true;
                        break;

                    case Key.OemPlus:
                    case Key.Add:
                        _viewportInteraction.MoveTarget(0, 0, +1);
                        evtArgs_.Handled = true;
                        break;

                    case Key.OemMinus:
                    case Key.Subtract:
                        _viewportInteraction.MoveTarget(0, 0, -1);
                        _modelManager?.UpdateTargetSpherePosition();
                        evtArgs_.Handled = true;
                        break;

                    case Key.PageUp:
                        _viewportInteraction.ChangeFov(-0.5);
                        evtArgs_.Handled = true;
                        break;

                    case Key.PageDown:
                        _viewportInteraction.ChangeFov(+0.5);
                        evtArgs_.Handled = true;
                        break;
                }
            }
        }

        private Int32 _shiftKeyUpCount;        // counts of Shift + K keyups

        private void Window_PreviewKeyUp(Object sender_, KeyEventArgs evt_) {
            Boolean shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            Boolean tDown = Keyboard.IsKeyDown(Key.T);
            Boolean bothDown = shiftDown || tDown;

            if (evt_.Key == Key.T || evt_.Key == Key.LeftShift || evt_.Key == Key.RightShift) {
                if (!bothDown) {
                    _targetSphereGrowing = false;
                    _shiftKeyUpCount++;
                    if (_shiftKeyUpCount == 1) {
                        _modelManager?.ShowTargetSphere();
                    } else if (_shiftKeyUpCount >= 2) {
                        _modelManager?.HideTargetSphere();
                        _shiftKeyUpCount = 0;
                    }
                    evt_.Handled = true;
                }
            }
        }

        /// <summary>
        /// Handles mouse down in viewport for placing or selecting flaws.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void Viewport3D_MouseDown(Object sender_, MouseButtonEventArgs evt_) {
            if (evt_.LeftButton != MouseButtonState.Pressed) return;
            Point mousePos = evt_.GetPosition(viewport3D.Viewport);
            _flawManager.DeselectFlaw();
            if (_uiStateManager.IsAddingFlaw) {   // in the adding flaw mode
                (Boolean modelHit, Point3D? hitPoint) = _viewportInteraction.Get3DPointInModel(mousePos, _modelManager);
                if ((!modelHit) || (hitPoint is null)) {
                    _uiStateManager.SetStatus($"Fail to create. Please try again");
                } else if (!_waitEndPoint) {
                    _flawManager.StartFlaw(hitPoint.Value);
                    _uiStateManager.SetStatus($"Flaw started. Click to end. {hitPoint.Value.X} {hitPoint.Value.Y}");
                    _waitEndPoint = true;
                } else {
                    Boolean flawCreated = _flawManager.EndFlaw(hitPoint.Value);
                    if (flawCreated) {
                        _uiStateManager.SetStatus($"Flaw created. Total: {_flawManager.Count}");
                        _uiStateManager.UpdateFlawCount(_flawManager.Count);
                        RefreshFlawList();
                    } else {
                        _uiStateManager.SetStatus("Invalid flaw. Try again.");
                    }
                    _waitEndPoint = false;
                }
            } else {
                (Boolean modelHit, _, FlawMarker? flaw) = _viewportInteraction.Get3DPointInFlaws(mousePos, _modelManager, _flawManager);
                if (!modelHit) {
                    _flawManager.SelectFlaw(null);
                    _uiStateManager.SetStatus($"No flaw selected.");
                    _uiStateManager.UpdateSelectedFlaw(null);
                    flawListBox.SelectedItem = null;                                                    // Clear listbox selection when clicking nothing in viewport
                } else {
                    _flawManager.SelectFlaw(flaw);
                    _uiStateManager.SetStatus($"Flaw selected.");
                    _uiStateManager.UpdateSelectedFlaw(flaw);
                    flawListBox.SelectedItem = flaw;                                                     // Update listbox selection
                }
            }
        }

        /// <summary>
        /// Updates in-progress flaw end point while moving the mouse.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void Viewport3D_MouseMove(Object sender_, MouseEventArgs evt_) {
            if (_uiStateManager.IsAddingFlaw && _waitEndPoint && _flawManager.CurrentFlaw != null) {
                Point mousePos = evt_.GetPosition(viewport3D.Viewport);
                (Boolean modelHit, Point3D? hitPoint) = _viewportInteraction.Get3DPointInModel(mousePos, _modelManager);
                if (modelHit && hitPoint is not null) {
                    _flawManager.MoveFlaw(hitPoint.Value);
                }
            }
        }

        /// <summary>
        /// Starts sphere highlight when pressing mouse on color button.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void FlawColorButton_PreviewMouseLeftButtonDown(Object sender_, MouseButtonEventArgs evt_) {
            if (sender_ is Button btn && btn.Tag is FlawMarker flaw) {
                _flawManager.StartHighlightSphere(flaw);
                evt_.Handled = true;
            }
        }

        /// <summary>
        /// Stops sphere highlight when releasing mouse on color button.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void FlawColorButton_PreviewMouseLeftButtonUp(Object sender_, MouseButtonEventArgs evt_) {
            if (sender_ is Button btn && btn.Tag is FlawMarker flaw) {
                _flawManager.StopHighlightSphere(flaw);
                evt_.Handled = true;
            }
        }
        /// <summary>
        /// Stops sphere highlight when mouse capture is lost.
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void FlawColorButton_LostMouseCapture(Object sender_, MouseEventArgs evt_) {
            if (sender_ is Button btn && btn.Tag is FlawMarker flaw) {
                _flawManager.StopHighlightSphere(flaw);
            }
        }

        /// <summary>
        /// Mouse up handler for viewport (reserved for future use).
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Mouse event args.</param>
        private void Viewport3D_MouseUp(Object sender_, MouseButtonEventArgs evt_) {

        }

        /// <summary>
        /// Camera changed handler (reserved for future use).
        /// </summary>
        /// <param name="sender_">Event sender.</param>
        /// <param name="evt_">Routed event args.</param>
        private void Viewport_CameraChanged(Object sender_, RoutedEventArgs evt_) {
            
        }

        // Render-loop callback for smooth target sphere growth
        private void OnRendering(Object? sender_, EventArgs evt_) {
            if (!_targetSphereGrowing || _modelManager == null)
                return;
            DateTime now = DateTime.UtcNow;
            if (_lastRenderTime == DateTime.MinValue) {
                _lastRenderTime = now;
                return;
            }
            Double deltaSeconds = (now - _lastRenderTime).TotalSeconds;
            _lastRenderTime = now;
            _modelManager.UpdateTargetSphereGrowth(deltaSeconds);
        }

        // Update marker sizes after mouse wheel zoom. Use Dispatcher to ensure the zoom has been applied first
        private void Viewport3D_MouseWheel(Object sender_, MouseWheelEventArgs evt_) => Dispatcher.BeginInvoke(new Action(UpdateSelectedLineMarkerSizes), System.Windows.Threading.DispatcherPriority.Render);

        private void UpdateSelectedLineMarkerSizes() {
            if (_flawManager != null && _flawManager.HasSelection) {
                _flawManager.UpdateSelectedFlawMarkerSizes();
            }
        }
    }
}