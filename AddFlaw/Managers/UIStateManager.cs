using System.Windows.Controls;
using System.Windows.Media;
using AddFlaw.Models;

namespace AddFlaw.Managers
{
    public class UIStateManager
    {
        private bool isAddingLine = false;
        
        private Button addLineButton;
        
        private Button applyLineLabelButton;
        
        private TextBlock statusLabel;
        
        private TextBlock lineCountLabel;
        
        private TextBlock selectedLineIdLabel;
        
        private TextBox lineLabelTextBox;
        public bool IsAddingLine => isAddingLine;

        public UIStateManager(Button addLineButton, Button applyLineLabelButton, TextBlock statusLabel, TextBlock lineCountLabel, TextBlock selectedLineIdLabel, TextBox lineLabelTextBox)
        {
            this.addLineButton = addLineButton;
            this.statusLabel = statusLabel;
            this.lineCountLabel = lineCountLabel;
            this.selectedLineIdLabel = selectedLineIdLabel;
            this.applyLineLabelButton = applyLineLabelButton;
            this.lineLabelTextBox = lineLabelTextBox;
        }

        public void ToggleLineAddingMode()
        {
            isAddingLine = !isAddingLine;

            if (isAddingLine)
            {
                addLineButton.Background = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                addLineButton.Content = "✓ Stop Adding";
                SetStatus("Click to start line, click to end.");
            }
            else
            {
                addLineButton.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                addLineButton.Content = "➕ Add Line";
                SetStatus("Line creation finished");
            }
        }

        public void SetStatus(string message)
        {
            statusLabel.Text = message;
        }

        public void UpdateLineCount(int count)
        {
            lineCountLabel.Text = count.ToString();
        }

        public void UpdateSelectedLine(LineMarker? line)
        {
            if (line != null)
            {
                selectedLineIdLabel.Text = line.Id.ToString();
                lineLabelTextBox.Text = line.Label ?? string.Empty;
                applyLineLabelButton.IsEnabled = true;
            }
            else
            {
                selectedLineIdLabel.Text = "None";
                lineLabelTextBox.Text = string.Empty;
                applyLineLabelButton.IsEnabled = false;
            }
        }
    }
}