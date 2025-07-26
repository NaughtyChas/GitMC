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
using System.Text;

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
                // Disable performance counters in debug mode to prevent UI freezing
                #if !DEBUG
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                #endif
                
                _performanceTimer = new DispatcherTimer();
                _performanceTimer.Interval = TimeSpan.FromSeconds(2); // Reduced frequency
                _performanceTimer.Tick += UpdatePerformanceMetrics;
            }
            catch (Exception ex)
            {
                LogMessage($"Performance meter initialization failed: {ex.Message}");
                // Continue without performance monitoring
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
                            
                            StartTranslationButton.IsEnabled = true;
                            StartTranslationButton.IsEnabled = true;
                            
                            LogMessage($"Select save: {path}");
                            LogMessage("Save validation successful");
                        }
                        else
                        {
                            SaveInfoTextBlock.Text = "⚠ Detected invalid Minecraft save";
                            SaveInfoTextBlock.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                            SaveInfoTextBlock.Visibility = Visibility.Visible;
                            
                            StartTranslationButton.IsEnabled = false;
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
            
            // Log file type breakdown
            var mcaFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".mca").Count();
            var mccFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".mcc").Count();
            var datFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".dat").Count();
            var nbtFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".nbt").Count();
            
            LogMessage($"  📊 File breakdown: {mcaFiles} MCA, {mccFiles} MCC, {datFiles} DAT, {nbtFiles} NBT");

            UpdateProgress(10, "Copying files...");
            
            // Step 3: Copy all files first
            await CopyAllFiles(_selectedSavePath, gitMcPath, cancellationToken);
            
            UpdateProgress(30, "Starting NBT translation process...");
            
            // Step 4: Process selected file types with enhanced tracking
            var processedCount = 0;
            var totalFiles = filesToProcess.Count;
            var multiChunkFiles = 0;
            var totalChunksProcessed = 0;
            var lastUiUpdate = DateTime.Now;
            
            foreach (var fileInfo in filesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var relativePath = Path.GetRelativePath(_selectedSavePath, fileInfo.FullName);
                    var targetPath = Path.Combine(gitMcPath, relativePath);
                    
                    // Significantly reduce logging frequency to improve performance
                    if (processedCount % 50 == 0 || processedCount == 0)
                    {
                        LogMessage($"Processing batch: {processedCount}-{Math.Min(processedCount + 49, totalFiles)} of {totalFiles}");
                    }
                    
                    // Track multi-chunk processing
                    var isMultiChunk = await ProcessFileWithTracking(targetPath, fileInfo.Extension, cancellationToken);
                    if (isMultiChunk.IsMultiChunk)
                    {
                        multiChunkFiles++;
                        totalChunksProcessed += isMultiChunk.ChunkCount;
                    }
                    
                    processedCount++;
                    var progress = 30 + (processedCount * 60 / totalFiles);
                    
                    // Update UI much less frequently and with time-based throttling
                    var timeSinceLastUpdate = DateTime.Now - lastUiUpdate;
                    if (timeSinceLastUpdate.TotalMilliseconds > 3000 || processedCount == totalFiles) // Every 3 seconds or at completion
                    {
                        UpdateProgress(progress, $"Processing files {processedCount}/{totalFiles}...");
                        lastUiUpdate = DateTime.Now;
                        
                        // Minimal yield to UI thread for responsiveness
                        if (processedCount % 25 == 0)
                        {
                            await Task.Yield();
                        }
                    }
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
            
            // Log final statistics
            LogMessage("=== Save translation process complete ===");
            LogMessage($"📈 Processing Statistics:");
            LogMessage($"  ✓ Total files processed: {processedCount}");
            LogMessage($"  📦 Multi-chunk files: {multiChunkFiles}");
            LogMessage($"  🧊 Total chunks processed: {totalChunksProcessed}");
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
                // If the directory to be copied is GitMC directory, skip it.
                // When a folder called GitMC is copied, block it:
                if (subDir.Name.Equals("GitMC", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                CopyDirectory(subDir, target.CreateSubdirectory(subDir.Name), cancellationToken);

            }
        }

        private async Task ProcessFile(string filePath, string extension, CancellationToken cancellationToken)
        {
            try
            {
                var tempSnbtPath = filePath + ".snbt";
                var backupPath = CreateBackupCheckBox.IsChecked == true ? filePath + ".backup" : null;
                
                // Check if file exists and is accessible
                if (!File.Exists(filePath))
                {
                    LogMessage($"  ⚠ File not found: {Path.GetFileName(filePath)}");
                    return;
                }
                
                // Check file size and skip very large files that might cause issues
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB limit
                {
                    LogMessage($"  ⚠ Skipping large file: {Path.GetFileName(filePath)} ({fileInfo.Length / 1024 / 1024}MB)");
                    return;
                }
                
                // Log file details for better tracking
                LogMessage($"  Processing: {Path.GetFileName(filePath)} ({fileInfo.Length / 1024}KB, {extension.ToUpper()})");
                
                // Create backup if requested
                if (backupPath != null)
                {
                    try
                    {
                        File.Copy(filePath, backupPath, true);
                        LogMessage($"  ✓ Backup created: {Path.GetFileName(backupPath)}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"  ⚠ Backup failed for {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
                
                // Convert to SNBT with timeout protection and enhanced logging
                LogMessage($"  🔄 Converting to SNBT: {Path.GetFileName(filePath)}");
                
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5))) // 5-minute timeout per file
                using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    await ConvertToSnbt(filePath, tempSnbtPath, extension, combinedCts.Token);
                    
                    // Log SNBT file info for verification
                    if (File.Exists(tempSnbtPath))
                    {
                        var snbtSize = new FileInfo(tempSnbtPath).Length;
                        LogMessage($"  ✓ SNBT created: {snbtSize / 1024}KB");
                        
                        // For MCA files, check if multi-chunk was detected
                        if (extension == ".mca" || extension == ".mcc")
                        {
                            var snbtContent = File.ReadAllText(tempSnbtPath);
                            if (snbtContent.Contains("# Chunk") && snbtContent.Split("# Chunk").Length > 2)
                            {
                                var chunkCount = snbtContent.Split("# Chunk").Length - 1;
                                LogMessage($"  📦 Multi-chunk file detected: {chunkCount} chunks");
                            }
                        }
                    }
                    
                    // Convert back from SNBT
                    LogMessage($"  🔄 Converting back from SNBT: {Path.GetFileName(filePath)}");
                    await ConvertFromSnbt(tempSnbtPath, filePath, extension, combinedCts.Token);
                    
                    // Verify final result
                    var finalSize = new FileInfo(filePath).Length;
                    LogMessage($"  ✓ Final file: {finalSize / 1024}KB");
                }
                
                // Clean up SNBT file if not preserving
                if (PreserveSNBTCheckBox.IsChecked != true && File.Exists(tempSnbtPath))
                {
                    try
                    {
                        File.Delete(tempSnbtPath);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"  ⚠ Failed to delete temp SNBT: {ex.Message}");
                    }
                }
                else if (File.Exists(tempSnbtPath))
                {
                    LogMessage($"  📁 SNBT preserved: {Path.GetFileName(tempSnbtPath)}");
                }
                
                LogMessage($"  ✅ Complete: {Path.GetFileName(filePath)}");
            }
            catch (OperationCanceledException)
            {
                LogMessage($"  ⏹ Cancelled: {Path.GetFileName(filePath)}");
                throw;
            }
            catch (Exception ex)
            {
                LogMessage($"  ❌ Failed: {Path.GetFileName(filePath)} - {ex.Message}");
                throw;
            }
        }

        private async Task<(bool IsMultiChunk, int ChunkCount)> ProcessFileWithTracking(string filePath, string extension, CancellationToken cancellationToken)
        {
            var isMultiChunk = false;
            var chunkCount = 0;
            
            try
            {
                var tempSnbtPath = filePath + ".snbt";
                var backupPath = CreateBackupCheckBox.IsChecked == true ? filePath + ".backup" : null;
                
                // Check if file exists and is accessible
                if (!File.Exists(filePath))
                {
                    return (false, 0);
                }
                
                // Check file size and skip very large files that might cause issues
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB limit
                {
                    LogMessage($"  ⚠ Skipping large file: {Path.GetFileName(filePath)} ({fileInfo.Length / 1024 / 1024}MB)");
                    return (false, 0);
                }
                
                // Create backup if requested
                if (backupPath != null)
                {
                    try
                    {
                        File.Copy(filePath, backupPath, true);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"  ⚠ Backup failed for {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                }
                
                // Convert to SNBT with timeout protection 
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5))) // 5-minute timeout per file
                using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    await ConvertToSnbt(filePath, tempSnbtPath, extension, combinedCts.Token);
                    
                    // Optimized multi-chunk detection: only read file once and use efficient string operations
                    if (File.Exists(tempSnbtPath))
                    {
                        // For MCA files, efficiently check chunk count without extensive string splitting
                        if (extension == ".mca" || extension == ".mcc")
                        {
                            // Use async file reading with optimized chunk detection
                            using (var fileStream = new FileStream(tempSnbtPath, FileMode.Open, FileAccess.Read))
                            using (var reader = new StreamReader(fileStream, Encoding.UTF8, bufferSize: 8192))
                            {
                                var firstKiloBytes = new char[1024];
                                var charsRead = await reader.ReadAsync(firstKiloBytes, 0, 1024);
                                var headerText = new string(firstKiloBytes, 0, charsRead);
                                
                                // Look for total chunks in header (much faster than parsing entire file)
                                if (headerText.Contains("// Total chunks:"))
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(headerText, @"// Total chunks:\s*(\d+)");
                                    if (match.Success && int.TryParse(match.Groups[1].Value, out chunkCount))
                                    {
                                        isMultiChunk = chunkCount > 1;
                                    }
                                }
                            }
                        }
                    }
                    
                    // Convert back from SNBT
                    await ConvertFromSnbt(tempSnbtPath, filePath, extension, combinedCts.Token);
                }
                
                // Clean up SNBT file if not preserving
                if (PreserveSNBTCheckBox.IsChecked != true && File.Exists(tempSnbtPath))
                {
                    try
                    {
                        File.Delete(tempSnbtPath);
                    }
                    catch (Exception)
                    {
                        // Silently ignore cleanup errors
                    }
                }
                
                return (isMultiChunk, chunkCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessage($"  ❌ Failed: {Path.GetFileName(filePath)} - {ex.Message}");
                return (false, 0);
            }
        }

        private async Task ConvertToSnbt(string inputPath, string outputPath, string extension, CancellationToken cancellationToken)
        {
            try
            {
                // Add file validation before conversion
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"Input file not found: {inputPath}");
                }
                
                var fileInfo = new FileInfo(inputPath);
                if (fileInfo.Length == 0)
                {
                    throw new InvalidDataException($"Input file is empty: {inputPath}");
                }
                
                // Create minimal progress reporter to reduce UI overhead
                var progress = new Progress<string>(message =>
                {
                    // Only log critical conversion events, not detailed chunk processing
                    if (message.Contains("error") || message.Contains("failed") || message.Contains("ERROR"))
                    {
                        LogMessage($"    {message}");
                    }
                });
                
                // Use the enhanced async NbtService with minimal progress reporting
                await _nbtService.ConvertToSnbtAsync(inputPath, outputPath, progress);
                
                // Verify output was created
                if (!File.Exists(outputPath))
                {
                    throw new InvalidOperationException($"SNBT output file was not created: {outputPath}");
                }
            }
            catch (Exception ex)
            {
                // Enhanced error reporting
                var errorMessage = $"ConvertToSnbt failed for {Path.GetFileName(inputPath)}: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner: {ex.InnerException.Message}";
                }
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        private async Task ConvertFromSnbt(string inputPath, string outputPath, string extension, CancellationToken cancellationToken)
        {
            try
            {
                // Add file validation before conversion
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"SNBT input file not found: {inputPath}");
                }
                
                var fileInfo = new FileInfo(inputPath);
                if (fileInfo.Length == 0)
                {
                    throw new InvalidDataException($"SNBT input file is empty: {inputPath}");
                }
                
                // Create minimal progress reporter to reduce UI overhead
                var progress = new Progress<string>(message =>
                {
                    // Only log critical conversion events, not detailed chunk processing
                    if (message.Contains("error") || message.Contains("failed") || message.Contains("ERROR"))
                    {
                        LogMessage($"    {message}");
                    }
                });
                
                // Use the enhanced async NbtService with minimal progress reporting
                await _nbtService.ConvertFromSnbtAsync(inputPath, outputPath, progress);
                
                // Verify output was created
                if (!File.Exists(outputPath))
                {
                    throw new InvalidOperationException($"NBT output file was not created: {outputPath}");
                }
            }
            catch (Exception ex)
            {
                // Enhanced error reporting
                var errorMessage = $"ConvertFromSnbt failed for {Path.GetFileName(inputPath)}: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner: {ex.InnerException.Message}";
                }
                throw new InvalidOperationException(errorMessage, ex);
            }
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
            // Use synchronous Enqueue instead of TryEnqueue to ensure UI updates are processed
            try
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    if (OverallProgressBar != null) OverallProgressBar.Value = value;
                    if (ProgressTextBlock != null) ProgressTextBlock.Text = text;
                });
            }
            catch (Exception ex)
            {
                // Fallback - ignore UI update errors during debug
                System.Diagnostics.Debug.WriteLine($"UI Update Error: {ex.Message}");
            }
        }

        private readonly Queue<string> _logQueue = new Queue<string>();
        private readonly object _logLock = new object();
        private DateTime _lastLogFlush = DateTime.Now;
        private bool _isLogFlushScheduled = false;

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            
            try
            {
                // Add to queue instead of immediate UI update
                lock (_logLock)
                {
                    _logQueue.Enqueue(logEntry);
                    
                    // Limit queue size to prevent memory buildup
                    while (_logQueue.Count > 1000)
                    {
                        _logQueue.Dequeue();
                    }
                }
                
                // Schedule batch flush every 500ms or for critical messages
                var isCritical = message.Contains("Error") || message.Contains("Failed") || message.Contains("❌") || message.Contains("===");
                var timeSinceLastFlush = DateTime.Now - _lastLogFlush;
                
                if (isCritical || timeSinceLastFlush.TotalMilliseconds > 500)
                {
                    FlushLogQueue();
                }
                else if (!_isLogFlushScheduled)
                {
                    ScheduleLogFlush();
                }
            }
            catch (Exception ex)
            {
                // Fallback - output to debug console
                System.Diagnostics.Debug.WriteLine($"Log Error: {ex.Message} - Message: {logEntry}");
            }
        }

        private void ScheduleLogFlush()
        {
            if (_isLogFlushScheduled) return;
            
            _isLogFlushScheduled = true;
            
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                _isLogFlushScheduled = false;
                FlushLogQueue();
            }, TaskScheduler.Default);
        }

        private void FlushLogQueue()
        {
            try
            {
                List<string> logsToFlush;
                lock (_logLock)
                {
                    if (_logQueue.Count == 0) return;
                    
                    logsToFlush = new List<string>(_logQueue);
                    _logQueue.Clear();
                }
                
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        if (LogTextBox != null)
                        {
                            var sb = new StringBuilder();
                            
                            // Check if we need to trim existing content
                            var currentLength = LogTextBox.Text.Length;
                            var newContentLength = logsToFlush.Sum(log => log.Length + 1); // +1 for newline
                            
                            if (currentLength + newContentLength > 80000) // ~80KB limit
                            {
                                // Keep only the last portion of current text
                                var lines = LogTextBox.Text.Split('\n');
                                var keepLines = lines.Skip(Math.Max(0, lines.Length - 200)); // Keep last 200 lines
                                sb.AppendLine(string.Join('\n', keepLines));
                                sb.AppendLine("... [Earlier logs truncated for performance] ...");
                            }
                            else
                            {
                                sb.Append(LogTextBox.Text);
                            }
                            
                            // Add new logs
                            foreach (var log in logsToFlush)
                            {
                                sb.AppendLine(log);
                            }
                            
                            LogTextBox.Text = sb.ToString();
                            
                            // Always auto-scroll to bottom for better UX
                            if (LogTextBox.Parent is ScrollViewer scrollViewer)
                            {
                                scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, false);
                            }
                        }
                        
                        _lastLogFlush = DateTime.Now;
                    }
                    catch (Exception)
                    {
                        // Ignore UI errors during heavy processing
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FlushLogQueue Error: {ex.Message}");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _performanceTimer?.Stop();
            
            // Flush any remaining logs
            FlushLogQueue();
            
            base.OnNavigatedFrom(e);
        }
    }
}
