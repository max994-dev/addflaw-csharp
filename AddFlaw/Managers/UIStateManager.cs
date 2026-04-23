using System.Windows.Controls;
using System.Windows.Media;
using AddFlaw.Models;
using Color = System.Windows.Media.Color;

namespace AddFlaw.Managers {
    /// <summary>
    /// Manages UI state for flaw creation and selection, updating buttons, labels, and text boxes
    /// according to the current application state.
    /// </summary>
    public class UIStateManager {
        private readonly Button _addFlawButton;                                     // Add/Stop flaw creation button
        private readonly Button _applyFlawLabelButton;                              // Apply label button for selected flaw
        private readonly TextBlock _statusLabel;                                    // Status text label
        private readonly TextBlock _flawCountLabel;                                 // Total flaw count label
        private readonly TextBlock _selectedFlawIdLabel;                            // Selected flaw id label
        private readonly TextBox _flawLabelTextBox;                                 // Text box for editing selected flaw label
        public Boolean IsAddingFlaw { get; private set; }                           // Whether the UI is in "add flaw" mode
        
        /// <summary>
        /// Constructs a new UI state manager with references to key UI controls.
        /// </summary>
        /// <param name="addFlawButton_">Button for toggling flaw creation.</param>
        /// <param name="applyFlawLabelButton_">Button to apply label to selected flaw.</param>
        /// <param name="statusLabel_">Label displaying status messages.</param>
        /// <param name="flawCountLabel_">Label showing total flaw count.</param>
        /// <param name="selectedFlawIdLabel_">Label showing the selected flaw id.</param>
        /// <param name="flawLabelTextBox_">Text box to edit selected flaw label.</param>
        public UIStateManager(Button addFlawButton_, Button applyFlawLabelButton_, TextBlock statusLabel_, TextBlock flawCountLabel_, TextBlock selectedFlawIdLabel_, TextBox flawLabelTextBox_) {
            _addFlawButton = addFlawButton_;
            _statusLabel = statusLabel_;
            _flawCountLabel = flawCountLabel_;
            _selectedFlawIdLabel = selectedFlawIdLabel_;
            _applyFlawLabelButton = applyFlawLabelButton_;
            _flawLabelTextBox = flawLabelTextBox_;
        }

        /// <summary>
        /// Toggles the UI mode between adding a new flaw and idle, updating visuals accordingly.
        /// </summary>
        public void ToggleFlawAddingMode() {
            IsAddingFlaw = !IsAddingFlaw;
            if (IsAddingFlaw) {
                _addFlawButton.Background = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                _addFlawButton.Content = "✓ Stop Adding";
                SetStatus("Click to start flaw, click to end.");
            } else {
                _addFlawButton.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                _addFlawButton.Content = "➕ Add Flaw";
                SetStatus("Flaw creation finished");
            }
        }

        /// <summary>
        /// Sets the status message.
        /// </summary>
        /// <param name="message_">Message to display.</param>
        public void SetStatus(String message_) => _statusLabel.Text = message_;

        /// <summary>
        /// Updates the displayed flaw count.
        /// </summary>
        /// <param name="count_">Total number of flaws.</param>
        public void UpdateFlawCount(Int32 count_) => _flawCountLabel.Text = count_.ToString();

        /// <summary>
        /// Updates UI controls to reflect the currently selected flaw.
        /// </summary>
        /// <param name="flaw_">Selected flaw marker or null.</param>
        public void UpdateSelectedFlaw(FlawMarker? flaw_) {
            if (flaw_ != null) {
                _selectedFlawIdLabel.Text = flaw_.Id.ToString();
                _flawLabelTextBox.Text = flaw_.Label ?? String.Empty;
                _applyFlawLabelButton.IsEnabled = true;
            } else {
                _selectedFlawIdLabel.Text = "None";
                _flawLabelTextBox.Text = String.Empty;
                _applyFlawLabelButton.IsEnabled = false;
            }
        }
    }
}