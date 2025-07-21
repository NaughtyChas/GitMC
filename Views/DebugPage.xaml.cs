using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitMC.Views
{
    public sealed partial class DebugPage : Page
    {
        public DebugPage()
        {
            this.InitializeComponent();
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic for selecting a file will be added later
            FileInfoTextBox.Text = "Select File button clicked.";
        }

        private void ConvertToSnbtButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic for converting to SNBT will be added later
            FileInfoTextBox.Text = "Convert to SNBT button clicked.";
        }

        private void ConvertToNbtButton_Click(object sender, RoutedEventArgs e)
        {
            // Logic for converting back to NBT will be added later
            FileInfoTextBox.Text = "Convert back to NBT button clicked.";
        }
    }
}
