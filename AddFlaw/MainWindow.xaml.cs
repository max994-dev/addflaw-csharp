using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AddFlaw.Managers;
using AddFlaw.Models;
using HelixToolkit.Wpf;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Path = System.IO.Path;
using Point = System.Windows.Point;

namespace AddFlaw
{
    public partial class MainWindow : Window
    {
        private ModelManager modelManager;
        private LineManager lineManager;
        private ViewportInteraction viewportInteraction;
        private UIStateManager uiStateManager;
        private Color[] ModelColors = { Colors.Blue, Colors.Gray };
        private Color[] LineColors = { Colors.Red, Colors.Green };
        bool waitEndPoint = false;
        private byte emissionAlpha = 60;
        private double moveSpeed = 0.05;

        // Target sphere render-loop state
        private bool targetSphereGrowing = false;
        private DateTime lastRenderTime = DateTime.MinValue;


        public MainWindow()
        {
            InitializeComponent();
            InitializeManagers();
            InitializeViewportEvents();

            CompositionTarget.Rendering += OnRendering;
        }

        private void InitializeManagers()
        {
            modelManager = new ModelManager(viewport3D, modelRoot);
            lineManager = new LineManager(viewport3D);
            viewportInteraction = new ViewportInteraction(viewport3D);
            uiStateManager = new UIStateManager(addLineButton, applyLineLabelButton, statusLabel, linesCountLabel, selectedLineIdLabel, lineLabelTextBox);

            emissionAlpha = (byte)EmissionAlphaSlider.Value;
            EmissionAlphaLabel.Text = emissionAlpha.ToString();
        }

        private void InitializeViewportEvents()
        {
            viewport3D.MouseDown += Viewport3D_MouseDown;
            viewport3D.MouseMove += Viewport3D_MouseMove;
            viewport3D.MouseUp += Viewport3D_MouseUp;
            viewport3D.LostFocus += Viewport3D_LostFocus;
            viewport3D.MouseWheel += Viewport3D_MouseWheel;
        }

        private void Viewport3D_LostFocus(object sender, RoutedEventArgs e)
        {
            waitEndPoint = false;
            lineManager.CancelLine();
        }

        private void ViewUpButton_Click(object sender, RoutedEventArgs e)
        {
            viewportInteraction.PanCamera(0, +moveSpeed);
        }

        private void ViewDownButton_Click(object sender, RoutedEventArgs e)
        {
            viewportInteraction.PanCamera(0, -moveSpeed);
        }

        private void ViewLeftButton_Click(object sender, RoutedEventArgs e)
        {
            viewportInteraction.PanCamera(+moveSpeed, 0);
        }

        private void ViewRightButton_Click(object sender, RoutedEventArgs e)
        {
            viewportInteraction.PanCamera(-moveSpeed, 0);
        }

        private void ViewCenterButton_Click(object sender, RoutedEventArgs e)
        {
            viewport3D.ZoomExtents();
            UpdateSelectedLineMarkerSizes();
        }

        private void ViewZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            viewportInteraction.ChangeFov(-0.5); // smaller FOV → zoom in
            UpdateSelectedLineMarkerSizes();
        }

