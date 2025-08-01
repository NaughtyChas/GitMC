using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using GitMC.Services;
using GitMC.Tests;
using GitMC.Utils;
using WinRT.Interop;

namespace GitMC.Views;

public sealed partial class DebugPage : Page
{
    private readonly INbtService _nbtService;
    private string? _currentSnbtContent;
    private bool _isAnvilFile;
    private string? _selectedFilePath;

    public DebugPage()
    {
        InitializeComponent();
        _nbtService = new NbtService();
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".nbt");
            picker.FileTypeFilter.Add(".dat");
            picker.FileTypeFilter.Add(".mca");
            picker.FileTypeFilter.Add(".mcc");
            picker.FileTypeFilter.Add(".mcstructure");
            picker.FileTypeFilter.Add("*");

            // Get current window handle for the picker
            Window? window = App.MainWindow;
            IntPtr hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hWnd);

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _selectedFilePath = file.Path;
                SelectedFileTextBlock.Text = Path.GetFileName(_selectedFilePath);

                // Check if it's an Anvil file
                string extension = Path.GetExtension(_selectedFilePath).ToLowerInvariant();
                _isAnvilFile = await CommonHelpers.IsAnvilRelatedFileAsync(_selectedFilePath, extension);

                // Show appropriate UI panels
                if (_isAnvilFile)
                {
                    AnvilActionsPanel.Visibility = Visibility.Visible;
                    ChunkInputPanel.Visibility = Visibility.Visible;

                    // Enable Anvil buttons
                    ShowRegionInfoButton.IsEnabled = true;
                    ListChunksButton.IsEnabled = true;
                    ExtractChunkButton.IsEnabled = true;
                    ConvertMcaToSnbtButton.IsEnabled = true;
                    ConvertSnbtToMcaButton.IsEnabled = true;

                    // Disable NBT buttons for .mca files (but not .mcc)
                    bool enableNbtActions = extension == ".mcc";
                    ConvertToSnbtButton.IsEnabled = enableNbtActions;
                    ValidateFileButton.IsEnabled = enableNbtActions;
                }
                else
                {
                    AnvilActionsPanel.Visibility = Visibility.Collapsed;
                    ChunkInputPanel.Visibility = Visibility.Collapsed;

                    // Enable NBT buttons
                    ConvertToSnbtButton.IsEnabled = true;
                    ValidateFileButton.IsEnabled = true;

                    // Disable Anvil buttons
                    ShowRegionInfoButton.IsEnabled = false;
                    ListChunksButton.IsEnabled = false;
                    ExtractChunkButton.IsEnabled = false;
                }

                // Show file information
                OutputTextBox.Text = "Analyzing file...";
                await ShowFileInfo();
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"Error when selecting file: {ex.Message}";
        }
    }

    private async Task ShowFileInfo()
    {
        try
        {
            if (_isAnvilFile)
            {
                bool isValid = await _nbtService.IsValidAnvilFileAsync(_selectedFilePath!);
                var fileInfo = new FileInfo(_selectedFilePath!);

                if (Path.GetExtension(_selectedFilePath)?.ToLowerInvariant() == ".mca")
                {
                    AnvilRegionInfo regionInfo = await _nbtService.GetRegionInfoAsync(_selectedFilePath!);
                    FileInfoTextBlock.Text =
                        $"Anvil Region File | Size: {fileInfo.Length / 1024.0:F1} KB | Valid: {(isValid ? "Yes" : "No")}";
                    OutputTextBox.Text = $"📁 Anvil Region File Analysis\n" +
                                         $"File: {Path.GetFileName(_selectedFilePath)}\n" +
                                         $"Region Coordinates: ({regionInfo.RegionX}, {regionInfo.RegionZ})\n" +
                                         $"File Size: {regionInfo.FileSize / 1024.0:F1} KB\n" +
                                         $"Total Chunks: {regionInfo.TotalChunks}\n" +
                                         $"Valid Chunks: {regionInfo.ValidChunks}\n" +
                                         $"Last Modified: {regionInfo.LastModified}\n" +
                                         $"Is Valid: {(isValid ? "✅ Yes" : "❌ No")}\n\n" +
                                         $"Use the Anvil Actions above to explore the region file.";
                }
                else
                {
                    // .mcc file
                    FileInfoTextBlock.Text =
                        $"Oversized Chunk File | Size: {fileInfo.Length / 1024.0:F1} KB | Valid: {(isValid ? "Yes" : "No")}";
                    string nbtInfo = await _nbtService.GetNbtFileInfoAsync(_selectedFilePath!);
                    OutputTextBox.Text = $"📦 Oversized Chunk File Analysis\n{nbtInfo}";
                }
            }
            else
            {
                string fileInfo = await _nbtService.GetNbtFileInfoAsync(_selectedFilePath!);
                var file = new FileInfo(_selectedFilePath!);
                FileInfoTextBlock.Text = $"NBT/DAT File | Size: {file.Length / 1024.0:F1} KB";
                OutputTextBox.Text = fileInfo;
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"Error analyzing file: {ex.Message}";
            FileInfoTextBlock.Text = "Analysis failed";
        }
    }

    private async void ShowRegionInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            OutputTextBox.Text = "Please select an Anvil file first.";
            return;
        }

        try
        {
            OutputTextBox.Text = "Loading region information...";
            AnvilRegionInfo regionInfo = await _nbtService.GetRegionInfoAsync(_selectedFilePath);

            OutputTextBox.Text = $"🗺️ Region Information\n" +
                                 $"{new string('=', 50)}\n" +
                                 $"File: {Path.GetFileName(regionInfo.FilePath)}\n" +
                                 $"Region Coordinates: ({regionInfo.RegionX}, {regionInfo.RegionZ})\n" +
                                 $"World Chunk Range: X[{regionInfo.RegionX * 32}...{regionInfo.RegionX * 32 + 31}], Z[{regionInfo.RegionZ * 32}...{regionInfo.RegionZ * 32 + 31}]\n" +
                                 $"File Size: {regionInfo.FileSize:N0} bytes ({regionInfo.FileSize / 1024.0:F1} KB)\n" +
                                 $"Total Chunk Slots: {regionInfo.TotalChunks}\n" +
                                 $"Valid Chunks: {regionInfo.ValidChunks}\n" +
                                 $"Empty Slots: {regionInfo.TotalChunks - regionInfo.ValidChunks}\n" +
                                 $"Fill Rate: {regionInfo.ValidChunks / (double)regionInfo.TotalChunks * 100:F1}%\n" +
                                 $"Last Modified: {regionInfo.LastModified}";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"Error loading region info: {ex.Message}";
        }
    }

    private async void ListChunksButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            OutputTextBox.Text = "Please select an Anvil file first.";
            return;
        }

        try
        {
            OutputTextBox.Text = "Listing chunks...";
            List<AnvilChunkInfo> chunks = await _nbtService.ListChunksInRegionAsync(_selectedFilePath);
            var validChunks = chunks.Where(c => c.IsValid).ToList();

            string output = $"📋 Chunk List ({validChunks.Count} valid chunks)\n" +
                            $"{new string('=', 80)}\n" +
                            $"{"Chunk Coords",-15} {"Local",-8} {"Offset",-8} {"Sectors",-8} {"Size",-10} {"Compression",-12} {"Last Modified",-20}\n" +
                            $"{new string('-', 80)}\n";

            foreach (AnvilChunkInfo chunk in validChunks.Take(50)) // Limit to first 50 chunks
            {
                string compressionStr = chunk.IsOversized ? "Oversized" : chunk.CompressionType.ToString();
                string sizeStr = chunk.IsOversized ? "External" : $"{chunk.DataSize} B";

                output += $"({chunk.ChunkX},{chunk.ChunkZ})"
                              .PadRight(15) +
                          $"({chunk.LocalX},{chunk.LocalZ})"
                              .PadRight(8) +
                          $"{chunk.SectorOffset}"
                              .PadRight(8) +
                          $"{chunk.SectorCount}"
                              .PadRight(8) +
                          $"{sizeStr}"
                              .PadRight(10) +
                          $"{compressionStr}"
                              .PadRight(12) +
                          $"{(chunk.LastModified == DateTime.MinValue ? "N/A" : chunk.LastModified.ToString("yyyy-MM-dd HH:mm"))}"
                              .PadRight(20) + "\n";
            }

            if (validChunks.Count > 50) output += $"\n... and {validChunks.Count - 50} more chunks (showing first 50)";

            OutputTextBox.Text = output;
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"Error listing chunks: {ex.Message}";
        }
    }

    private async void ExtractChunkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            OutputTextBox.Text = "Please select an Anvil file first.";
            return;
        }

        try
        {
            int chunkX = (int)ChunkXNumberBox.Value;
            int chunkZ = (int)ChunkZNumberBox.Value;

            OutputTextBox.Text = $"Extracting chunk ({chunkX}, {chunkZ})...";
            string chunkData = await _nbtService.ExtractChunkDataAsync(_selectedFilePath, chunkX, chunkZ);

            OutputTextBox.Text = $"🎯 Chunk Data ({chunkX}, {chunkZ})\n" +
                                 $"{new string('=', 50)}\n" +
                                 $"File: {Path.GetFileName(_selectedFilePath)}\n" +
                                 $"Chunk Coordinates: ({chunkX}, {chunkZ})\n\n" +
                                 $"SNBT Content:\n" +
                                 $"{new string('-', 50)}\n" +
                                 $"{chunkData}";

            // Enable conversion options for extracted chunk data
            _currentSnbtContent = chunkData;
            ConvertToNbtButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            OutputTextBox.Text =
                $"Error extracting chunk ({ChunkXNumberBox.Value}, {ChunkZNumberBox.Value}): {ex.Message}";
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

            OutputTextBox.Text =
                $"✅ Success!\n\nFile: {Path.GetFileName(_selectedFilePath)}\n\nSNBT Content:\n{new string('=', 50)}\n{_currentSnbtContent}";

            // Enable Convert to NBT button
            ConvertToNbtButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"❌ Error when converting to SNBT: {ex.Message}";
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
            Window? window = App.MainWindow;
            IntPtr hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hWnd);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                OutputTextBox.Text = "Translating to NBT...";

                await _nbtService.ConvertSnbtToNbtAsync(_currentSnbtContent, file.Path);

                OutputTextBox.Text =
                    $"✅ Success!\n\nFile saved to: {file.Path}\n\nOriginal SNBT Content:\n{new string('=', 50)}\n{_currentSnbtContent}";
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"❌ Error when converting to NBT: {ex.Message}";
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

            bool isValid;
            string fileInfo;

            if (_isAnvilFile)
            {
                isValid = await _nbtService.IsValidAnvilFileAsync(_selectedFilePath);
                if (Path.GetExtension(_selectedFilePath).ToLowerInvariant() == ".mca")
                {
                    AnvilRegionInfo regionInfo = await _nbtService.GetRegionInfoAsync(_selectedFilePath);
                    fileInfo =
                        $"Anvil Region File\nRegion: ({regionInfo.RegionX}, {regionInfo.RegionZ})\nChunks: {regionInfo.ValidChunks}/{regionInfo.TotalChunks}";
                }
                else
                {
                    fileInfo = await _nbtService.GetNbtFileInfoAsync(_selectedFilePath);
                }
            }
            else
            {
                isValid = await _nbtService.IsValidNbtFileAsync(_selectedFilePath);
                fileInfo = await _nbtService.GetNbtFileInfoAsync(_selectedFilePath);
            }

            OutputTextBox.Text = $"🔍 Validation Result: {(isValid ? "✅ Valid File" : "❌ Invalid File")}\n\n{fileInfo}";
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"❌ Error when validating file: {ex.Message}";
        }
    }

    private async void RunRoundTripTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            OutputTextBox.Text = "Please select a file first to run the round-trip test.";
            return;
        }

        try
        {
            // Store reference to button to prevent multiple clicks
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;

            OutputTextBox.Text = "\ud83e\uddea Running Round-Trip Test on selected file...\n\n";
            OutputTextBox.Text += $"File: {Path.GetFileName(_selectedFilePath)}\n";

            if (_isAnvilFile)
            {
                // Use Progress<T> to report progress
                var progress = new Progress<string>(message =>
                {
                    // Append to existing text without clearing
                    OutputTextBox.Text += $"{message}\n";
                });

                // Create test instance and run with progress reporting
                var test = new RoundtripConversionTest(progress);
                bool success = await Task.Run(async () =>
                {
                    try
                    {
                        return await test.TestRoundtripConversion(_selectedFilePath);
                    }
                    catch (Exception ex)
                    {
                        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                            CoreDispatcherPriority.Normal,
                            () => OutputTextBox.Text += $"\nError during test: {ex.Message}\n");
                        return false;
                    }
                });

                if (success)
                {
                    OutputTextBox.Text += "\n✅ MCA Round-Trip Test PASSED!\n";
                    OutputTextBox.Text += "MCA → SNBT → MCA conversion works correctly for the selected file.";
                }
                else
                {
                    OutputTextBox.Text += "\n❌ MCA Round-Trip Test FAILED!\n";
                    OutputTextBox.Text += "There was an issue with the MCA conversion process.";
                }
            }
            else
            {
                // Test NBT file roundtrip conversion
                OutputTextBox.Text += "\nRunning NBT round-trip test...\n";

                var test = new NbtRoundTripTest();
                bool success = await Task.Run(async () =>
                {
                    try
                    {
                        return await test.TestRoundTripConversion();
                    }
                    catch (Exception ex)
                    {
                        await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                            CoreDispatcherPriority.Normal,
                            () => OutputTextBox.Text += $"\nError during test: {ex.Message}\n");
                        return false;
                    }
                });

                if (success)
                {
                    OutputTextBox.Text += "\n✅ NBT Round-Trip Test PASSED!\n";
                    OutputTextBox.Text += "NBT → SNBT → NBT conversion works correctly.";
                }
                else
                {
                    OutputTextBox.Text += "\n❌ NBT Round-Trip Test FAILED!\n";
                    OutputTextBox.Text += "There was an issue with the NBT conversion process.";
                }
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"❌ Error running round-trip test: {ex.Message}\n\nStack trace:\n{ex.StackTrace}";
        }
        finally
        {
            // Re-enable button whether successful or not
            var button = sender as Button;
            if (button != null) button.IsEnabled = true;
        }
    }

    private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
    {
        OutputTextBox.Text = "Ready...Select an NBT, DAT, or Anvil (.mca/.mcc) file to start debugging.";
        _currentSnbtContent = null;
        ConvertToNbtButton.IsEnabled = false;
    }

    private async void ConvertMcaToSnbtButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            OutputTextBox.Text = "Please select an MCA file first.";
            return;
        }

        try
        {
            // Disable button to prevent multiple clicks
            ConvertMcaToSnbtButton.IsEnabled = false;

            // Show initial status
            OutputTextBox.Text = "Preparing to convert MCA file to SNBT...";

            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("SNBT File", new[] { ".snbt" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_selectedFilePath);

            // Get current window handle for the picker
            Window? window = App.MainWindow;
            IntPtr hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hWnd);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                // Create progress reporter to update UI
                var progress = new Progress<string>(message =>
                {
                    OutputTextBox.Text = $"Conversion in progress...\n\n{message}";
                });

                // Execute conversion asynchronously
                await _nbtService.ConvertToSnbtAsync(_selectedFilePath, file.Path, progress);

                // Update UI after conversion
                var fileInfo = new FileInfo(file.Path);
                OutputTextBox.Text = "\u2705 Success!\n\n";
                OutputTextBox.Text += "MCA file converted to SNBT format\n";
                OutputTextBox.Text += $"Input: {Path.GetFileName(_selectedFilePath)}\n";
                OutputTextBox.Text += $"Output: {file.Path}\n";
                OutputTextBox.Text += $"SNBT file size: {fileInfo.Length / 1024.0:F1} KB\n\n";
                OutputTextBox.Text += "The SNBT file contains all chunk data from the MCA region file.";
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"❌ Error converting MCA to SNBT: {ex.Message}\n\n{ex.StackTrace}";
        }
        finally
        {
            // Re-enable button whether successful or not
            ConvertMcaToSnbtButton.IsEnabled = true;
        }
    }

    private async void ConvertSnbtToMcaButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Disable button to prevent multiple clicks
            ConvertSnbtToMcaButton.IsEnabled = false;

            // First, let user select an SNBT file
            var openPicker = new FileOpenPicker();
            openPicker.FileTypeFilter.Add(".snbt");
            openPicker.FileTypeFilter.Add("*");

            // Get current window handle for the picker
            Window? window = App.MainWindow;
            IntPtr hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(openPicker, hWnd);

            StorageFile? snbtFile = await openPicker.PickSingleFileAsync();
            if (snbtFile == null)
            {
                OutputTextBox.Text = "No SNBT file selected.";
                return;
            }

            OutputTextBox.Text = "Converting SNBT file to MCA...";

            // Now let user choose where to save the MCA file
            var savePicker = new FileSavePicker();
            savePicker.FileTypeChoices.Add("MCA File", new[] { ".mca" });
            savePicker.SuggestedFileName = Path.GetFileNameWithoutExtension(snbtFile.Path);

            InitializeWithWindow.Initialize(savePicker, hWnd);

            StorageFile? mcaFile = await savePicker.PickSaveFileAsync();
            if (mcaFile != null)
            {
                // Create progress reporter to update UI
                var progress = new Progress<string>(message =>
                {
                    OutputTextBox.Text = $"Conversion in progress...\n\n{message}";
                });

                // Execute conversion asynchronously
                await _nbtService.ConvertFromSnbtAsync(snbtFile.Path, mcaFile.Path, progress);

                // Update UI after conversion
                var fileInfo = new FileInfo(mcaFile.Path);
                OutputTextBox.Text = "\u2705 Success!\n\n";
                OutputTextBox.Text += "SNBT file converted to MCA format\n";
                OutputTextBox.Text += $"Input: {snbtFile.Path}\n";
                OutputTextBox.Text += $"Output: {mcaFile.Path}\n";
                OutputTextBox.Text += $"MCA file size: {fileInfo.Length / 1024.0:F1} KB\n\n";
                OutputTextBox.Text += "The MCA file has been reconstructed from the SNBT data.";
            }
        }
        catch (Exception ex)
        {
            OutputTextBox.Text = $"❌ Error converting SNBT to MCA: {ex.Message}\n\n{ex.StackTrace}";
        }
        finally
        {
            // Re-enable button whether successful or not
            ConvertSnbtToMcaButton.IsEnabled = true;
        }
    }
}
