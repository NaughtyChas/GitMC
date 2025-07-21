using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using Windows.Storage.Pickers;
using Windows.Storage;
using GitMC.Services;
using System.Threading.Tasks;

namespace GitMC.Views
{
    public sealed partial class DebugPage : Page
    {
        private readonly INbtService _nbtService;
        private string? _selectedFilePath;
        private string? _currentSnbtContent;

        public DebugPage()
        {
            this.InitializeComponent();
            _nbtService = new NbtService();
        }

        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".nbt");
                picker.FileTypeFilter.Add(".dat");
                picker.FileTypeFilter.Add(".mcstructure");
                picker.FileTypeFilter.Add("*");
                
                // Get current window handle for the picker
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    _selectedFilePath = file.Path;
                    SelectedFileTextBlock.Text = Path.GetFileName(_selectedFilePath);

                    // Enable operation buttons
                    ConvertToSnbtButton.IsEnabled = true;
                    ValidateFileButton.IsEnabled = true;

                    // Show file information
                    OutputTextBox.Text = "Analyzing file...";
                    var fileInfo = await _nbtService.GetNbtFileInfoAsync(_selectedFilePath);
                    OutputTextBox.Text = fileInfo;
                }
            }
            catch (Exception ex)
            {
                OutputTextBox.Text = $"Error when selecting file: {ex.Message}";
            }
        }

        private async void ConvertToSnbtButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                OutputTextBox.Text = "Please select a file first.";
                return;
            }

            try
            {
                OutputTextBox.Text = "Translating into SNBT...";
                _currentSnbtContent = await _nbtService.ConvertNbtToSnbtAsync(_selectedFilePath);

                OutputTextBox.Text = $"Success! \n\nFile: {Path.GetFileName(_selectedFilePath)}\n\nSNBT Content:\n{new string('=', 50)}\n{_currentSnbtContent}";

                // Enable Convert to NBT button
                ConvertToNbtButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                OutputTextBox.Text = $"Error when converting to SNBT: {ex.Message}";
                _currentSnbtContent = null;
                ConvertToNbtButton.IsEnabled = false;
            }
        }

        private async void ConvertToNbtButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSnbtContent))
            {
                OutputTextBox.Text = "Please convert the file to SNBT format first.";
                return;
            }

            try
            {
                var picker = new FileSavePicker();
                picker.FileTypeChoices.Add("NBT File", new[] { ".nbt" });
                picker.FileTypeChoices.Add("DAT File", new[] { ".dat" });
                picker.SuggestedFileName = "converted";

                // Get current window handle for the picker
                var window = App.MainWindow;
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    OutputTextBox.Text = "Translating to NBT...";
                    
                    await _nbtService.ConvertSnbtToNbtAsync(_currentSnbtContent, file.Path);

                    OutputTextBox.Text = $"Success!\n\nFile saved to: {file.Path}\n\nOriginal SNBT Content:\n{new string('=', 50)}\n{_currentSnbtContent}";
                }
            }
            catch (Exception ex)
            {
                OutputTextBox.Text = $"Error when converting to NBT: {ex.Message}";
            }
        }

        private async void ValidateFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath))
            {
                OutputTextBox.Text = "Please select a file first.";
                return;
            }

            try
            {
                OutputTextBox.Text = "Validating file...";
                
                var isValid = await _nbtService.IsValidNbtFileAsync(_selectedFilePath);
                var fileInfo = await _nbtService.GetNbtFileInfoAsync(_selectedFilePath);
                
                OutputTextBox.Text = $"Validation Result: {(isValid ? "✅ Valid NBT File" : "❌ Invalid NBT File")}\n\n{fileInfo}";
            }
            catch (Exception ex)
            {
                OutputTextBox.Text = $"Error when validating file: {ex.Message}";
            }
        }

        private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
        {
            OutputTextBox.Text = "Emptied output...";
            _currentSnbtContent = null;
            ConvertToNbtButton.IsEnabled = false;
        }
    }
}