        private void ViewZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            viewportInteraction.ChangeFov(+0.5); // larger FOV → zoom out
            UpdateSelectedLineMarkerSizes();
        }

        private void RefreshLineList()
        {
            if (lineListBox == null || lineManager == null) return;

            lineListBox.ItemsSource = null;
            lineListBox.ItemsSource = lineManager.Lines;
        }

        private void LineListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lineListBox.SelectedItem is not LineMarker marker)
                return;

            viewportInteraction.FocusCameraOnLineAnimated(marker);
        }

        private void LineListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            uiStateManager.SetStatus("Line placement canceled.");

            if (lineListBox.SelectedItem is LineMarker line)
            {
                lineManager.SelectLine(line);
                uiStateManager.UpdateSelectedLine(line);
            }
            else
            {
                lineManager.SelectLine(null);
                uiStateManager.UpdateSelectedLine(null);
            }
        }

        private bool IsWithin(DependencyObject? child, DependencyObject target)
        {
            while (child != null)
            {
                if (child == target)
                    return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        private void LineListBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;

            // Keep selection if focus moved to textbox or apply button
            if (focused != null &&
                (IsWithin(focused, lineLabelTextBox) || IsWithin(focused, applyLineLabelButton)))
                return;

            // Otherwise, clear selection when the listbox loses focus
            lineListBox.SelectedItem = null;
        }

        private void LineLabelTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;

            // Keep selection if focus moved to listbox or apply button
            if (focused != null &&
                (IsWithin(focused, lineListBox) || IsWithin(focused, applyLineLabelButton)))
                return;

            lineListBox.SelectedItem = null;
        }

        private void ApplyLineLabelButton_LostFocus(object sender, RoutedEventArgs e)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;

            // Keep selection if focus moved to listbox or textbox
            if (focused != null &&
                (IsWithin(focused, lineListBox) || IsWithin(focused, lineLabelTextBox)))
                return;

            lineListBox.SelectedItem = null;
        }

        private void ApplyLineLabelButton_Click(object sender, RoutedEventArgs e)
        {
            if (!lineManager.HasSelection)
                return;

            lineManager.UpdateSelectedLabel(lineLabelTextBox.Text);
            RefreshLineList();
            uiStateManager.SetStatus($"Successfully Updated!");
        }

        private void EmissionAlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (modelManager == null) return; // Guard clause

            emissionAlpha = (byte)e.NewValue;
            if (EmissionAlphaLabel != null)
                EmissionAlphaLabel.Text = emissionAlpha.ToString();

            modelManager.SetEmissionAlpha(emissionAlpha);
        }

        private void LoadModelButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "3D Model Files (*.obj;*.stl)|*.obj;*.stl|OBJ Files (*.obj)|*.obj|STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
                Title = "Open 3D Model"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    modelManager.LoadModel(dialog.FileName);
                    modelManager.SetModelColor(ModelColors[0]);
                    filePathLabel.Text = Path.GetFileName(dialog.FileName);
                    uiStateManager.SetStatus("Model loaded successfully");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    filePathLabel.Text = "Error loading file";
                }
            }
        }

        private void SaveLinesButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Save Lines"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    lineManager.SaveLines(dlg.FileName);
                    uiStateManager.SetStatus("Lines saved");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving lines: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadLinesButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Load Lines"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    lineManager.LoadLines(dlg.FileName);
                    RefreshLineList();
                    uiStateManager.SetStatus("Lines loaded");
                    uiStateManager.UpdateLineCount(lineManager.Count);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading lines: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddLineButton_Click(object sender, RoutedEventArgs e)
        {
            uiStateManager.ToggleLineAddingMode();
        }

        private void ModelColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (modelManager == null) return; // Guard clause

            Color color = ModelColors[modelColorCombo.SelectedIndex];
            modelManager.SetModelColor(color);
        }

        private void LineColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lineManager == null) return; // Guard clause

            Color color = LineColors[lineColorCombo.SelectedIndex];
            lineManager.SetLineColor(color);
        }

        private void ShowAxisCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            modelManager?.ShowAxisLines(true);
        }

        private void ShowAxisCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            modelManager?.ShowAxisLines(false);
        }


        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = false;
            // Handle Delete: remove selected line
            if (e.Key == Key.Delete)
            {
                if (lineManager != null && lineManager.HasSelection)
                {
                    lineManager.DeleteSelectedLine();
                    uiStateManager.UpdateLineCount(lineManager.Count);
                    RefreshLineList();
                    uiStateManager.UpdateSelectedLine(null);
                }

                e.Handled = true;
                return;
            }

            // Handle Esc: cancel line placement states
            if (e.Key == Key.Escape)
            {
                if(waitEndPoint)
                {
                    lineManager.CurrentLine.UpdateEndPoint(lineManager.CurrentLine.StartPoint, false);
                    
                    waitEndPoint = false;
                    uiStateManager.SetStatus("Second point canceled. Press Esc again to exit line placement.");
                } else if(lineManager.CurrentLine != null)
                {
                    waitEndPoint = false;
                    lineManager.CancelLine();
                    uiStateManager.SetStatus("Line placement canceled.");
                }
                e.Handled = true;
                return;
            }

            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if(ctrl)
            {
                switch (e.Key)
                {
                    case Key.Left:
                        viewportInteraction.MovePosition(-1, 0, 0);
                        e.Handled = true;
                        break;

                    case Key.Right:
                        viewportInteraction.MovePosition(+1, 0, 0);
                        e.Handled = true;

                        break;

                    case Key.Up:
                        viewportInteraction.MovePosition(0, +1, 0);
                        e.Handled = true;
                        break;

                    case Key.Down:
                        viewportInteraction.MovePosition(0, -1, 0);
                        e.Handled = true;
                        break;

                    case Key.OemPlus:
                    case Key.Add:
                        viewportInteraction.MovePosition(0, 0, +1);
                        e.Handled = true;
                        break;

                    case Key.OemMinus:
                    case Key.Subtract:
                        viewportInteraction.MovePosition(0, 0, -1);
                        e.Handled = true;
                        break;

                    case Key.PageUp:
                        viewportInteraction.ChangeFov(-0.5);
                        e.Handled = true;
                        break;

                    case Key.PageDown:
                        viewportInteraction.ChangeFov(+0.5);
                        e.Handled = true;
                        break;
                }
            }
            if (shift && Keyboard.IsKeyDown(Key.T) && !e.IsRepeat)
            {
                modelManager?.StartTargetSphereGrowing();
                targetSphereGrowing = true;
                lastRenderTime = DateTime.MinValue;
                e.Handled = true;
                return;
            }

            if (shift)
            {
                switch (e.Key)
                {
                    case Key.Left:
                        viewportInteraction.MoveTarget(-1, 0, 0);
                        modelManager?.UpdateTargetSpherePosition();
                        e.Handled = true;
                        break;

                    case Key.Right:
                        viewportInteraction.MoveTarget(+1, 0, 0);
                        modelManager?.UpdateTargetSpherePosition();
                        e.Handled = true;
                        break;

                    case Key.Up:
                        viewportInteraction.MoveTarget(0, +1, 0);
                        modelManager?.UpdateTargetSpherePosition();
                        e.Handled = true;
                        break;

                    case Key.Down:
                        viewportInteraction.MoveTarget(0, -1, 0);
                        modelManager?.UpdateTargetSpherePosition();
                        e.Handled = true;
                        break;

                    case Key.OemPlus:
                    case Key.Add:
                        viewportInteraction.MoveTarget(0, 0, +1);
                        modelManager?.UpdateTargetSpherePosition();
                        e.Handled = true;
                        break;

                    case Key.OemMinus:
                    case Key.Subtract:
                        viewportInteraction.MoveTarget(0, 0, -1);
                        modelManager?.UpdateTargetSpherePosition();
                        e.Handled = true;
                        break;

                    case Key.PageUp:
                        viewportInteraction.ChangeFov(-0.5);
                        UpdateSelectedLineMarkerSizes();
                        e.Handled = true;
                        break;

                    case Key.PageDown:
                        viewportInteraction.ChangeFov(+0.5);
                        UpdateSelectedLineMarkerSizes();
                        e.Handled = true;
                        break;
                }
            }

        }

        
        private int shiftKeyUpCount = 0;        // counts of Shift + K keyups

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            bool shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool tDown = Keyboard.IsKeyDown(Key.T);
            bool bothDown = shiftDown || tDown;

            if (e.Key == Key.T || e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                if (!bothDown)
                {
                    targetSphereGrowing = false;
                    shiftKeyUpCount++;
                    if (shiftKeyUpCount == 1)
                    {
                        modelManager?.ShowTargetSphere();
                    }
                    else if (shiftKeyUpCount >= 2)
                    {
                        modelManager?.HideTargetSphere();
                        shiftKeyUpCount = 0;
                    }
                    e.Handled = true;
                }
            }
        }


        private void Viewport3D_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point mousePos = e.GetPosition(viewport3D.Viewport);

            lineManager.DeselectLine();

            if (uiStateManager.IsAddingLine)    // in the adding line mode
            {
                (bool modelHit, Point3D? hitPoint) = viewportInteraction.Get3DPointInModel(mousePos, modelManager);

                if (!modelHit) 
                {

                    uiStateManager.SetStatus($"Fail to create. Please try again");
                } 
                else if (!waitEndPoint)
                {
                    lineManager.StartLine(hitPoint.Value);
                    uiStateManager.SetStatus($"Line started. Click to end. {hitPoint.Value.X} {hitPoint.Value.Y}");
                    waitEndPoint = true;
                } 
                else
                {
                    bool lineCreated = lineManager.EndLine(hitPoint.Value);
                    if (lineCreated)
                    {
                        uiStateManager.SetStatus($"Line created. Total: {lineManager.Count}");
                        uiStateManager.UpdateLineCount(lineManager.Count);
                        RefreshLineList();
                    }
                    else
                    {
                        uiStateManager.SetStatus("Invalid line. Try again.");
                    }
                    waitEndPoint = false;
                }
                 
            }
            else 
            {
                (bool modelHit, Point3D? hitPoint, LineMarker? line) = viewportInteraction.Get3DPointInLines(mousePos, modelManager, lineManager);
                if (!modelHit)
                {
                    lineManager.SelectLine(null);
                    uiStateManager.SetStatus($"No line selected.");
                    uiStateManager.UpdateSelectedLine(null);
                    // Clear listbox selection when clicking nothing in viewport
                    lineListBox.SelectedItem = null;
                }
                else
                {
                    int id = line.Id;
                    lineManager.SelectLine(line);
                    uiStateManager.SetStatus($"Line selected.");
                    uiStateManager.UpdateSelectedLine(line);
                    // Sync listbox selection when clicking line in viewport
                    lineListBox.SelectedItem = line;
                }
            }
        }

        private void Viewport3D_MouseMove(object sender, MouseEventArgs e)
        {
            if(uiStateManager.IsAddingLine && waitEndPoint && lineManager.CurrentLine != null)
            {
                Point mousePos = e.GetPosition(viewport3D.Viewport);
                (bool modelHit, Point3D? hitPoint) = viewportInteraction.Get3DPointInModel(mousePos, modelManager);
                if (modelHit)
                {
                    lineManager.MoveLine(hitPoint.Value);
                }
            }
        }

        private void LineColorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LineMarker line)
            {
                lineManager.StartHighlightSphere(line);
                e.Handled = true;
            }
        }

        private void LineColorButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LineMarker line)
            {
                lineManager.StopHighlightSphere(line);
                e.Handled = true;
            }
        }
        private void LineColorButton_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LineMarker line)
            {
                lineManager.StopHighlightSphere(line);
            }
        }

        private void Viewport3D_MouseUp(object sender, MouseButtonEventArgs e)
        {
        
        }

        private void Viewport_CameraChanged(object sender, RoutedEventArgs e)
        {
            
        }

        // Render-loop callback for smooth target sphere growth
        private void OnRendering(object? sender, EventArgs e)
        {
            if (!targetSphereGrowing || modelManager == null)
                return;

            var now = DateTime.UtcNow;
            if (lastRenderTime == DateTime.MinValue)
            {
                lastRenderTime = now;
                return;
            }

            double deltaSeconds = (now - lastRenderTime).TotalSeconds;
            lastRenderTime = now;

            modelManager.UpdateTargetSphereGrowth(deltaSeconds);
        }

        private void Viewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Update marker sizes after mouse wheel zoom
            // Use Dispatcher to ensure the zoom has been applied first
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateSelectedLineMarkerSizes();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void UpdateSelectedLineMarkerSizes()
        {
            if (lineManager != null && lineManager.HasSelection)
            {
                lineManager.UpdateSelectedLineMarkerSizes();
            }
        }
    }
}