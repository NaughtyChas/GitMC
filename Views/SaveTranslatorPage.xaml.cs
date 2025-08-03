using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using GitMC.Services;
using GitMC.Utils;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace GitMC.Views;

public sealed partial class SaveTranslatorPage : Page
{
    private readonly object _logLock = new();

    private readonly Queue<string> _logQueue = new();
    private readonly INbtService _nbtService;
    private CancellationTokenSource? _cancellationTokenSource;
    private PerformanceCounter? _cpuCounter;
    private bool _isLogFlushScheduled;
    private DateTime _lastLogFlush = DateTime.Now;
    private PerformanceCounter? _memoryCounter;
    private DispatcherTimer? _performanceTimer;
    private string? _selectedSavePath;
    private string? _selectedGitMcPath;

    public SaveTranslatorPage()
    {
        InitializeComponent();
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
            float cpuUsage = _cpuCounter?.NextValue() ?? 0;
            CpuUsageBar.Value = Math.Min(cpuUsage, 100);

            float availableMemory = _memoryCounter?.NextValue() ?? 0;
            long totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // Convert to MB
            float memoryUsage = Math.Max(0, 100 - availableMemory / 16 * 100); // Assuming 16GB total
            MemoryUsageBar.Value = Math.Min(memoryUsage, 100);
        }
        catch (Exception ex)
        {
            LogMessage($"Performance monitoring error: {ex.Message}");
        }
    }

    private async void BrowseSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            // Get the current window's handle
            Window? window = App.MainWindow;
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                _selectedSavePath = folder.Path;
                SavePathTextBox.Text = _selectedSavePath;

                await ValidateSaveFolder(_selectedSavePath).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in BrowseSaveButton_Click: {ex.Message}");
        }
    }

    private async Task ValidateSaveFolder(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                // Check if it's a valid Minecraft save
                string levelDatPath = Path.Combine(path, "level.dat");
                string levelDatOldPath = Path.Combine(path, "level.dat_old");

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (File.Exists(levelDatPath) || File.Exists(levelDatOldPath))
                    {
                        SaveInfoTextBlock.Text = "✓ Detected valid Minecraft save";
                        SaveInfoTextBlock.Foreground =
                            (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                        SaveInfoTextBlock.Visibility = Visibility.Visible;

                        StartTranslationButton.IsEnabled = true;
                        StartTranslationButton.IsEnabled = true;

                        LogMessage($"Select save: {path}");
                        LogMessage("Save validation successful");
                    }
                    else
                    {
                        SaveInfoTextBlock.Text = "⚠ Detected invalid Minecraft save";
                        SaveInfoTextBlock.Foreground =
                            (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
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
                    SaveInfoTextBlock.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
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
            string gitMcPath = Path.Combine(_selectedSavePath, "GitMC");
            if (Directory.Exists(gitMcPath))
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = gitMcPath, UseShellExecute = true, Verb = "open" });
                }
                catch (Exception ex)
                {
                    LogMessage($"Unable to open output folder: {ex.Message}");
                }
            else
                LogMessage("Output folder does not exist, please run translation first");
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

        bool verifyIntegrity = VerifyIntegrityCheckBox.IsChecked == true;

        // Step 1: Create GitMC folder
        string gitMcPath = Path.Combine(_selectedSavePath, "GitMC");
        LogMessage($"Create GitMC folder: {gitMcPath}");
        Directory.CreateDirectory(gitMcPath);

        UpdateProgress(5, "Analyzing file structure...");

        // Step 2: Scan for files to process
        List<FileInfo> filesToProcess = await ScanForFiles(_selectedSavePath, cancellationToken);
        LogMessage($"Found {filesToProcess.Count} files to process");

        // Log file type breakdown
        int mcaFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".mca").Count();
        int mccFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".mcc").Count();
        int datFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".dat").Count();
        int nbtFiles = filesToProcess.Where(f => f.Extension.ToLower() == ".nbt").Count();

        LogMessage($"  📊 File breakdown: {mcaFiles} MCA, {mccFiles} MCC, {datFiles} DAT, {nbtFiles} NBT");

        UpdateProgress(10, "Copying files...");

        // Step 3: Copy all files first
        await CopyAllFiles(_selectedSavePath, gitMcPath, cancellationToken);

        UpdateProgress(30, "Starting NBT translation process...");

        // Step 4: Process selected file types with enhanced tracking
        int processedCount = 0;
        int totalFiles = filesToProcess.Count;
        int multiChunkFiles = 0;
        int totalChunksProcessed = 0;
        DateTime lastUiUpdate = DateTime.Now;

        foreach (FileInfo fileInfo in filesToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string relativePath = Path.GetRelativePath(_selectedSavePath, fileInfo.FullName);
                string targetPath = Path.Combine(gitMcPath, relativePath);

                // Significantly reduce logging frequency to improve performance
                if (processedCount % 50 == 0 || processedCount == 0)
                    LogMessage(
                        $"Processing batch: {processedCount}-{Math.Min(processedCount + 49, totalFiles)} of {totalFiles}");

                // Track multi-chunk processing
                (bool IsMultiChunk, int ChunkCount) isMultiChunk =
                    await ProcessFileWithTracking(targetPath, fileInfo.Extension, cancellationToken);
                if (isMultiChunk.IsMultiChunk)
                {
                    multiChunkFiles++;
                    totalChunksProcessed += isMultiChunk.ChunkCount;
                }

                processedCount++;
                int progress = 30 + processedCount * 60 / totalFiles;

                // Update UI much less frequently and with time-based throttling
                TimeSpan timeSinceLastUpdate = DateTime.Now - lastUiUpdate;
                if (timeSinceLastUpdate.TotalMilliseconds > 3000 ||
                    processedCount == totalFiles) // Every 3 seconds or at completion
                {
                    UpdateProgress(progress, $"Processing files {processedCount}/{totalFiles}...");
                    lastUiUpdate = DateTime.Now;

                    // Minimal yield to UI thread for responsiveness
                    if (processedCount % 25 == 0) await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to process file {fileInfo.Name}: {ex.Message}");
            }
        }

        UpdateProgress(95, "Verifying translation results...");

        // Step 5: Verify if enabled
        if (verifyIntegrity) await VerifyTranslation(_selectedSavePath, gitMcPath, cancellationToken);

        UpdateProgress(100, "Translate complete!");

        // Log final statistics
        LogMessage("=== Save translation process complete ===");
        LogMessage("\ud83d\udcc8 Processing Statistics:");
        LogMessage($"  ✓ Total files processed: {processedCount}");
        LogMessage($"  📦 Multi-chunk files: {multiChunkFiles}");
        LogMessage($"  🧊 Total chunks processed: {totalChunksProcessed}");
        LogMessage($"Output folder: {gitMcPath}");
    }

    private async Task<List<FileInfo>> ScanForFiles(string savePath, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
        var directory = new DirectoryInfo(savePath);

        bool processRegionFiles = RegionFilesCheckBox.IsChecked == true;
        bool processDataFiles = DataFilesCheckBox.IsChecked == true;
        bool processNbtFiles = NbtFilesCheckBox.IsChecked == true;
        bool processLevelData = LevelDataCheckBox.IsChecked == true;

        await Task.Run(() =>
        {
            if (processRegionFiles)
            {
                ScanDirectory(directory, "*.mca", files, cancellationToken);
                ScanDirectory(directory, "*.mcc", files, cancellationToken);
            }

            if (processDataFiles) ScanDirectory(directory, "*.dat", files, cancellationToken);

            if (processNbtFiles) ScanDirectory(directory, "*.nbt", files, cancellationToken);

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

    private void ScanDirectory(DirectoryInfo directory, string pattern, List<FileInfo> files,
        CancellationToken cancellationToken)
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
        foreach (FileInfo file in source.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            file.CopyTo(Path.Combine(target.FullName, file.Name), true);
        }

        // Copy subdirectories
        foreach (DirectoryInfo subDir in source.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // If the directory to be copied is GitMC directory, skip it.
            // When a folder called GitMC is copied, block it:
            if (subDir.Name.Equals("GitMC", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(subDir, target.CreateSubdirectory(subDir.Name), cancellationToken);
        }
    }

    private async Task ProcessFile(string filePath, string extension, CancellationToken cancellationToken)
    {
        try
        {
            string tempSnbtPath = filePath + ".snbt";
            string? backupPath = CreateBackupCheckBox.IsChecked == true ? filePath + ".backup" : null;

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
                LogMessage(
                    $"  ⚠ Skipping large file: {Path.GetFileName(filePath)} ({fileInfo.Length / 1024 / 1024}MB)");
                return;
            }

            // Log file details for better tracking
            LogMessage(
                $"  Processing: {Path.GetFileName(filePath)} ({fileInfo.Length / 1024}KB, {extension.ToUpper()})");

            // Create backup if requested
            if (backupPath != null)
                try
                {
                    File.Copy(filePath, backupPath, true);
                    LogMessage($"  ✓ Backup created: {Path.GetFileName(backupPath)}");
                }
                catch (Exception ex)
                {
                    LogMessage($"  ⚠ Backup failed for {Path.GetFileName(filePath)}: {ex.Message}");
                }

            // Convert to SNBT with timeout protection and enhanced logging
            LogMessage($"  🔄 Converting to SNBT: {Path.GetFileName(filePath)}");

            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5))) // 5-minute timeout per file
            using (var combinedCts =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                await ConvertToSnbt(filePath, tempSnbtPath, extension, combinedCts.Token);

                // Log SNBT file info for verification
                if (File.Exists(tempSnbtPath))
                {
                    long snbtSize = new FileInfo(tempSnbtPath).Length;
                    LogMessage($"  ✓ SNBT created: {snbtSize / 1024}KB");

                    // For MCA files, check if multi-chunk was detected
                    if (extension == ".mca" || extension == ".mcc")
                    {
                        string snbtContent = await File.ReadAllTextAsync(tempSnbtPath, cancellationToken);
                        if (snbtContent.Contains("# Chunk"))
                        {
                            // Optimized: Count occurrences without Split to avoid massive memory allocations
                            int chunkCount = CommonHelpers.CountOccurrences(snbtContent.AsSpan(), "# Chunk".AsSpan());
                            if (chunkCount > 1) LogMessage($"  📦 Multi-chunk file detected: {chunkCount} chunks");
                        }
                    }
                }

                // Convert back from SNBT
                LogMessage($"  🔄 Converting back from SNBT: {Path.GetFileName(filePath)}");
                await ConvertFromSnbt(tempSnbtPath, filePath, extension, combinedCts.Token);

                // Verify final result
                long finalSize = new FileInfo(filePath).Length;
                LogMessage($"  ✓ Final file: {finalSize / 1024}KB");
            }

            // Clean up SNBT file if not preserving
            if (PreserveSNBTCheckBox.IsChecked != true && File.Exists(tempSnbtPath))
                try
                {
                    File.Delete(tempSnbtPath);
                }
                catch (Exception ex)
                {
                    LogMessage($"  ⚠ Failed to delete temp SNBT: {ex.Message}");
                }
            else if (File.Exists(tempSnbtPath)) LogMessage($"  📁 SNBT preserved: {Path.GetFileName(tempSnbtPath)}");

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

    private async Task<(bool IsMultiChunk, int ChunkCount)> ProcessFileWithTracking(string filePath, string extension,
        CancellationToken cancellationToken)
    {
        bool isMultiChunk = false;
        int chunkCount = 0;

        try
        {
            string tempSnbtPath = filePath + ".snbt";
            string? backupPath = CreateBackupCheckBox.IsChecked == true ? filePath + ".backup" : null;

            // Check if file exists and is accessible
            if (!File.Exists(filePath)) return (false, 0);

            // Check file size and skip very large files that might cause issues
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 50 * 1024 * 1024) // 50MB limit
            {
                LogMessage(
                    $"  ⚠ Skipping large file: {Path.GetFileName(filePath)} ({fileInfo.Length / 1024 / 1024}MB)");
                return (false, 0);
            }

            // Create backup if requested
            if (backupPath != null)
                try
                {
                    File.Copy(filePath, backupPath, true);
                }
                catch (Exception ex)
                {
                    LogMessage($"  ⚠ Backup failed for {Path.GetFileName(filePath)}: {ex.Message}");
                }

            // Convert to SNBT with timeout protection
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5))) // 5-minute timeout per file
            using (var combinedCts =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                await ConvertToSnbt(filePath, tempSnbtPath, extension, combinedCts.Token);

                // Optimized multi-chunk detection: only read file once and use efficient string operations
                if (File.Exists(tempSnbtPath))
                    // For MCA files, efficiently check chunk count without extensive string splitting
                    if (extension == ".mca" || extension == ".mcc")
                        // Use async file reading with optimized chunk detection
                        using (var fileStream = new FileStream(tempSnbtPath, FileMode.Open, FileAccess.Read))
                        using (var reader = new StreamReader(fileStream, Encoding.UTF8, bufferSize: 8192))
                        {
                            char[] firstKiloBytes = new char[1024];
                            int charsRead = await reader.ReadAsync(firstKiloBytes, 0, 1024);
                            string headerText = new(firstKiloBytes, 0, charsRead);

                            // Look for total chunks in header (much faster than parsing entire file)
                            if (headerText.Contains("// Total chunks:"))
                            {
                                Match match = Regex.Match(headerText, @"// Total chunks:\s*(\d+)");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out chunkCount))
                                    isMultiChunk = chunkCount > 1;
                            }
                        }

                // Convert back from SNBT
                await ConvertFromSnbt(tempSnbtPath, filePath, extension, combinedCts.Token);
            }

            // Clean up SNBT file if not preserving
            if (PreserveSNBTCheckBox.IsChecked != true && File.Exists(tempSnbtPath))
                try
                {
                    File.Delete(tempSnbtPath);
                }
                catch (Exception)
                {
                    // Silently ignore cleanup errors
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

    private async Task ConvertToSnbt(string inputPath, string outputPath, string extension,
        CancellationToken cancellationToken)
    {
        try
        {
            // Add file validation before conversion
            if (!File.Exists(inputPath)) throw new FileNotFoundException($"Input file not found: {inputPath}");

            var fileInfo = new FileInfo(inputPath);
            if (fileInfo.Length == 0) throw new InvalidDataException($"Input file is empty: {inputPath}");

            // Create minimal progress reporter to reduce UI overhead
            var progress = new Progress<string>(message =>
            {
                // Only log critical conversion events, not detailed chunk processing
                if (message.Contains("error") || message.Contains("failed") || message.Contains("ERROR"))
                    LogMessage($"    {message}");
            });

            // Check if this is an MCA file and chunk-based mode is enabled
            bool isChunkBasedMode = ChunkBasedMcaRadioButton?.IsChecked == true;

            if ((extension == ".mca" || extension == ".mcc") && isChunkBasedMode)
            {
                // Use chunk-based processing
                string chunkFolderPath = Path.ChangeExtension(outputPath, ".chunks");
                await _nbtService.ConvertMcaToChunkFilesAsync(inputPath, chunkFolderPath, progress);

                // Create a marker file to indicate this is chunk-based output
                string markerPath = outputPath + ".chunk_mode";
                await File.WriteAllTextAsync(markerPath, chunkFolderPath, Encoding.UTF8, cancellationToken);

                LogMessage($"    ✓ Created {Directory.GetFiles(chunkFolderPath, "chunk_*.snbt").Length} chunk files");
            }
            else
            {
                // Use standard single-file conversion
                await _nbtService.ConvertToSnbtAsync(inputPath, outputPath, progress);

                // Verify output was created
                if (!File.Exists(outputPath))
                    throw new InvalidOperationException($"SNBT output file was not created: {outputPath}");
            }
        }
        catch (Exception ex)
        {
            // Enhanced error reporting
            string errorMessage = $"ConvertToSnbt failed for {Path.GetFileName(inputPath)}: {ex.Message}";
            if (ex.InnerException != null) errorMessage += $" Inner: {ex.InnerException.Message}";
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    private async Task ConvertFromSnbt(string inputPath, string outputPath, string extension,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create minimal progress reporter to reduce UI overhead
            var progress = new Progress<string>(message =>
            {
                // Only log critical conversion events, not detailed chunk processing
                if (message.Contains("error") || message.Contains("failed") || message.Contains("ERROR"))
                    LogMessage($"    {message}");
            });

            // Check if this was processed in chunk-based mode
            string markerPath = inputPath + ".chunk_mode";
            if (File.Exists(markerPath) && (extension == ".mca" || extension == ".mcc"))
            {
                // Read chunk folder path from marker file
                string chunkFolderPath = await File.ReadAllTextAsync(markerPath, Encoding.UTF8, cancellationToken);

                if (Directory.Exists(chunkFolderPath))
                {
                    // Convert chunk files back to MCA
                    await _nbtService.ConvertChunkFilesToMcaAsync(chunkFolderPath, outputPath, progress);

                    // Clean up marker file
                    File.Delete(markerPath);

                    LogMessage($"    ✓ Reconstructed MCA from {Directory.GetFiles(chunkFolderPath, "chunk_*.snbt").Length} chunk files");
                }
                else
                {
                    throw new DirectoryNotFoundException($"Chunk folder not found: {chunkFolderPath}");
                }
            }
            else
            {
                // Standard single-file conversion
                // Add file validation before conversion
                if (!File.Exists(inputPath)) throw new FileNotFoundException($"SNBT input file not found: {inputPath}");

                var fileInfo = new FileInfo(inputPath);
                if (fileInfo.Length == 0) throw new InvalidDataException($"SNBT input file is empty: {inputPath}");

                // Use the enhanced async NbtService with minimal progress reporting
                await _nbtService.ConvertFromSnbtAsync(inputPath, outputPath, progress);
            }

            // Verify output was created
            if (!File.Exists(outputPath))
                throw new InvalidOperationException($"Output file was not created: {outputPath}");
        }
        catch (Exception ex)
        {
            // Enhanced error reporting
            string errorMessage = $"ConvertFromSnbt failed for {Path.GetFileName(inputPath)}: {ex.Message}";
            if (ex.InnerException != null) errorMessage += $" Inner: {ex.InnerException.Message}";
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    private async Task VerifyTranslation(string originalPath, string translatedPath,
        CancellationToken cancellationToken)
    {
        LogMessage("Validating file integrity...");

        await Task.Run(() =>
        {
            string[] originalFiles = Directory.GetFiles(originalPath, "*", SearchOption.AllDirectories);
            int verifiedCount = 0;
            int totalCount = originalFiles.Length;

            foreach (string originalFile in originalFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath = Path.GetRelativePath(originalPath, originalFile);
                string translatedFile = Path.Combine(translatedPath, relativePath);

                if (File.Exists(translatedFile))
                {
                    // For NBT files, we can do more sophisticated comparison
                    // For now, just check file sizes are reasonable
                    long originalSize = new FileInfo(originalFile).Length;
                    long translatedSize = new FileInfo(translatedFile).Length;

                    double sizeDifference = Math.Abs(originalSize - translatedSize) / (double)originalSize;
                    if (sizeDifference > 0.1) // Allow 10% size difference
                        LogMessage(
                            $"⚠ File size difference is large: {relativePath} (Original: {originalSize}, Translated: {translatedSize})");
                }
                else
                {
                    LogMessage($"⚠ Missing translated file: {relativePath}");
                }

                verifiedCount++;
                if (verifiedCount % 100 == 0) LogMessage($"Validated {verifiedCount}/{totalCount} files");
            }

            LogMessage($"Validation complete: {verifiedCount}/{totalCount} files");
        }, cancellationToken);
    }

    private void UpdateProgress(int value, string text)
    {
        // Use synchronous Enqueue instead of TryEnqueue to ensure UI updates are processed
        try
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                if (OverallProgressBar != null) OverallProgressBar.Value = value;
                if (ProgressTextBlock != null) ProgressTextBlock.Text = text;
            });
        }
        catch (Exception ex)
        {
            // Fallback - ignore UI update errors during debug
            Debug.WriteLine($"UI Update Error: {ex.Message}");
        }
    }

    private void LogMessage(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string logEntry = $"[{timestamp}] {message}";

        try
        {
            // Add to queue instead of immediate UI update
            lock (_logLock)
            {
                _logQueue.Enqueue(logEntry);

                // Limit queue size to prevent memory buildup
                while (_logQueue.Count > 1000) _logQueue.Dequeue();
            }

            // Schedule batch flush every 500ms or for critical messages
            bool isCritical = message.Contains("Error") || message.Contains("Failed") || message.Contains("❌") ||
                              message.Contains("===");
            TimeSpan timeSinceLastFlush = DateTime.Now - _lastLogFlush;

            if (isCritical || timeSinceLastFlush.TotalMilliseconds > 500)
                FlushLogQueue();
            else if (!_isLogFlushScheduled) ScheduleLogFlush();
        }
        catch (Exception ex)
        {
            // Fallback - output to debug console
            Debug.WriteLine($"Log Error: {ex.Message} - Message: {logEntry}");
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

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                try
                {
                    if (LogTextBox != null)
                    {
                        var sb = new StringBuilder();

                        // Check if we need to trim existing content
                        int currentLength = LogTextBox.Text.Length;
                        int newContentLength = logsToFlush.Sum(log => log.Length + 1); // +1 for newline

                        if (currentLength + newContentLength > 80000) // ~80KB limit
                        {
                            // Keep only the last portion of current text
                            // Optimized: Use span-based approach to avoid Split allocation
                            string? textContent = LogTextBox.Text;
                            var lines = new List<string>();

                            ReadOnlySpan<char> contentSpan = textContent.AsSpan();
                            ReadOnlySpan<char> remaining = contentSpan;

                            while (!remaining.IsEmpty)
                            {
                                int lineEnd = remaining.IndexOf('\n');
                                ReadOnlySpan<char> line;

                                if (lineEnd >= 0)
                                {
                                    line = remaining[..lineEnd];
                                    remaining = remaining[(lineEnd + 1)..];
                                }
                                else
                                {
                                    line = remaining;
                                    remaining = ReadOnlySpan<char>.Empty;
                                }

                                lines.Add(line.ToString());
                            }

                            IEnumerable<string>
                                keepLines = lines.Skip(Math.Max(0, lines.Count - 200)); // Keep last 200 lines
                            sb.AppendLine(string.Join('\n', keepLines));
                            sb.AppendLine("... [Earlier logs truncated for performance] ...");
                        }
                        else
                        {
                            sb.Append(LogTextBox.Text);
                        }

                        // Add new logs
                        foreach (string log in logsToFlush) sb.AppendLine(log);

                        LogTextBox.Text = sb.ToString();

                        // Always auto-scroll to bottom for better UX
                        if (LogTextBox.Parent is ScrollViewer scrollViewer)
                            scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, false);
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
            Debug.WriteLine($"FlushLogQueue Error: {ex.Message}");
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

    #region Reverse Translation Event Handlers

    private async void BrowseGitMcButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            folderPicker.FileTypeFilter.Add("*");

            // Get the current window's handle
            Window? window = App.MainWindow;
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                _selectedGitMcPath = folder.Path;
                GitMcPathTextBox.Text = _selectedGitMcPath;

                await ValidateGitMcFolder(_selectedGitMcPath).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in BrowseGitMcButton_Click: {ex.Message}");
            DispatcherQueue.TryEnqueue(() =>
            {
                GitMcValidationText.Text = $"Error selecting folder: {ex.Message}";
                GitMcValidationText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            });
        }
    }

    private async Task ValidateGitMcFolder(string folderPath)
    {
        try
        {
            // Check if it's a valid GitMC folder structure
            var regionFolder = Path.Combine(folderPath, "region");

            bool hasRegionFolder = Directory.Exists(regionFolder);
            bool hasSnbtFiles = false;

            if (hasRegionFolder)
            {
                hasSnbtFiles = Directory.GetFiles(regionFolder, "*.snbt", SearchOption.AllDirectories).Length > 0;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (hasRegionFolder && hasSnbtFiles)
                {
                    GitMcValidationText.Text = "✓ Valid GitMC folder detected";
                    GitMcValidationText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                    GitMcValidationText.Visibility = Visibility.Visible;
                    StartReverseTranslationButton.IsEnabled = true;
                }
                else
                {
                    var issues = new List<string>();
                    if (!hasRegionFolder) issues.Add("No region folder found");
                    if (!hasSnbtFiles) issues.Add("No SNBT files found");

                    GitMcValidationText.Text = $"✗ Invalid GitMC folder: {string.Join(", ", issues)}";
                    GitMcValidationText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    GitMcValidationText.Visibility = Visibility.Visible;
                    StartReverseTranslationButton.IsEnabled = false;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error validating GitMC folder: {ex.Message}");
            DispatcherQueue.TryEnqueue(() =>
            {
                GitMcValidationText.Text = $"Validation error: {ex.Message}";
                GitMcValidationText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                GitMcValidationText.Visibility = Visibility.Visible;
                StartReverseTranslationButton.IsEnabled = false;
            });
        }
    }

    private async void StartReverseTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedGitMcPath))
        {
            return;
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            // Disable UI elements
            DispatcherQueue.TryEnqueue(() =>
            {
                StartReverseTranslationButton.IsEnabled = false;
                BrowseGitMcButton.IsEnabled = false;
                ReverseTranslationProgressBar.Visibility = Visibility.Visible;
                ReverseTranslationProgressText.Visibility = Visibility.Visible;
                ReverseTranslationProgressText.Text = "Preparing reverse translation...";
            });

            // Create output folder
            var outputFolder = Path.Combine(Path.GetDirectoryName(_selectedGitMcPath)!,
                Path.GetFileName(_selectedGitMcPath) + "_restored");

            if (Directory.Exists(outputFolder))
            {
                Directory.Delete(outputFolder, true);
            }
            Directory.CreateDirectory(outputFolder);

            LogMessage($"Starting reverse translation from: {_selectedGitMcPath}");
            LogMessage($"Output folder: {outputFolder}");

            await PerformReverseTranslation(_selectedGitMcPath, outputFolder, cancellationToken);

            DispatcherQueue.TryEnqueue(() =>
            {
                ReverseTranslationProgressText.Text = "✓ Reverse translation completed successfully!";
                ReverseTranslationProgressText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
            });

            LogMessage($"Reverse translation completed. Restored save available at: {outputFolder}");
        }
        catch (OperationCanceledException)
        {
            LogMessage("Reverse translation was cancelled.");
            DispatcherQueue.TryEnqueue(() =>
            {
                ReverseTranslationProgressText.Text = "Reverse translation cancelled.";
                ReverseTranslationProgressText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in reverse translation: {ex.Message}");
            LogMessage($"Error during reverse translation: {ex.Message}");
            DispatcherQueue.TryEnqueue(() =>
            {
                ReverseTranslationProgressText.Text = $"✗ Error: {ex.Message}";
                ReverseTranslationProgressText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            });
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StartReverseTranslationButton.IsEnabled = true;
                BrowseGitMcButton.IsEnabled = true;
                ReverseTranslationProgressBar.Visibility = Visibility.Collapsed;
            });
        }
    }

    private async Task PerformReverseTranslation(string gitMcPath, string outputPath, CancellationToken cancellationToken)
    {
        var regionFolder = Path.Combine(gitMcPath, "region");
        var outputRegionFolder = Path.Combine(outputPath, "region");
        Directory.CreateDirectory(outputRegionFolder);

        // Copy non-region files first
        await CopyNonRegionFiles(gitMcPath, outputPath, cancellationToken);

        // Get all SNBT files and chunk marker files in the region folder
        var snbtFiles = Directory.GetFiles(regionFolder, "*.snbt", SearchOption.AllDirectories);
        var chunkMarkerFiles = Directory.GetFiles(regionFolder, "*.chunk_mode", SearchOption.AllDirectories);

        var totalOperations = snbtFiles.Length + chunkMarkerFiles.Length;
        var processedOperations = 0;

        LogMessage($"Found {snbtFiles.Length} SNBT files and {chunkMarkerFiles.Length} chunk-based MCA files to convert");

        // First, handle chunk-based MCA files with parallel processing for better performance
        if (chunkMarkerFiles.Length > 0)
        {
            LogMessage($"Processing {chunkMarkerFiles.Length} chunk-based MCA files in parallel...");

            var chunkProcessingTasks = chunkMarkerFiles.Select(async markerFile =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Read chunk folder path from marker file
                    string chunkFolderPath = await File.ReadAllTextAsync(markerFile, Encoding.UTF8, cancellationToken);

                    if (Directory.Exists(chunkFolderPath))
                    {
                        // Determine output MCA file path
                        // marker file is like "r.0.0.mca.snbt.chunk_mode", we want "r.0.0.mca"
                        string snbtPath = markerFile.Replace(".chunk_mode", "");
                        string originalFileName = Path.GetFileName(snbtPath);
                        if (originalFileName.EndsWith(".snbt"))
                        {
                            originalFileName = originalFileName.Substring(0, originalFileName.Length - 5); // Remove .snbt
                        }

                        string outputMcaPath = Path.Combine(outputRegionFolder, originalFileName);

                        // Create minimal progress reporter to reduce overhead during parallel processing
                        var progress = new Progress<string>(message =>
                        {
                            // Only log critical errors to avoid overwhelming the UI during parallel processing
                            if (message.Contains("ERROR") || message.Contains("FAILED"))
                                LogMessage($"    {message}");
                        });

                        // Convert chunk files back to MCA
                        await _nbtService.ConvertChunkFilesToMcaAsync(chunkFolderPath, outputMcaPath, progress);

                        // Delete source files after successful conversion to match standard Minecraft save structure
                        // Only delete if the output file was successfully created
                        if (File.Exists(outputMcaPath))
                        {
                            try
                            {
                                // Delete chunk folder (contains individual chunk SNBT files)
                                if (Directory.Exists(chunkFolderPath))
                                {
                                    Directory.Delete(chunkFolderPath, true);
                                }

                                // Delete marker file
                                if (File.Exists(markerFile))
                                {
                                    File.Delete(markerFile);
                                }

                                // Delete SNBT file if it exists
                                if (File.Exists(snbtPath))
                                {
                                    File.Delete(snbtPath);
                                }
                            }
                            catch (Exception cleanupEx)
                            {
                                LogMessage($"    ⚠ Warning: Failed to clean up source files for {originalFileName}: {cleanupEx.Message}");
                            }
                        }

                        // Log completion for this file
                        int chunkCount = 0;
                        try
                        {
                            chunkCount = Directory.Exists(chunkFolderPath) ? 0 :
                                        Directory.GetFiles(Path.GetDirectoryName(chunkFolderPath) ?? "", "chunk_*.snbt").Length;
                        }
                        catch { /* Ignore if we can't count */ }

                        LogMessage($"    ✓ Reconstructed MCA: {originalFileName}");
                    }
                    else
                    {
                        LogMessage($"    ⚠ Warning: Chunk folder not found for {markerFile}: {chunkFolderPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"    ✗ Error processing chunk-based MCA {markerFile}: {ex.Message}");
                }

                // Update progress less frequently to reduce UI overhead
                var completed = Interlocked.Increment(ref processedOperations);
                if (completed % Math.Max(1, chunkMarkerFiles.Length / 5) == 0 || completed == chunkMarkerFiles.Length)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ReverseTranslationProgressText.Text = $"Converting chunk files... ({completed}/{totalOperations})";
                        ReverseTranslationProgressBar.Value = (double)completed / totalOperations * 100;
                    });
                }
            });

            // Process chunk-based MCA files with controlled concurrency to avoid overwhelming the system
            var maxConcurrency = Math.Min(Environment.ProcessorCount, Math.Max(1, chunkMarkerFiles.Length));
            var chunkSemaphore = new SemaphoreSlim(maxConcurrency);
            var chunkConcurrentTasks = chunkProcessingTasks.Select(async task =>
            {
                await chunkSemaphore.WaitAsync(cancellationToken);
                try
                {
                    await task;
                }
                finally
                {
                    chunkSemaphore.Release();
                }
            });

            await Task.WhenAll(chunkConcurrentTasks);
            LogMessage($"✓ Completed {chunkMarkerFiles.Length} chunk-based MCA files");
        }

        // Then handle regular SNBT files (excluding those already processed as chunk-based) with optimized parallel processing
        var remainingSnbtFiles = snbtFiles.Where(snbtFile =>
        {
            string markerPath = snbtFile + ".chunk_mode";
            return !File.Exists(markerPath); // Only process if no chunk marker exists
        }).ToArray();

        if (remainingSnbtFiles.Length > 0)
        {
            LogMessage($"Processing {remainingSnbtFiles.Length} regular SNBT files in parallel...");

            // Optimize parallelism based on file count and system capabilities
            var maxDegreeOfParallelism = Math.Min(
                Environment.ProcessorCount * 2, // Allow some over-subscription for I/O bound operations
                Math.Max(1, remainingSnbtFiles.Length / 2) // Don't over-parallelize for small file counts
            );

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };

            // Use concurrent collection for thread-safe progress tracking
            var completedFiles = 0;
            var lockObject = new object();

            await Task.Run(() =>
            {
                Parallel.ForEach(remainingSnbtFiles, parallelOptions, snbtFile =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Calculate relative path and output path
                        var relativePath = Path.GetRelativePath(regionFolder, snbtFile);
                        var outputFile = Path.Combine(outputRegionFolder, relativePath);

                        // Handle proper extension removal: .dat.snbt -> .dat, .nbt.snbt -> .nbt, .mca.snbt -> .mca
                        if (outputFile.EndsWith(".snbt"))
                        {
                            outputFile = outputFile.Substring(0, outputFile.Length - 5); // Remove .snbt extension
                        }

                        // Ensure output directory exists (thread-safe)
                        var outputDir = Path.GetDirectoryName(outputFile);
                        if (!string.IsNullOrEmpty(outputDir))
                        {
                            lock (lockObject)
                            {
                                Directory.CreateDirectory(outputDir);
                            }
                        }

                        // Convert SNBT back to original format
                        _nbtService.ConvertFromSnbt(snbtFile, outputFile);

                        // Delete source SNBT file after successful conversion to match standard Minecraft save structure
                        // Only delete if the output file was successfully created
                        if (File.Exists(outputFile))
                        {
                            try
                            {
                                File.Delete(snbtFile);
                            }
                            catch (Exception cleanupEx)
                            {
                                LogMessage($"    ⚠ Warning: Failed to delete SNBT file {Path.GetFileName(snbtFile)}: {cleanupEx.Message}");
                            }
                        }

                        // Thread-safe progress tracking with reduced UI update frequency
                        var currentCompleted = Interlocked.Increment(ref completedFiles);
                        var totalCompleted = Interlocked.Increment(ref processedOperations);

                        // Update UI less frequently to improve performance (every 10% or every 50 files, whichever is less)
                        var updateInterval = Math.Min(Math.Max(1, remainingSnbtFiles.Length / 10), 50);
                        if (currentCompleted % updateInterval == 0 || currentCompleted == remainingSnbtFiles.Length)
                        {
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                ReverseTranslationProgressText.Text = $"Converting regular files... ({totalCompleted}/{totalOperations})";
                                ReverseTranslationProgressBar.Value = (double)totalCompleted / totalOperations * 100;
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"    ✗ Error converting {Path.GetFileName(snbtFile)}: {ex.Message}");

                        // Still increment progress even on error to keep UI consistent
                        Interlocked.Increment(ref processedOperations);
                    }
                });
            }, cancellationToken);

            LogMessage($"✓ Completed {remainingSnbtFiles.Length} regular SNBT files");
        }

        LogMessage($"✅ Reverse translation completed. Processed {processedOperations} operations.");
        LogMessage($"   - Converted {chunkMarkerFiles.Length} chunk-based MCA files");
        LogMessage($"   - Converted {remainingSnbtFiles.Length} regular SNBT files");
    }

    private async Task CopyNonRegionFiles(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        var allEntries = Directory.GetFileSystemEntries(sourcePath, "*", SearchOption.TopDirectoryOnly);

        foreach (var entry in allEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryName = Path.GetFileName(entry);

            // Skip region folder (we handle it separately) and git folder
            if (entryName.Equals("region", StringComparison.OrdinalIgnoreCase) ||
                entryName.Equals(".git", StringComparison.OrdinalIgnoreCase))
                continue;

            var outputEntry = Path.Combine(outputPath, entryName);

            if (Directory.Exists(entry))
            {
                // Copy directory recursively
                await Task.Run(() => CopyDirectory(entry, outputEntry), cancellationToken);
            }
            else
            {
                // Copy file
                File.Copy(entry, outputEntry, true);
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetDirectoryName(subDir)!);
            CopyDirectory(subDir, destSubDir);
        }
    }

    #endregion
}
