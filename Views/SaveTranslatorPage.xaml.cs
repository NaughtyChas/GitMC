using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using GitMC.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace GitMC.Views
{
    public sealed partial class SaveTranslatorPage : Page
    {
        private readonly INbtService _nbtService;
        private CancellationTokenSource? _cancellationTokenSource;
        private string? _selectedSavePath;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _memoryCounter;
        private DispatcherTimer? _performanceTimer;

        public SaveTranslatorPage()
        {
            this.InitializeComponent();
            _nbtService = new NbtService();
            InitializePerformanceCounters();
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                
                _performanceTimer = new DispatcherTimer();
                _performanceTimer.Interval = TimeSpan.FromSeconds(1);
                _performanceTimer.Tick += UpdatePerformanceMetrics;
            }
            catch (Exception ex)
            {
                LogMessage($"Performance meter initialization failed: {ex.Message}");
            }
        }

        private void UpdatePerformanceMetrics(object? sender, object e)
        {
            try
            {
                var cpuUsage = _cpuCounter?.NextValue() ?? 0;
                CpuUsageBar.Value = Math.Min(cpuUsage, 100);

                var availableMemory = _memoryCounter?.NextValue() ?? 0;
                var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // Convert to MB
                var memoryUsage = Math.Max(0, 100 - (availableMemory / 16 * 100)); // Assuming 16GB total
                MemoryUsageBar.Value = Math.Min(memoryUsage, 100);
            }
            catch (Exception ex)
            {
                LogMessage($"Performance monitoring error: {ex.Message}");
            }
        }

        private async void BrowseSaveButton_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            // Get the current window's handle
            var window = App.MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                _selectedSavePath = folder.Path;
                SavePathTextBox.Text = _selectedSavePath;
                
                await ValidateSaveFolder(_selectedSavePath);
            }
        }

        private async Task ValidateSaveFolder(string path)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Check if it's a valid Minecraft save
                    var levelDatPath = Path.Combine(path, "level.dat");
                    var levelDatOldPath = Path.Combine(path, "level.dat_old");

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (File.Exists(levelDatPath) || File.Exists(levelDatOldPath))
                        {
                            SaveInfoTextBlock.Text = "✓ Detected valid Minecraft save";
                            SaveInfoTextBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                            SaveInfoTextBlock.Visibility = Visibility.Visible;
                            
                            TranslationOptionsExpander.IsEnabled = true;
                            StartTranslationButton.IsEnabled = true;
                            
                            LogMessage($"Select save: {path}");
                            LogMessage("Save validation successful");
                        }
                        else
                        {
                            SaveInfoTextBlock.Text = "⚠ Detected invalid Minecraft save";
                            SaveInfoTextBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                            SaveInfoTextBlock.Visibility = Visibility.Visible;
                            
                            TranslationOptionsExpander.IsEnabled = false;
                            StartTranslationButton.IsEnabled = false;
                            
                            LogMessage("Save validation failed: level.dat not found");
                        }
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        SaveInfoTextBlock.Text = $"Validation error: {ex.Message}";
                        SaveInfoTextBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                        SaveInfoTextBlock.Visibility = Visibility.Visible;
                        LogMessage($"Save validation error: {ex.Message}");
                    });
                }
            });
        }

        private async void StartTranslationButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedSavePath))
            {
                LogMessage("Error: No save path selected");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            
            StartTranslationButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            ProgressExpander.IsExpanded = true;
            LogExpander.IsExpanded = true;
            
            _performanceTimer?.Start();

            try
            {
                await PerformTranslation(_cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                LogMessage("Translation process was cancelled.");
            }
            catch (Exception ex)
            {
                LogMessage($"Translation process error: {ex.Message}");
            }
            finally
            {
                _performanceTimer?.Stop();
                StartTranslationButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                OverallProgressBar.Value = 0;
                ProgressTextBlock.Text = "Ready";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            LogMessage("Cancelling translation...");
        }

        private void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_selectedSavePath))
            {
                var gitMcPath = Path.Combine(_selectedSavePath, "GitMC");
                if (Directory.Exists(gitMcPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = gitMcPath,
                            UseShellExecute = true,
                            Verb = "open"
                        });
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Unable to open output folder: {ex.Message}");
                    }
                }
                else
                {
                    LogMessage("Output folder does not exist, please run translation first");
                }
            }
        }

        private async Task PerformTranslation(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_selectedSavePath))
            {
                LogMessage("Error: Selected save directory is empty");
                return;
            }

            LogMessage("=== Starting save translation process ===");

            var verifyIntegrity = VerifyIntegrityCheckBox.IsChecked == true;
            
            // Step 1: Create GitMC folder
            var gitMcPath = Path.Combine(_selectedSavePath, "GitMC");
            LogMessage($"Create GitMC folder: {gitMcPath}");
            Directory.CreateDirectory(gitMcPath);
            
            UpdateProgress(5, "Analyzing file structure...");
            
            // Step 2: Scan for files to process
            var filesToProcess = await ScanForFiles(_selectedSavePath, cancellationToken);
            LogMessage($"Found {filesToProcess.Count} files to process");

            UpdateProgress(10, "Copying files...");
            
            // Step 3: Copy all files first
            await CopyAllFiles(_selectedSavePath, gitMcPath, cancellationToken);
            
            UpdateProgress(30, "Starting NBT translation process...");
            
            // Step 4: Process selected file types
            var processedCount = 0;
            var totalFiles = filesToProcess.Count;
            
            foreach (var fileInfo in filesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var relativePath = Path.GetRelativePath(_selectedSavePath, fileInfo.FullName);
                    var targetPath = Path.Combine(gitMcPath, relativePath);
                    
                    LogMessage($"Processing files: {relativePath}");
                    
                    await ProcessFile(targetPath, fileInfo.Extension, cancellationToken);
                    
                    processedCount++;
                    var progress = 30 + (processedCount * 60 / totalFiles);
                    UpdateProgress(progress, $"Processing files {processedCount}/{totalFiles}: {Path.GetFileName(fileInfo.Name)}");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to process file {fileInfo.Name}: {ex.Message}");
                }
            }

            UpdateProgress(95, "Verifying translation results...");

            // Step 5: Verify if enabled
            if (verifyIntegrity)
            {
                await VerifyTranslation(_selectedSavePath, gitMcPath, cancellationToken);
            }
            
            UpdateProgress(100, "Translate complete!");
            LogMessage("=== Save translation process complete ===");
            LogMessage($"Output folder: {gitMcPath}");
        }

        private async Task<List<FileInfo>> ScanForFiles(string savePath, CancellationToken cancellationToken)
        {
            var files = new List<FileInfo>();
            var directory = new DirectoryInfo(savePath);
            
            var processRegionFiles = RegionFilesCheckBox.IsChecked == true;
            var processDataFiles = DataFilesCheckBox.IsChecked == true;
            var processNbtFiles = NbtFilesCheckBox.IsChecked == true;
            var processLevelData = LevelDataCheckBox.IsChecked == true;
            
            await Task.Run(() =>
            {
                if (processRegionFiles)
                {
                    ScanDirectory(directory, "*.mca", files, cancellationToken);
                    ScanDirectory(directory, "*.mcc", files, cancellationToken);
                }
                
                if (processDataFiles)
                {
                    ScanDirectory(directory, "*.dat", files, cancellationToken);
                }
                
                if (processNbtFiles)
                {
                    ScanDirectory(directory, "*.nbt", files, cancellationToken);
                }
                
                if (processLevelData)
                {
                    var levelDat = new FileInfo(Path.Combine(savePath, "level.dat"));
                    if (levelDat.Exists) files.Add(levelDat);
                    
                    var levelDatOld = new FileInfo(Path.Combine(savePath, "level.dat_old"));
                    if (levelDatOld.Exists) files.Add(levelDatOld);
                }
            }, cancellationToken);
            
            return files;
        }

        private void ScanDirectory(DirectoryInfo directory, string pattern, List<FileInfo> files, CancellationToken cancellationToken)
        {
            try
            {
                files.AddRange(directory.GetFiles(pattern, SearchOption.AllDirectories));
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (UnauthorizedAccessException)
            {
                LogMessage($"Unable to access directory: {directory.FullName}");
            }
        }

        private async Task CopyAllFiles(string sourcePath, string targetPath, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                CopyDirectory(new DirectoryInfo(sourcePath), new DirectoryInfo(targetPath), cancellationToken);
            }, cancellationToken);

            LogMessage("File copy complete");
        }

        private void CopyDirectory(DirectoryInfo source, DirectoryInfo target, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            Directory.CreateDirectory(target.FullName);
            
            // Copy files
            foreach (var file in source.GetFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                file.CopyTo(Path.Combine(target.FullName, file.Name), true);
            }
            
            // Copy subdirectories
            foreach (var subDir in source.GetDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (subDir.Name.Equals("GitMC", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // Skip copying the GitMC folder itself
                }
                else
                { 
                    CopyDirectory(subDir, target.CreateSubdirectory(subDir.Name), cancellationToken);
                }
            }
        }

        private async Task ProcessFile(string filePath, string extension, CancellationToken cancellationToken)
        {
            try
            {
                var tempSnbtPath = filePath + ".snbt";
                var backupPath = CreateBackupCheckBox.IsChecked == true ? filePath + ".backup" : null;
                
                // Create backup if requested
                if (backupPath != null)
                {
                    File.Copy(filePath, backupPath, true);
                }
                
                // Convert to SNBT
                LogMessage($"  Convert to SNBT: {Path.GetFileName(filePath)}");
                await ConvertToSnbt(filePath, tempSnbtPath, extension, cancellationToken);
                
                // Convert back from SNBT
                LogMessage($"  Convert back from SNBT: {Path.GetFileName(filePath)}");
                await ConvertFromSnbt(tempSnbtPath, filePath, extension, cancellationToken);
                
                // Clean up SNBT file if not preserving
                if (PreserveSNBTCheckBox.IsChecked != true && File.Exists(tempSnbtPath))
                {
                    File.Delete(tempSnbtPath);
                }
                
                LogMessage($"  ✓ Complete: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                LogMessage($"  ✗ Failed: {Path.GetFileName(filePath)} - {ex.Message}");
                throw;
            }
        }

        private async Task ConvertToSnbt(string inputPath, string outputPath, string extension, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                // Use the NbtService to convert to SNBT
                _nbtService.ConvertToSnbt(inputPath, outputPath);
            }, cancellationToken);
        }

        private async Task ConvertFromSnbt(string inputPath, string outputPath, string extension, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                // Use the NbtService to convert from SNBT back to original format
                _nbtService.ConvertFromSnbt(inputPath, outputPath);
            }, cancellationToken);
        }

        private async Task VerifyTranslation(string originalPath, string translatedPath, CancellationToken cancellationToken)
        {
            LogMessage("Validating file integrity...");
            
            await Task.Run(() =>
            {
                var originalFiles = Directory.GetFiles(originalPath, "*", SearchOption.AllDirectories);
                var verifiedCount = 0;
                var totalCount = originalFiles.Length;
                
                foreach (var originalFile in originalFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var relativePath = Path.GetRelativePath(originalPath, originalFile);
                    var translatedFile = Path.Combine(translatedPath, relativePath);
                    
                    if (File.Exists(translatedFile))
                    {
                        // For NBT files, we can do more sophisticated comparison
                        // For now, just check file sizes are reasonable
                        var originalSize = new FileInfo(originalFile).Length;
                        var translatedSize = new FileInfo(translatedFile).Length;
                        
                        var sizeDifference = Math.Abs(originalSize - translatedSize) / (double)originalSize;
                        if (sizeDifference > 0.1) // Allow 10% size difference
                        {
                            LogMessage($"⚠ File size difference is large: {relativePath} (Original: {originalSize}, Translated: {translatedSize})");
                        }
                    }
                    else
                    {
                        LogMessage($"⚠ Missing translated file: {relativePath}");
                    }
                    
                    verifiedCount++;
                    if (verifiedCount % 100 == 0)
                    {
                        LogMessage($"Validated {verifiedCount}/{totalCount} files");
                    }
                }

                LogMessage($"Validation complete: {verifiedCount}/{totalCount} files");
            }, cancellationToken);
        }

        private void UpdateProgress(int value, string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                OverallProgressBar.Value = value;
                ProgressTextBlock.Text = text;
            });
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            
            DispatcherQueue.TryEnqueue(() =>
            {
                LogTextBlock.Text += logEntry + "\n";
                
                // Auto-scroll to bottom
                if (LogTextBlock.Parent is ScrollViewer scrollViewer)
                {
                    scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null);
                }
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _performanceTimer?.Stop();
            base.OnNavigatedFrom(e);
        }
    }
}
