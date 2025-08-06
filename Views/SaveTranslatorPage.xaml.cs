using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Storage.Pickers;
using GitMC.Services;
using GitMC.Utils;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
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
    private string? _selectedGitMcPath;
    private string? _selectedSavePath;

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
            var cpuUsage = _cpuCounter?.NextValue() ?? 0;
            CpuUsageBar.Value = Math.Min(cpuUsage, 100);

            var availableMemory = _memoryCounter?.NextValue() ?? 0;
            var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // Convert to MB
            var memoryUsage = Math.Max(0, 100 - availableMemory / 16 * 100); // Assuming 16GB total
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
            var window = App.MainWindow;
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
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
                var levelDatPath = Path.Combine(path, "level.dat");
                var levelDatOldPath = Path.Combine(path, "level.dat_old");

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
            var gitMcPath = Path.Combine(_selectedSavePath, "GitMC");
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

        // Process files in batches on background threads to avoid UI blocking
        const int batchSize = 5; // Process 5 files per batch
        for (var batchStart = 0; batchStart < filesToProcess.Count; batchStart += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Get current batch
            var batch = filesToProcess.Skip(batchStart).Take(batchSize).ToList();

            // Process batch on background thread
            var batchResults =
                await Task.Run(async () =>
                {
                    var results =
                        new List<(bool IsMultiChunk, int ChunkCount, string fileName, bool success, string? error)>();

                    foreach (var fileInfo in batch)
                        try
                        {
                            var relativePath = Path.GetRelativePath(_selectedSavePath, fileInfo.FullName);
                            var targetPath = Path.Combine(gitMcPath, relativePath);

                            // Track multi-chunk processing
                            var isMultiChunk =
                                await ProcessFileWithTracking(targetPath, fileInfo.Extension, cancellationToken);

                            results.Add((isMultiChunk.IsMultiChunk, isMultiChunk.ChunkCount, fileInfo.Name, true,
                                null));
                        }
                        catch (Exception ex)
                        {
                            results.Add((false, 0, fileInfo.Name, false, ex.Message));
                        }

                    return results;
                }, cancellationToken);

            // Update counters and UI on main thread
            foreach (var result in
                     batchResults)
            {
                processedCount++;

                if (result.IsMultiChunk)
                {
                    multiChunkFiles++;
                    totalChunksProcessed += result.ChunkCount;
                }

                if (!result.success && result.error != null)
                    LogMessage($"Failed to process file {result.fileName}: {result.error}");
            }

            // Log batch progress less frequently
            if (batchStart / batchSize % 10 == 0 || processedCount >= totalFiles)
                LogMessage($"Processing batch: {processedCount}/{totalFiles} files completed");

            var progress = 30 + processedCount * 60 / totalFiles;

            // Update UI much less frequently and with time-based throttling
            var timeSinceLastUpdate = DateTime.Now - lastUiUpdate;
            if (timeSinceLastUpdate.TotalMilliseconds > 2000 ||
                processedCount >= totalFiles) // Every 2 seconds or at completion
            {
                UpdateProgress(progress, $"Processing files {processedCount}/{totalFiles}...");
                lastUiUpdate = DateTime.Now;
            }

            // Always yield control to UI thread after each batch
            await Task.Delay(50, cancellationToken); // Small delay to ensure UI responsiveness
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
        await Task.Run(
            () => { CopyDirectory(new DirectoryInfo(sourcePath), new DirectoryInfo(targetPath), cancellationToken); },
            cancellationToken);

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
            if (subDir.Name.Equals("GitMC", StringComparison.OrdinalIgnoreCase)) continue;
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
                    var snbtSize = new FileInfo(tempSnbtPath).Length;
                    LogMessage($"  ✓ SNBT created: {snbtSize / 1024}KB");

                    // For MCA files, check if multi-chunk was detected
                    if (extension == ".mca" || extension == ".mcc")
                    {
                        var snbtContent = await File.ReadAllTextAsync(tempSnbtPath, cancellationToken);
                        if (snbtContent.Contains("# Chunk"))
                        {
                            // Optimized: Count occurrences without Split to avoid massive memory allocations
                            var chunkCount = CommonHelpers.CountOccurrences(snbtContent.AsSpan(), "# Chunk".AsSpan());
                            if (chunkCount > 1) LogMessage($"  📦 Multi-chunk file detected: {chunkCount} chunks");
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
        var isMultiChunk = false;
        var chunkCount = 0;

        try
        {
            var tempSnbtPath = filePath + ".snbt";
            var backupPath = CreateBackupCheckBox.IsChecked == true ? filePath + ".backup" : null;

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
                            var firstKiloBytes = new char[1024];
                            var charsRead = await reader.ReadAsync(firstKiloBytes, 0, 1024);
                            string headerText = new(firstKiloBytes, 0, charsRead);

                            // Look for total chunks in header (much faster than parsing entire file)
                            if (headerText.Contains("// Total chunks:"))
                            {
                                var match = Regex.Match(headerText, @"// Total chunks:\s*(\d+)");
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
            var isChunkBasedMode = ChunkBasedMcaRadioButton?.IsChecked == true;

            if ((extension == ".mca" || extension == ".mcc") && isChunkBasedMode)
            {
                // Use chunk-based processing
                var chunkFolderPath = Path.ChangeExtension(outputPath, ".chunks");
                await _nbtService.ConvertMcaToChunkFilesAsync(inputPath, chunkFolderPath, progress);

                // Create a marker file to indicate this is chunk-based output
                var markerPath = outputPath + ".chunk_mode";
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
            var errorMessage = $"ConvertToSnbt failed for {Path.GetFileName(inputPath)}: {ex.Message}";
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
            var markerPath = inputPath + ".chunk_mode";
            if (File.Exists(markerPath) && (extension == ".mca" || extension == ".mcc"))
            {
                // Read chunk folder path from marker file
                var chunkFolderPath = await File.ReadAllTextAsync(markerPath, Encoding.UTF8, cancellationToken);

                if (Directory.Exists(chunkFolderPath))
                {
                    // Convert chunk files back to MCA
                    await _nbtService.ConvertChunkFilesToMcaAsync(chunkFolderPath, outputPath, progress);

                    // Clean up marker file
                    File.Delete(markerPath);

                    LogMessage(
                        $"    ✓ Reconstructed MCA from {Directory.GetFiles(chunkFolderPath, "chunk_*.snbt").Length} chunk files");
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
            var errorMessage = $"ConvertFromSnbt failed for {Path.GetFileName(inputPath)}: {ex.Message}";
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
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] {message}";

        try
        {
            // Add to queue instead of immediate UI update
            lock (_logLock)
            {
                _logQueue.Enqueue(logEntry);

                // Limit queue size to prevent memory buildup
                while (_logQueue.Count > 500) _logQueue.Dequeue(); // Reduced from 1000 to 500
            }

            // Schedule batch flush every 1000ms or for critical messages
            var isCritical = message.Contains("Error") || message.Contains("Failed") || message.Contains("❌") ||
                             message.Contains("===");
            var timeSinceLastFlush = DateTime.Now - _lastLogFlush;

            if (isCritical || timeSinceLastFlush.TotalMilliseconds > 1000) // Increased from 500ms to 1000ms
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

        _ = Task.Delay(1000).ContinueWith(_ => // Increased from 500ms to 1000ms
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
                        var currentLength = LogTextBox.Text.Length;
                        var newContentLength = logsToFlush.Sum(log => log.Length + 1); // +1 for newline

                        if (currentLength + newContentLength > 60000) // Reduced limit to 60KB
                        {
                            // Keep only the last portion of current text - more aggressive trimming
                            var existingLines = LogTextBox.Text.Split('\n');

                            if (existingLines.Length > 150) // Reduced from 200 to 150 lines
                            {
                                var keepLines = existingLines.Skip(existingLines.Length - 150);
                                sb.AppendLine(string.Join('\n', keepLines));
                                sb.AppendLine("... [Earlier logs truncated for performance] ...");
                            }
                            else
                            {
                                sb.Append(LogTextBox.Text);
                            }
                        }
                        else
                        {
                            sb.Append(LogTextBox.Text);
                        }

                        // Add new logs
                        foreach (var log in logsToFlush) sb.AppendLine(log);

                        LogTextBox.Text = sb.ToString();

                        // Only auto-scroll occasionally to reduce UI work - scroll less frequently
                        if (logsToFlush.Count > 10 ||
                            logsToFlush.Any(log => log.Contains("complete") || log.Contains("===")))
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
            var window = App.MainWindow;
            var hwnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
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
                GitMcValidationText.Foreground = new SolidColorBrush(Colors.Red);
            });
        }
    }

    private async Task ValidateGitMcFolder(string folderPath)
    {
        try
        {
            // Check if it's a valid GitMC folder structure
            var regionFolder = Path.Combine(folderPath, "region");

            var hasRegionFolder = Directory.Exists(regionFolder);
            var hasSnbtFiles = false;

            if (hasRegionFolder)
                hasSnbtFiles = Directory.GetFiles(regionFolder, "*.snbt", SearchOption.AllDirectories).Length > 0;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (hasRegionFolder && hasSnbtFiles)
                {
                    GitMcValidationText.Text = "✓ Valid GitMC folder detected";
                    GitMcValidationText.Foreground = new SolidColorBrush(Colors.Green);
                    GitMcValidationText.Visibility = Visibility.Visible;
                    StartReverseTranslationButton.IsEnabled = true;
                }
                else
                {
                    var issues = new List<string>();
                    if (!hasRegionFolder) issues.Add("No region folder found");
                    if (!hasSnbtFiles) issues.Add("No SNBT files found");

                    GitMcValidationText.Text = $"✗ Invalid GitMC folder: {string.Join(", ", issues)}";
                    GitMcValidationText.Foreground = new SolidColorBrush(Colors.Red);
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
                GitMcValidationText.Foreground = new SolidColorBrush(Colors.Red);
                GitMcValidationText.Visibility = Visibility.Visible;
                StartReverseTranslationButton.IsEnabled = false;
            });
        }
    }

    private async void StartReverseTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedGitMcPath)) return;

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

            if (Directory.Exists(outputFolder)) Directory.Delete(outputFolder, true);
            Directory.CreateDirectory(outputFolder);

            LogMessage($"Starting reverse translation from: {_selectedGitMcPath}");
            LogMessage($"Output folder: {outputFolder}");

            await PerformReverseTranslation(_selectedGitMcPath, outputFolder, cancellationToken);

            DispatcherQueue.TryEnqueue(() =>
            {
                ReverseTranslationProgressText.Text = "✓ Reverse translation completed successfully!";
                ReverseTranslationProgressText.Foreground = new SolidColorBrush(Colors.Green);
            });

            LogMessage($"Reverse translation completed. Restored save available at: {outputFolder}");
        }
        catch (OperationCanceledException)
        {
            LogMessage("Reverse translation was cancelled.");
            DispatcherQueue.TryEnqueue(() =>
            {
                ReverseTranslationProgressText.Text = "Reverse translation cancelled.";
                ReverseTranslationProgressText.Foreground = new SolidColorBrush(Colors.Orange);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in reverse translation: {ex.Message}");
            LogMessage($"Error during reverse translation: {ex.Message}");
            DispatcherQueue.TryEnqueue(() =>
            {
                ReverseTranslationProgressText.Text = $"✗ Error: {ex.Message}";
                ReverseTranslationProgressText.Foreground = new SolidColorBrush(Colors.Red);
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

    private async Task PerformReverseTranslation(string gitMcPath, string outputPath,
        CancellationToken cancellationToken)
    {
        LogMessage("=== Starting reverse translation process ===");
        LogMessage($"Source GitMC folder: {gitMcPath}");
        LogMessage($"Output folder: {outputPath}");

        // Step 1: Copy all files from GitMC to GitMC_restored
        LogMessage("Step 1: Copying all files from GitMC folder...");
        await Task.Run(() => CopyDirectory(gitMcPath, outputPath), cancellationToken);
        LogMessage("✓ All files copied to output folder");

        // Step 2: Process MCA files in region, poi, and entities folders
        string[] mcaFolders = { "region", "poi", "entities" };
        var totalMcaProcessed = 0;

        foreach (var folderName in mcaFolders)
        {
            var sourceFolder = Path.Combine(outputPath, folderName);
            if (!Directory.Exists(sourceFolder))
            {
                LogMessage($"  Folder {folderName} does not exist, skipping...");
                continue;
            }

            LogMessage(
                $"Step 2.{Array.IndexOf(mcaFolders, folderName) + 1}: Processing MCA files in /{folderName} folder...");
            var processed = await ProcessMcaFilesInFolder(sourceFolder, cancellationToken);
            totalMcaProcessed += processed;
            LogMessage($"✓ Processed {processed} MCA files in /{folderName}");
        }

        // Step 3: Process other SNBT files (dat, nbt files)
        LogMessage("Step 3: Processing other SNBT files (dat, nbt, etc.)...");
        var otherSnbtProcessed = await ProcessOtherSnbtFiles(outputPath, cancellationToken);
        LogMessage($"✓ Processed {otherSnbtProcessed} other SNBT files");

        // Step 4: Clean up - remove all SNBT files and empty chunk folders
        LogMessage("Step 4: Cleaning up SNBT files and empty chunk folders...");
        var cleanedUp =
            await CleanupSnbtFilesAndChunkFolders(outputPath, cancellationToken);
        LogMessage($"✓ Cleaned up {cleanedUp.snbtFiles} SNBT files and {cleanedUp.chunkFolders} chunk folders");

        LogMessage("\u2705 Reverse translation completed successfully!");
        LogMessage($"   - Total MCA files reconstructed: {totalMcaProcessed}");
        LogMessage($"   - Other files converted: {otherSnbtProcessed}");
        LogMessage($"   - Files cleaned up: {cleanedUp.snbtFiles} SNBT files, {cleanedUp.chunkFolders} chunk folders");
        LogMessage("=== Reverse translation process complete ===");
    }

    private async Task<int> ProcessMcaFilesInFolder(string folderPath, CancellationToken cancellationToken)
    {
        var processedCount = 0;

        // Get all chunk marker files in this folder
        var chunkMarkerFiles = Directory.GetFiles(folderPath, "*.chunk_mode", SearchOption.AllDirectories);

        if (chunkMarkerFiles.Length == 0)
        {
            LogMessage($"  No chunk-based MCA files found in {Path.GetFileName(folderPath)}");
            return 0;
        }

        LogMessage($"  Found {chunkMarkerFiles.Length} chunk-based MCA files");

        foreach (var markerFile in chunkMarkerFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Read chunk folder path from marker file
                var chunkFolderPath = await File.ReadAllTextAsync(markerFile, Encoding.UTF8, cancellationToken);

                if (Directory.Exists(chunkFolderPath))
                {
                    // Determine output MCA file path
                    // marker file is like "r.0.0.mca.snbt.chunk_mode", we want "r.0.0.mca"
                    var snbtPath = markerFile.Replace(".chunk_mode", "");
                    var originalFileName = Path.GetFileName(snbtPath);
                    if (originalFileName.EndsWith(".snbt"))
                        originalFileName = originalFileName.Substring(0, originalFileName.Length - 5); // Remove .snbt

                    var outputMcaPath = Path.Combine(folderPath, originalFileName);

                    // Create progress reporter
                    var progress = new Progress<string>(message =>
                    {
                        if (message.Contains("ERROR") || message.Contains("FAILED"))
                            LogMessage($"    {message}");
                    });

                    // Convert chunk files back to MCA
                    LogMessage($"    Converting chunks to {originalFileName}...");
                    await _nbtService.ConvertChunkFilesToMcaAsync(chunkFolderPath, outputMcaPath, progress);

                    // Verify conversion succeeded
                    if (File.Exists(outputMcaPath))
                    {
                        var fileInfo = new FileInfo(outputMcaPath);
                        LogMessage($"    ✓ Created {originalFileName} ({fileInfo.Length / 1024}KB)");
                        processedCount++;

                        // Note: We don't delete source files here - cleanup happens in step 4
                    }
                    else
                    {
                        LogMessage($"    ✗ Failed to create {originalFileName}");
                    }
                }
                else
                {
                    LogMessage($"    ⚠ Warning: Chunk folder not found: {chunkFolderPath}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"    ✗ Error processing {Path.GetFileName(markerFile)}: {ex.Message}");
            }
        }

        return processedCount;
    }

    private async Task<int> ProcessOtherSnbtFiles(string outputPath, CancellationToken cancellationToken)
    {
        var processedCount = 0;

        // Find all SNBT files that are NOT part of chunk-based MCA conversion
        var allSnbtFiles = Directory.GetFiles(outputPath, "*.snbt", SearchOption.AllDirectories);

        var otherSnbtFiles = allSnbtFiles.Where(snbtFile =>
        {
            // Skip if this is part of a chunk-based conversion (has marker file)
            var markerPath = snbtFile + ".chunk_mode";
            if (File.Exists(markerPath))
                return false;

            // Skip if this file is inside a chunks folder
            var parentDir = Path.GetDirectoryName(snbtFile) ?? "";
            if (parentDir.EndsWith(".chunks", StringComparison.OrdinalIgnoreCase))
                return false;

            // Skip individual chunk files (chunk_X_Z.snbt pattern)
            var fileName = Path.GetFileName(snbtFile);
            if (fileName.StartsWith("chunk_") && fileName.EndsWith(".snbt"))
                return false;

            // Skip files that are originally .snbt format (not converted from other formats)
            // If the file name is "something.snbt" without any intermediate extension,
            // it means this file was originally a .snbt file and should be left untouched
            if (fileName.EndsWith(".snbt") && !fileName.Contains(".dat.snbt") &&
                !fileName.Contains(".nbt.snbt") && !fileName.Contains(".dat_old.snbt") &&
                !fileName.Contains(".mca.snbt") && !fileName.Contains(".mcc.snbt"))
            {
                // Check if it's a simple .snbt file (not .extension.snbt)
                var nameWithoutSnbt = fileName.Substring(0, fileName.Length - 5); // Remove .snbt
                if (!nameWithoutSnbt.Contains('.'))
                    // This is a pure .snbt file, not converted from another format
                    return false;
            }

            return true;
        }).ToArray();

        if (otherSnbtFiles.Length == 0)
        {
            LogMessage("  No other SNBT files found");
            return 0;
        }

        LogMessage($"  Found {otherSnbtFiles.Length} other SNBT files to convert");

        foreach (var snbtFile in otherSnbtFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Determine output file path by removing .snbt extension
                var outputFile = snbtFile;
                if (outputFile.EndsWith(".snbt"))
                    outputFile = outputFile.Substring(0, outputFile.Length - 5); // Remove .snbt

                // Convert SNBT back to original format
                LogMessage($"    Converting {Path.GetFileName(snbtFile)}...");
                await _nbtService.ConvertFromSnbtAsync(snbtFile, outputFile);

                // Verify conversion succeeded
                if (File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    LogMessage($"    ✓ Created {Path.GetFileName(outputFile)} ({fileInfo.Length / 1024}KB)");
                    processedCount++;
                }
                else
                {
                    LogMessage($"    ✗ Failed to create {Path.GetFileName(outputFile)}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"    ✗ Error converting {Path.GetFileName(snbtFile)}: {ex.Message}");
            }
        }

        return processedCount;
    }

    private async Task<(int snbtFiles, int chunkFolders)> CleanupSnbtFilesAndChunkFolders(string outputPath,
        CancellationToken cancellationToken)
    {
        var snbtFilesDeleted = 0;
        var chunkFoldersDeleted = 0;

        await Task.Run(() =>
        {
            // Define folders where cleanup should happen
            string[] targetFolders = { "region", "poi", "entities" };

            foreach (var folderName in targetFolders)
            {
                var folderPath = Path.Combine(outputPath, folderName);
                if (!Directory.Exists(folderPath))
                {
                    LogMessage($"  Folder {folderName} does not exist, skipping cleanup...");
                    continue;
                }

                LogMessage($"  Cleaning up {folderName} folder...");

                // Step 4.1: Delete SNBT files only in this specific folder
                var snbtFiles = Directory.GetFiles(folderPath, "*.snbt", SearchOption.AllDirectories);
                LogMessage($"    Found {snbtFiles.Length} SNBT files to remove in {folderName}");

                foreach (var snbtFile in snbtFiles)
                    try
                    {
                        File.Delete(snbtFile);
                        snbtFilesDeleted++;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"      ⚠ Warning: Failed to delete {Path.GetFileName(snbtFile)}: {ex.Message}");
                    }

                // Step 4.2: Delete chunk marker files only in this specific folder
                var markerFiles = Directory.GetFiles(folderPath, "*.chunk_mode", SearchOption.AllDirectories);
                LogMessage($"    Found {markerFiles.Length} chunk marker files to remove in {folderName}");

                foreach (var markerFile in markerFiles)
                    try
                    {
                        File.Delete(markerFile);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"      ⚠ Warning: Failed to delete {Path.GetFileName(markerFile)}: {ex.Message}");
                    }

                // Step 4.3: Delete chunk folders only in this specific folder
                var allDirectories = Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories);
                var chunkDirectories =
                    allDirectories.Where(dir => Path.GetFileName(dir).EndsWith(".chunks"));

                LogMessage($"    Found {chunkDirectories.Count()} chunk folders to remove in {folderName}");

                foreach (var chunkDir in chunkDirectories)
                    try
                    {
                        if (Directory.Exists(chunkDir))
                        {
                            Directory.Delete(chunkDir, true);
                            chunkFoldersDeleted++;
                            LogMessage($"      ✓ Deleted chunk folder: {Path.GetFileName(chunkDir)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage(
                            $"      ⚠ Warning: Failed to delete chunk folder {Path.GetFileName(chunkDir)}: {ex.Message}");
                    }

                // Step 4.4: Remove any remaining empty directories only in this specific folder
                var emptyDirs = allDirectories.Where(dir =>
                    Directory.Exists(dir) &&
                    Directory.GetFileSystemEntries(dir).Length == 0).ToArray();

                if (emptyDirs.Length > 0)
                {
                    LogMessage($"    Found {emptyDirs.Length} empty directories to remove in {folderName}");
                    foreach (var emptyDir in emptyDirs)
                        try
                        {
                            Directory.Delete(emptyDir, false);
                            LogMessage(
                                $"      ✓ Deleted empty directory: {Path.GetRelativePath(folderPath, emptyDir)}");
                        }
                        catch (Exception ex)
                        {
                            LogMessage(
                                $"      ⚠ Warning: Failed to delete empty directory {Path.GetFileName(emptyDir)}: {ex.Message}");
                        }
                }
            }
        }, cancellationToken);

        return (snbtFilesDeleted, chunkFoldersDeleted);
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
                // Copy directory recursively
                await Task.Run(() => CopyDirectory(entry, outputEntry), cancellationToken);
            else
                // Copy file
                File.Copy(entry, outputEntry, true);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        // Copy all files in the source directory
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        // Recursively copy all subdirectories
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }

    #endregion
}