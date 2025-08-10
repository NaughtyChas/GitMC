using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using GitMC.Models;

namespace GitMC.Services;

/// <summary>
///     Service for managing save initialization process
/// </summary>
public class SaveInitializationService : ISaveInitializationService
{
    private readonly IDataStorageService _dataStorageService;
    private readonly IGitService _gitService;
    private readonly INbtService _nbtService;
    private readonly IManifestService _manifestService;

    public SaveInitializationService(
        IGitService gitService,
        INbtService nbtService,
        IDataStorageService dataStorageService,
        IManifestService manifestService)
    {
        _gitService = gitService;
        _nbtService = nbtService;
        _dataStorageService = dataStorageService;
        _manifestService = manifestService;
    }

    public ObservableCollection<SaveInitStep> GetInitializationSteps()
    {
        return new ObservableCollection<SaveInitStep>
        {
            new() { Name = "Setting up storage structure", Description = "Creating GitMC directory structure" },
            new() { Name = "Copying files", Description = "Copying save files to GitMC directory" },
            new()
            {
                Name = "Extracting chunks",
                Description = "Converting files to SNBT format",
                ShowProgressInName = true // Show progress for chunk extraction
            },
            new() { Name = "Setting up repo", Description = "Initializing Git repository" },
            new()
            {
                Name = "Setting up gitignore", Description = "Configuring files to exclude from version control"
            },
            new() { Name = "Preparing manifest", Description = "Creating initial manifest file" },
            new()
            {
                Name = "Initial commit",
                Description = "Creating first version snapshot",
                ShowProgressInName = true // Show progress for initial commit
            }
        };
    }

    public async Task<bool> InitializeSaveAsync(string savePath, IProgress<SaveInitStep>? progress = null)
    {
        // Validate Git identity is configured before proceeding
        (var userName, var userEmail) = await _gitService.GetIdentityAsync();
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(userEmail))
            throw new InvalidOperationException(
                "Git identity is not configured. Please configure your Git identity in the onboarding process before initializing a save.");

        var steps = GetInitializationSteps();

        // Create a progress wrapper that reports to both the provided handler and the status service
        IProgress<SaveInitStep> progressWrapper = new Progress<SaveInitStep>(step =>
        {
            progress?.Report(step);
            SaveInitializationStatusService.Instance.ReportProgress(step);
        });

        try
        {
            // Step 1: Create GitMC directory structure (moved first)
            await ExecuteStepAsync(steps[0], progressWrapper, async () =>
            {
                steps[0].Message = "Creating GitMC directory structure...";
                await CreateGitMcStructure(savePath);
                return true;
            });

            // Step 2: Copy all files (moved before git init)
            await ExecuteStepAsync(steps[1], progressWrapper, async () =>
            {
                steps[1].Message = "Copying save files to GitMC directory...";
                await CopyAllFiles(savePath, steps[1], progressWrapper);
                return true;
            });

            // Step 3: Extract chunks to SNBT (moved before git init)
            await ExecuteStepAsync(steps[2], progressWrapper, async () =>
            {
                steps[2].Message = "Converting files to SNBT format...";
                await ExtractChunksToSnbt(savePath, steps[2], progressWrapper);
                return true;
            });

            // Step 4: Initialize Git repositories (both save directory and GitMC directory)
            await ExecuteStepAsync(steps[3], progressWrapper, async () =>
            {
                steps[3].Message = "Initializing Git repositories...";

                // Initialize Git repository in the save directory
                var saveRepoSuccess = await _gitService.InitializeRepositoryAsync(savePath);
                if (!saveRepoSuccess)
                    throw new InvalidOperationException("Failed to initialize Git repository in save directory");

                // Initialize Git repository in the GitMC directory
                var gitMcPath = Path.Combine(savePath, "GitMC");
                var gitMcRepoSuccess = await _gitService.InitializeRepositoryAsync(gitMcPath);
                if (!gitMcRepoSuccess)
                    throw new InvalidOperationException("Failed to initialize Git repository in GitMC directory");

                return true;
            });

            // Step 5: Create .gitignore files for both repositories
            await ExecuteStepAsync(steps[4], progressWrapper, async () =>
            {
                steps[4].Message = "Creating .gitignore files...";

                // Create .gitignore for save directory
                await CreateGitIgnoreFile(savePath);

                // Create .gitignore for GitMC directory
                var gitMcPath = Path.Combine(savePath, "GitMC");
                await CreateGitMcGitIgnoreFile(gitMcPath);

                return true;
            });

            // Step 6: Create manifest file
            await ExecuteStepAsync(steps[5], progressWrapper, async () =>
            {
                steps[5].Message = "Creating manifest file...";
                await CreateManifestFile(savePath);
                return true;
            });

            // Step 7: Initial commits for both repositories
            await ExecuteStepAsync(steps[6], progressWrapper, async () =>
            {
                // Set up progress tracking for commit operations
                var totalOperations = 8; // GitMC: scan, stage, commit, update manifest, cleanup; Save: stage, status check, commit (if needed)
                var currentOperation = 0;

                steps[6].CurrentProgress = currentOperation;
                steps[6].TotalProgress = totalOperations;
                steps[6].Message = "Creating initial commits...";
                progressWrapper.Report(steps[6]);

                // First, create initial commit in GitMC directory (which should have SNBT files)
                var gitMcPath = Path.Combine(savePath, "GitMC");

                // Verify GitMC directory has files before committing
                if (!Directory.Exists(gitMcPath))
                    throw new InvalidOperationException("GitMC directory does not exist");

                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Scanning GitMC directory for files...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                var gitMcFiles = Directory.GetFiles(gitMcPath, "*", SearchOption.AllDirectories);
                if (gitMcFiles.Length == 0)
                    throw new InvalidOperationException("GitMC directory is empty - no files to commit");

                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = $"Staging {gitMcFiles.Length} files in GitMC directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                var gitMcStageResult = await _gitService.StageAllAsync(gitMcPath);
                if (!gitMcStageResult.Success)
                    throw new InvalidOperationException(
                        $"Failed to stage files in GitMC directory: {gitMcStageResult.ErrorMessage}");

                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Creating commit in GitMC directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                var gitMcCommitResult =
                    await _gitService.CommitAsync("Initial import", gitMcPath);
                if (!gitMcCommitResult.Success)
                    throw new InvalidOperationException(
                        $"Failed to commit in GitMC directory: {gitMcCommitResult.ErrorMessage}");

                // Get the actual commit hash after successful commit
                var commitHash = await _gitService.GetCurrentCommitHashAsync(gitMcPath);
                if (string.IsNullOrEmpty(commitHash))
                    throw new InvalidOperationException("Failed to retrieve commit hash after successful commit");

                // Update manifest with actual commit hash
                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Updating manifest with commit information...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                var initUpdated = await _manifestService.UpdatePendingEntriesAsync(gitMcPath, commitHash);
                if (initUpdated > 0)
                {
                    // Stage manifest and amend the commit so manifest and files share the same commit
                    await _gitService.StageFileAsync("manifest.json", gitMcPath);
                    var amendInit = await _gitService.AmendLastCommitAsync(null, gitMcPath);
                    if (!amendInit.Success)
                        throw new InvalidOperationException($"Failed to amend initial commit with manifest: {amendInit.ErrorMessage}");
                }

                // Clean up SNBT files from working directory after successful commit
                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Cleaning up SNBT files from working directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                await CleanupSnbtFiles(gitMcPath, steps[6], progressWrapper);

                // Then, create initial commit in save directory (excluding GitMC due to .gitignore)
                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Staging files in save directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                var saveStageResult = await _gitService.StageAllAsync(savePath);
                if (!saveStageResult.Success)
                    throw new InvalidOperationException(
                        $"Failed to stage files in save directory: {saveStageResult.ErrorMessage}");

                // Only commit if there are files staged
                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Checking staged files in save directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                var saveStatus = await _gitService.GetStatusAsync(savePath);
                if (saveStatus.StagedFiles.Length > 0)
                {
                    currentOperation++;
                    steps[6].CurrentProgress = currentOperation;
                    steps[6].Message =
                        $"Creating commit for {saveStatus.StagedFiles.Length} files in save directory...";
                    progressWrapper.Report(steps[6]);
                    await Task.Delay(100); // Brief pause for UI update

                    var saveCommitResult =
                        await _gitService.CommitAsync("Initial import", savePath);
                    if (!saveCommitResult.Success)
                        throw new InvalidOperationException(
                            $"Failed to commit in save directory: {saveCommitResult.ErrorMessage}");
                }
                else
                {
                    currentOperation++;
                    steps[6].CurrentProgress = currentOperation;
                    steps[6].Message = "No files to commit in save directory (all excluded by .gitignore)";
                    progressWrapper.Report(steps[6]);
                    await Task.Delay(100); // Brief pause for UI update
                }

                steps[6].CurrentProgress = totalOperations;
                steps[6].Message = "Repositories committed successfully";
                progressWrapper.Report(steps[6]);
                return true;
            });
            return true;
        }
        catch (Exception ex)
        {
            // Mark current step as failed
            var currentStep = steps.FirstOrDefault(s => s.Status == SaveInitStepStatus.InProgress);
            if (currentStep != null)
            {
                currentStep.Status = SaveInitStepStatus.Failed;
                currentStep.Message = $"Failed: {ex.Message}";
                progress?.Report(currentStep);
            }

            return false;
        }
    }

    private async Task ExecuteStepAsync(SaveInitStep step, IProgress<SaveInitStep>? progress, Func<Task<bool>> action)
    {
        step.Status = SaveInitStepStatus.InProgress;
        progress?.Report(step);

        await Task.Delay(100); // Brief pause for UI update

        var success = await action();

        step.Status = success ? SaveInitStepStatus.Completed : SaveInitStepStatus.Failed;
        step.Message = success ? "Completed" : "Failed";
        progress?.Report(step);

        if (!success)
            throw new InvalidOperationException($"Step '{step.Name}' failed");
    }

    private async Task CreateGitMcStructure(string savePath)
    {
        var gitMcPath = Path.Combine(savePath, "GitMC");
        var regionPath = Path.Combine(gitMcPath, "region");

        Directory.CreateDirectory(gitMcPath);
        Directory.CreateDirectory(regionPath);

        await Task.CompletedTask;
    }

    private async Task CreateGitIgnoreFile(string savePath)
    {
        var gitIgnoreContent = """
                               # Ignore Minecraft runtime files and logs
                               session.lock
                               *.log
                               logs/

                               # Ignore temporary files
                               *.tmp
                               *.temp

                               # Ignore player data that changes frequently
                               playerdata/
                               stats/

                               # Ignore GitMC directory (it has its own repository)
                               GitMC/
                               """;

        var gitIgnorePath = Path.Combine(savePath, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, gitIgnoreContent);
    }

    private async Task CreateGitMcGitIgnoreFile(string gitMcPath)
    {
        var gitIgnoreContent = """
                               # Ignore backup files
                               *.backup
                               *.bak

                               # Ignore temporary files
                               *.tmp
                               *.temp

                               # Ignore chunk mode marker files (these are temporary during processing)
                               *.chunk_mode

                               # Ignore processing artifacts
                               *.processing
                               *.error

                               # Keep all SNBT files and chunk directories
                               !*.snbt
                               !*.chunks/
                               """;

        var gitIgnorePath = Path.Combine(gitMcPath, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, gitIgnoreContent);
    }

    private async Task CreateManifestFile(string savePath)
    {
        var gitMcPath = Path.Combine(savePath, "GitMC");

        // Create proper manifest using ManifestService
        await _manifestService.CreateInitialManifestAsync(gitMcPath);
    }

    private async Task CopyAllFiles(string savePath, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        var gitMcPath = Path.Combine(savePath, "GitMC");

        step.Message = "Copying save files to GitMC directory...";
        progress?.Report(step);

        await Task.Run(() => { CopyDirectory(new DirectoryInfo(savePath), new DirectoryInfo(gitMcPath)); });

        // Verify that key files were copied
        string[] expectedFiles = { "level.dat", "level.dat_old" };
        foreach (var expectedFile in expectedFiles)
        {
            var sourcePath = Path.Combine(savePath, expectedFile);
            var targetPath = Path.Combine(gitMcPath, expectedFile);

            if (File.Exists(sourcePath) && !File.Exists(targetPath))
            {
                step.Message = $"Warning: Failed to copy {expectedFile}";
                progress?.Report(step);
                await Task.Delay(500); // Show warning briefly
            }
        }

        step.Message = "File copy complete";
        progress?.Report(step);
    }

    private void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
    {
        Directory.CreateDirectory(target.FullName);

        // Copy files
        foreach (var file in source.GetFiles()) file.CopyTo(Path.Combine(target.FullName, file.Name), true);

        // Copy subdirectories
        foreach (var subDir in source.GetDirectories())
        {
            // Skip GitMC directory to avoid infinite recursion
            if (subDir.Name.Equals("GitMC", StringComparison.OrdinalIgnoreCase))
                continue;

            CopyDirectory(subDir, target.CreateSubdirectory(subDir.Name));
        }
    }

    private async Task<List<FileInfo>> ScanForFiles(string savePath)
    {
        var files = new List<FileInfo>();
        var gitMcDirectory = new DirectoryInfo(Path.Combine(savePath, "GitMC"));

        await Task.Run(() =>
        {
            if (gitMcDirectory.Exists)
            {
                // Region files (.mca, .mcc)
                ScanDirectory(gitMcDirectory, "*.mca", files);
                ScanDirectory(gitMcDirectory, "*.mcc", files);

                // Data files (.dat)
                ScanDirectory(gitMcDirectory, "*.dat", files);

                // NBT files (.nbt)
                ScanDirectory(gitMcDirectory, "*.nbt", files);

                // World data (level.dat, level.dat_old) - check both source and GitMC
                var levelDat = new FileInfo(Path.Combine(savePath, "GitMC", "level.dat"));
                if (levelDat.Exists) files.Add(levelDat);

                var levelDatOld = new FileInfo(Path.Combine(savePath, "GitMC", "level.dat_old"));
                if (levelDatOld.Exists) files.Add(levelDatOld);
            }
        });

        return files;
    }

    private void ScanDirectory(DirectoryInfo directory, string pattern, List<FileInfo> files)
    {
        try
        {
            if (directory.Exists) files.AddRange(directory.GetFiles(pattern, SearchOption.AllDirectories));
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (DirectoryNotFoundException)
        {
            // Skip non-existent directories
        }
    }

    private async Task ExtractChunksToSnbt(string savePath, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        var gitMcPath = Path.Combine(savePath, "GitMC");

        // Verify GitMC directory exists
        if (!Directory.Exists(gitMcPath))
        {
            step.Message = "Error: GitMC directory not found after copy step";
            progress?.Report(step);
            return;
        }

        step.Message = "Scanning for files to process...";
        progress?.Report(step);

        // Scan for files to process (same logic as SaveTranslatorPage)
        var filesToProcess = await ScanForFiles(savePath);

        if (filesToProcess.Count == 0)
        {
            step.Message = "No files found to process - this may indicate copy step failed";
            progress?.Report(step);
            return;
        }

        var totalFiles = filesToProcess.Count;
        var processedFiles = 0;
        var multiChunkFiles = 0;
        var totalChunksProcessed = 0;

        // Update step with progress info
        step.CurrentProgress = 0;
        step.TotalProgress = totalFiles;
        step.Message = $"Starting conversion of {totalFiles} files...";
        progress?.Report(step);

        foreach (var fileInfo in filesToProcess)
        {
            var fileName = fileInfo.Name;

            // Update progress information before processing
            step.Message = $"Processing {fileName}";
            progress?.Report(step);

            try
            {
                // Verify file exists before processing
                if (!File.Exists(fileInfo.FullName))
                {
                    step.Message = $"Warning: File not found: {fileName}";
                    progress?.Report(step);
                    processedFiles++;
                    step.CurrentProgress = processedFiles;
                    progress?.Report(step);
                    await Task.Delay(500); // Show warning briefly
                    continue;
                }

                // Process file to SNBT (no back-conversion) - handle ALL files including empty ones
                (bool IsMultiChunk, int ChunkCount) chunkInfo;

                if (fileInfo.Length <= 0)
                {
                    // Handle empty files - create corresponding SNBT file
                    step.Message = $"Processing empty file: {fileName}";
                    progress?.Report(step);
                    chunkInfo = await ProcessEmptyFileToSnbt(fileInfo.FullName, fileInfo.Extension);
                }
                else
                {
                    // Handle normal files
                    chunkInfo = await ProcessFileToSnbt(fileInfo.FullName, fileInfo.Extension);
                }

                if (chunkInfo.IsMultiChunk)
                {
                    multiChunkFiles++;
                    totalChunksProcessed += chunkInfo.ChunkCount;
                }

                // Delete original file after successful conversion (including empty files)
                try
                {
                    File.Delete(fileInfo.FullName);
                }
                catch (Exception ex)
                {
                    step.Message = $"Warning: Could not delete original file {fileName}: {ex.Message}";
                    progress?.Report(step);
                    await Task.Delay(500); // Show warning briefly
                }
            }
            catch (Exception ex)
            {
                // Log but continue with other files
                step.Message = $"Warning: Failed to process {fileName}: {ex.Message}";
                progress?.Report(step);
                await Task.Delay(1000); // Show warning briefly
            }

            processedFiles++;
            // Update progress after processing each file
            step.CurrentProgress = processedFiles;
            progress?.Report(step);

            // Small delay to allow UI updates
            await Task.Delay(50);
        }

        // Final update with completion status
        step.CurrentProgress = processedFiles;
        step.Message =
            $"Completed: {processedFiles}/{totalFiles} files, {multiChunkFiles} multi-chunk, {totalChunksProcessed} total chunks";
        progress?.Report(step);
    }

    private async Task<(bool IsMultiChunk, int ChunkCount)> ProcessEmptyFileToSnbt(string filePath, string extension)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);

            // Check if this is an MCA/MCC file - use chunk-based structure even for empty files
            if (extension.Equals(".mca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mcc", StringComparison.OrdinalIgnoreCase))
            {
                // Create chunk folder structure for empty MCA files
                var chunkFolderPath = Path.ChangeExtension(filePath, ".chunks");
                Directory.CreateDirectory(chunkFolderPath);

                // Create region_info.snbt file with metadata about the empty MCA file
                var regionInfoPath = Path.Combine(chunkFolderPath, "region_info.snbt");
                var regionInfoContent = $@"// Empty MCA file: {fileName}
// File size: 0 bytes
// Original path: {filePath}

{{
    ""RegionInfo"": {{
        ""OriginalFile"": ""{fileName}"",
        ""FileSize"": 0L,
        ""Extension"": ""{extension}"",
        ""Note"": ""This MCA file was empty in the original save"",
        ""Timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss}"",
        ""ChunkCount"": 0,
        ""IsEmpty"": true
    }}
}}";

                await File.WriteAllTextAsync(regionInfoPath, regionInfoContent, Encoding.UTF8);

                // Create a marker file to indicate this is chunk-based output (compatible with SaveTranslatorPage)
                var markerPath = filePath + ".snbt.chunk_mode";
                await File.WriteAllTextAsync(markerPath, chunkFolderPath, Encoding.UTF8);

                // Empty MCA files are treated as multi-chunk structure but with 0 chunks
                return (true, 0);
            }

            // For non-MCA files, create standard single SNBT file
            var snbtPath = filePath + ".snbt";

            // Create SNBT content for empty file
            var emptyFileSnbtContent = $@"// Empty file: {fileName}
// File size: 0 bytes
// Original path: {filePath}

{{
    ""EmptyFile"": {{
        ""OriginalFile"": ""{fileName}"",
        ""FileSize"": 0L,
        ""Extension"": ""{extension}"",
        ""Note"": ""This file was empty in the original save"",
        ""Timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss}""
    }}
}}";

            await File.WriteAllTextAsync(snbtPath, emptyFileSnbtContent, Encoding.UTF8);

            // Non-MCA empty files are not multi-chunk
            return (false, 0);
        }
        catch (Exception)
        {
            // If we can't create the appropriate structure, try fallbacks
            if (extension.Equals(".mca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mcc", StringComparison.OrdinalIgnoreCase))
            {
                // For MCA files, try to create minimal chunk structure
                try
                {
                    var chunkFolderPath = Path.ChangeExtension(filePath, ".chunks");
                    Directory.CreateDirectory(chunkFolderPath);

                    var regionInfoPath = Path.Combine(chunkFolderPath, "region_info.snbt");
                    await File.WriteAllTextAsync(regionInfoPath,
                        $"// Error processing empty MCA file: {Path.GetFileName(filePath)}\n{{}}",
                        Encoding.UTF8);

                    var markerPath = filePath + ".snbt.chunk_mode";
                    await File.WriteAllTextAsync(markerPath, chunkFolderPath, Encoding.UTF8);

                    return (true, 0);
                }
                catch
                {
                    // If even the fallback fails, return false but don't crash
                }
            }
            else
            {
                // For non-MCA files, try simple fallback
                var snbtPath = filePath + ".snbt";
                try
                {
                    await File.WriteAllTextAsync(snbtPath,
                        $"// Empty file: {Path.GetFileName(filePath)}\n{{}}",
                        Encoding.UTF8);
                }
                catch
                {
                    // If even the fallback fails, return false but don't crash
                }
            }

            return (false, 0);
        }
    }

    private async Task<(bool IsMultiChunk, int ChunkCount)> ProcessFileToSnbt(string filePath, string extension)
    {
        var isMultiChunk = false;
        var chunkCount = 0;

        try
        {
            // Verify file exists
            if (!File.Exists(filePath)) return (false, 0);

            // Convert to SNBT only (no back-conversion)
            var progressCallback = new Progress<string>(message =>
            {
                // Only log errors, not detailed progress to avoid UI spam
                if (message.Contains("error") || message.Contains("failed") || message.Contains("ERROR"))
                {
                    // We could log this, but for initialization we keep it simple
                }
            });

            // Check if this is an MCA/MCC file - use chunk-based conversion for initialization
            if (extension.Equals(".mca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mcc", StringComparison.OrdinalIgnoreCase))
                try
                {
                    // Use chunk-based processing for MCA files (same as SaveTranslatorPage chunk mode)
                    var chunkFolderPath = Path.ChangeExtension(filePath, ".chunks");
                    await _nbtService.ConvertMcaToChunkFilesAsync(filePath, chunkFolderPath, progressCallback);

                    // Create a marker file to indicate this is chunk-based output (compatible with SaveTranslatorPage)
                    var markerPath = filePath + ".snbt.chunk_mode";
                    await File.WriteAllTextAsync(markerPath, chunkFolderPath, Encoding.UTF8);

                    // Count chunks for statistics
                    if (Directory.Exists(chunkFolderPath))
                    {
                        chunkCount = Directory.GetFiles(chunkFolderPath, "chunk_*.snbt").Length;
                        isMultiChunk = chunkCount > 1;
                    }

                    return (isMultiChunk, chunkCount);
                }
                catch (Exception ex)
                {
                    // If chunk-based conversion fails, create error marker
                    var errorSnbtPath = filePath + ".snbt";
                    var fileInfo = new FileInfo(filePath);
                    var errorSnbtContent = $@"// Failed to convert MCA file: {Path.GetFileName(filePath)}
// File size: {fileInfo.Length} bytes
// Error: {ex.Message}
// Original path: {filePath}

{{
    ""ConversionError"": {{
        ""OriginalFile"": ""{Path.GetFileName(filePath)}"",
        ""FileSize"": {fileInfo.Length}L,
        ""ErrorMessage"": ""{ex.Message.Replace("\"", "\\\"")}"",
        ""Timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss}"",
        ""ConversionType"": ""ChunkBased""
    }}
}}";

                    await File.WriteAllTextAsync(errorSnbtPath, errorSnbtContent, Encoding.UTF8);
                    return (false, 0);
                }

            // For non-MCA files, use standard single-file conversion
            var snbtPath = filePath + ".snbt";

            try
            {
                await _nbtService.ConvertToSnbtAsync(filePath, snbtPath, progressCallback);
            }
            catch (Exception ex)
            {
                // If conversion fails, create a basic SNBT file indicating the issue
                var fileInfo = new FileInfo(filePath);
                var errorSnbtContent = $@"// Failed to convert: {Path.GetFileName(filePath)}
// File size: {fileInfo.Length} bytes
// Error: {ex.Message}
// Original path: {filePath}

{{
    ""ConversionError"": {{
        ""OriginalFile"": ""{Path.GetFileName(filePath)}"",
        ""FileSize"": {fileInfo.Length}L,
        ""ErrorMessage"": ""{ex.Message.Replace("\"", "\\\"")}"",
        ""Timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss}"",
        ""ConversionType"": ""Standard""
    }}
}}";

                await File.WriteAllTextAsync(snbtPath, errorSnbtContent, Encoding.UTF8);
            }

            // Verify SNBT file was created (should always exist now)
            if (!File.Exists(snbtPath))
            {
                // Last resort: create minimal SNBT file
                var fileName = Path.GetFileName(filePath);
                var fallbackContent = $@"// File: {fileName}
// Note: Conversion failed and fallback creation also failed

{{
    ""Error"": ""Failed to process file: {fileName}""
}}";
                await File.WriteAllTextAsync(snbtPath, fallbackContent, Encoding.UTF8);
            }

            return (isMultiChunk, chunkCount);
        }
        catch (Exception ex)
        {
            // Even if processing fails, ensure some output exists
            if (extension.Equals(".mca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mcc", StringComparison.OrdinalIgnoreCase))
            {
                // For MCA files, create error SNBT in standard location
                var errorSnbtPath = filePath + ".snbt";
                if (!File.Exists(errorSnbtPath))
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var errorSnbtContent = $@"// Processing failed for MCA file: {Path.GetFileName(filePath)}
// File size: {fileInfo.Length} bytes
// Error: {ex.Message}

{{
    ""ProcessingError"": {{
        ""OriginalFile"": ""{Path.GetFileName(filePath)}"",
        ""FileSize"": {fileInfo.Length}L,
        ""ErrorMessage"": ""{ex.Message.Replace("\"", "\\\"")}"",
        ""Timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss}"",
        ""ConversionType"": ""ChunkBased""
    }}
}}";
                        await File.WriteAllTextAsync(errorSnbtPath, errorSnbtContent, Encoding.UTF8);
                    }
                    catch
                    {
                        // Absolute fallback
                        try
                        {
                            await File.WriteAllTextAsync(errorSnbtPath,
                                $"// Error processing MCA file {Path.GetFileName(filePath)}\n{{}}", Encoding.UTF8);
                        }
                        catch
                        {
                            // If we can't even create the file, give up on this one
                        }
                    }
            }
            else
            {
                // For non-MCA files, create standard error SNBT
                var snbtPath = filePath + ".snbt";
                if (!File.Exists(snbtPath))
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        var errorSnbtContent = $@"// Processing failed for: {Path.GetFileName(filePath)}
// File size: {fileInfo.Length} bytes
// Error: {ex.Message}

{{
    ""ProcessingError"": {{
        ""OriginalFile"": ""{Path.GetFileName(filePath)}"",
        ""FileSize"": {fileInfo.Length}L,
        ""ErrorMessage"": ""{ex.Message.Replace("\"", "\\\"")}"",
        ""Timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss}"",
        ""ConversionType"": ""Standard""
    }}
}}";
                        await File.WriteAllTextAsync(snbtPath, errorSnbtContent, Encoding.UTF8);
                    }
                    catch
                    {
                        // Absolute fallback
                        try
                        {
                            await File.WriteAllTextAsync(snbtPath,
                                $"// Error processing {Path.GetFileName(filePath)}\n{{}}", Encoding.UTF8);
                        }
                        catch
                        {
                            // If we can't even create the file, give up on this one
                        }
                    }
            }

            // Return false values but don't prevent the process from continuing
            return (false, 0);
        }
    }

    /// <summary>
    /// Clean up SNBT files from the working directory after successful commit.
    /// This keeps only the manifest file while removing all SNBT files to improve performance.
    /// </summary>
    private async Task CleanupSnbtFiles(string gitMcPath, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        try
        {
            var regionPath = Path.Combine(gitMcPath, "region");

            if (!Directory.Exists(regionPath))
            {
                step.Message = "No region directory found - cleanup skipped";
                progress?.Report(step);
                return;
            }

            // Count SNBT files first for progress reporting
            var snbtFiles = Directory.GetFiles(regionPath, "*.snbt", SearchOption.AllDirectories);
            var totalFiles = snbtFiles.Length;

            if (totalFiles == 0)
            {
                step.Message = "No SNBT files found - cleanup skipped";
                progress?.Report(step);
                return;
            }

            step.Message = $"Cleaning up {totalFiles} SNBT files...";
            progress?.Report(step);

            // Delete all SNBT files in batches to avoid blocking the UI
            const int batchSize = 100;
            var deletedCount = 0;

            for (int i = 0; i < snbtFiles.Length; i += batchSize)
            {
                var batch = snbtFiles.Skip(i).Take(batchSize);

                await Task.Run(() =>
                {
                    foreach (var snbtFile in batch)
                    {
                        try
                        {
                            File.Delete(snbtFile);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            // Log the error but continue with other files
                            System.Diagnostics.Debug.WriteLine($"Failed to delete SNBT file {snbtFile}: {ex.Message}");
                        }
                    }
                });

                // Update progress
                step.Message = $"Cleaned up {deletedCount}/{totalFiles} SNBT files...";
                progress?.Report(step);

                // Brief pause to prevent UI blocking
                await Task.Delay(10);
            }

            // Clean up empty directories
            await Task.Run(() =>
            {
                try
                {
                    CleanupEmptyDirectories(regionPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to cleanup empty directories: {ex.Message}");
                }
            });

            step.Message = $"Successfully cleaned up {deletedCount} SNBT files";
            progress?.Report(step);
        }
        catch (Exception ex)
        {
            step.Message = $"Warning: Failed to cleanup SNBT files - {ex.Message}";
            progress?.Report(step);
            // Don't throw - this is not critical for the initialization process
        }
    }

    /// <summary>
    /// Recursively remove empty directories, but preserve the main structure for performance
    /// Since Git doesn't track empty folders, keeping them helps avoid recreating them for each commit
    /// </summary>
    private void CleanupEmptyDirectories(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            return;

        // Don't delete any directories - preserve folder structure for performance
        // This keeps the region folder hierarchy intact so we don't need to recreate
        // folders for each new commit, which improves performance

        // Note: We only delete SNBT files, not directories
        // This approach is better for ongoing commits as the folder structure remains ready
    }

    #region Ongoing Commits - Partial Storage System

    /// <summary>
    /// Commit ongoing changes to the save using partial storage
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <param name="commitMessage">Message describing the changes</param>
    /// <param name="progress">Progress callback for step updates</param>
    /// <returns>True if commit was successful</returns>
    public async Task<bool> CommitOngoingChangesAsync(string savePath, string commitMessage, IProgress<SaveInitStep>? progress = null)
    {
        var step = new SaveInitStep { Name = "Committing Changes", Description = "Processing changes using partial storage" };
        // Detect (save + GitMC), Export (save->SNBT), Manifest(pending), Stage(GitMC), Commit(GitMC), Manifest(update+amend),
        // Rebuild MCA from SNBT (if any user-edited), Stage(save), Commit(save), Cleanup(SNBT)
        var totalOperations = 10;
        var currentOperation = 0;

        try
        {
            var gitMcPath = Path.Combine(savePath, "GitMC");

            // Step 1: Detect changed chunks from save (MCA) and detect user-edited SNBT in GitMC
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.TotalProgress = totalOperations;
            step.Message = "Detecting changed chunks (save + SNBT edits)...";
            progress?.Report(step);

            // Changes detected by comparing MCA files in save directory
            var changedChunksFromSave = await DetectChangedChunksAsync(savePath);

            // Changes coming from user-edited SNBT files in GitMC working directory
            var changedChunksFromGitMc = new List<string>();
            var gitMcStatus = await _gitService.GetStatusAsync(gitMcPath);
            var snbtChangedRel = gitMcStatus.ModifiedFiles
                .Concat(gitMcStatus.UntrackedFiles)
                .Where(p => p.Replace('/', Path.DirectorySeparatorChar)
                              .StartsWith($"region{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && p.EndsWith(".snbt", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var rel in snbtChangedRel)
            {
                var fileName = Path.GetFileName(rel);
                if (!string.IsNullOrEmpty(fileName) && fileName.StartsWith("chunk_") && fileName.EndsWith(".snbt"))
                {
                    changedChunksFromGitMc.Add(fileName);
                }
            }
            // Nothing to do if absolutely no changes detected in either place
            if (changedChunksFromSave.Count == 0 && changedChunksFromGitMc.Count == 0)
            {
                step.Message = "No changes detected - commit skipped";
                progress?.Report(step);
                return true;
            }

            // Step 2: Export only changed chunks to SNBT
            currentOperation++;
            step.CurrentProgress = currentOperation;
            // Avoid overwriting user-edited SNBT: exclude those from export
            var snbtChangedSet = new HashSet<string>(snbtChangedRel
                .Select(rel => rel.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
            // Build export list from save changes excluding SNBT already modified by user
            var exportChunks = new List<string>();
            foreach (var chunkFileName in changedChunksFromSave)
            {
                var match = System.Text.RegularExpressions.Regex.Match(chunkFileName, @"chunk_(-?\d+)_(-?\d+)\.snbt");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var cx) && int.TryParse(match.Groups[2].Value, out var cz))
                {
                    var rx = cx >> 5; var rz = cz >> 5;
                    var relPath = Path.Combine("region", $"r.{rx}.{rz}.mca", chunkFileName);
                    if (!snbtChangedSet.Contains(relPath))
                    {
                        exportChunks.Add(chunkFileName);
                    }
                }
            }
            step.Message = exportChunks.Count > 0
                ? $"Exporting {exportChunks.Count} changed chunk(s) to SNBT..."
                : "Skipping export (using user-edited SNBT)";
            progress?.Report(step);

            if (exportChunks.Count > 0)
            {
                await ExportChangedChunksToSnbt(savePath, exportChunks, step, progress);
            }

            // Step 3: Update manifest with pending entries
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Updating manifest with pending entries...";
            progress?.Report(step);

            // Build absolute paths to changed SNBT files so manifest normalizer can trim properly
            var changedSnbtAbsolute = new List<string>();
            // Use union of SNBT from save changes and user-edited ones
            var allChangedChunks = changedChunksFromSave
                .Concat(changedChunksFromGitMc)
                .Distinct()
                .ToList();
            foreach (var chunkFileName in allChangedChunks)
            {
                var match = System.Text.RegularExpressions.Regex.Match(chunkFileName, @"chunk_(-?\d+)_(-?\d+)\.snbt");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var cx) && int.TryParse(match.Groups[2].Value, out var cz))
                {
                    var rx = cx >> 5; var rz = cz >> 5;
                    var full = Path.Combine(gitMcPath, "region", $"r.{rx}.{rz}.mca", chunkFileName);
                    changedSnbtAbsolute.Add(full);
                }
            }
            await _manifestService.UpdateManifestForChangesAsync(gitMcPath, changedSnbtAbsolute);

            // Step 4: Stage changed SNBT files and manifest
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Staging changed files...";
            progress?.Report(step);

            await StageChangedFiles(gitMcPath, allChangedChunks);

            // Step 5: Commit
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Creating commit...";
            progress?.Report(step);

            var commitResult = await _gitService.CommitAsync(commitMessage, gitMcPath);
            if (!commitResult.Success)
                throw new InvalidOperationException($"Failed to commit changes: {commitResult.ErrorMessage}");

            // Get the actual commit hash
            var commitHash = await _gitService.GetCurrentCommitHashAsync(gitMcPath);
            if (string.IsNullOrEmpty(commitHash))
                throw new InvalidOperationException("Failed to retrieve commit hash after successful commit");

            // Step 6: Update manifest with actual commit hash
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Updating manifest with commit hash...";
            progress?.Report(step);

            var updated = await _manifestService.UpdatePendingEntriesAsync(gitMcPath, commitHash);
            if (updated > 0)
            {
                // Stage manifest and amend commit so manifest is in the same commit
                // Use relative path for staging within working directory
                await _gitService.StageFileAsync("manifest.json", gitMcPath);
                var amend = await _gitService.AmendLastCommitAsync(null, gitMcPath);
                if (!amend.Success)
                    throw new InvalidOperationException($"Failed to amend commit with manifest: {amend.ErrorMessage}");
            }

            // Step 7: Rebuild MCA from user-edited SNBT (if any)
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = changedChunksFromGitMc.Count > 0
                ? "Rebuilding MCA files from edited SNBT..."
                : "No user-edited SNBT; skipping rebuild";
            progress?.Report(step);

            if (changedChunksFromGitMc.Count > 0)
            {
                // Group by region folder so we rebuild per-region
                var regionFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rel in snbtChangedRel)
                {
                    var dir = Path.GetDirectoryName(rel) ?? string.Empty; // e.g., region\r.x.z.mca
                    if (!string.IsNullOrEmpty(dir)) regionFolders.Add(dir);
                }

                foreach (var regionFolder in regionFolders)
                {
                    try
                    {
                        var chunkFolderPath = Path.Combine(gitMcPath, regionFolder);
                        // Extract region coords from folder name r.x.z.mca
                        var folderName = Path.GetFileName(regionFolder);
                        // Output path in save directory
                        var outputDir = Path.Combine(savePath, "region");
                        Directory.CreateDirectory(outputDir);
                        var outputMcaPath = Path.Combine(outputDir, folderName);

                        var rebuildProgress = new Progress<string>(msg => System.Diagnostics.Debug.WriteLine($"[Rebuild] {msg}"));
                        await _nbtService.ConvertChunkFilesToMcaAsync(chunkFolderPath, outputMcaPath, rebuildProgress);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to rebuild MCA for {regionFolder}: {ex.Message}");
                        throw;
                    }
                }
            }

            // Step 8: Stage all changes in save directory
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Staging save directory changes...";
            progress?.Report(step);

            var saveStageResult = await _gitService.StageAllAsync(savePath);
            if (!saveStageResult.Success)
                throw new InvalidOperationException($"Failed to stage files in save directory: {saveStageResult.ErrorMessage}");

            // Step 9: Commit save directory changes (only if staged)
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Committing save directory changes...";
            progress?.Report(step);

            var saveStatus = await _gitService.GetStatusAsync(savePath);
            if (saveStatus.StagedFiles.Length > 0)
            {
                var saveCommit = await _gitService.CommitAsync(commitMessage, savePath);
                if (!saveCommit.Success)
                    throw new InvalidOperationException($"Failed to commit save directory changes: {saveCommit.ErrorMessage}");
            }
            else
            {
                step.Message = "No staged changes in save directory";
                progress?.Report(step);
            }

            // Step 10: Cleanup working directory (remove committed SNBT files)
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Cleaning up working directory...";
            progress?.Report(step);

            await CleanupCommittedSnbtFiles(gitMcPath, allChangedChunks, step, progress);

            step.CurrentProgress = totalOperations;
            var totalChanged = allChangedChunks.Count;
            step.Message = $"Successfully committed {totalChanged} change(s)";
            progress?.Report(step);

            return true;
        }
        catch (Exception ex)
        {
            step.Message = $"Failed to commit changes: {ex.Message}";
            progress?.Report(step);
            return false;
        }
    }

    /// <summary>
    /// Translate ongoing changes to SNBT without committing
    /// </summary>
    public async Task<bool> TranslateChangedAsync(string savePath, IProgress<SaveInitStep>? progress = null)
    {
        var step = new SaveInitStep { Name = "Translating Changes", Description = "Exporting changes to SNBT (no commit)" };
        var totalOperations = 4; // Detect, Export, Update manifest (pending), Cleanup old temp
        var currentOperation = 0;

        try
        {
            var gitMcPath = Path.Combine(savePath, "GitMC");

            // Step 1: Detect changed chunks
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.TotalProgress = totalOperations;
            step.Message = "Detecting changed chunks...";
            progress?.Report(step);

            var changedChunks = await DetectChangedChunksAsync(savePath);
            if (changedChunks.Count == 0)
            {
                step.Message = "No changes detected - nothing to translate";
                progress?.Report(step);
                return true;
            }

            // Step 2: Export only changed chunks to SNBT
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = $"Exporting {changedChunks.Count} chunk(s) to SNBT...";
            progress?.Report(step);

            await ExportChangedChunksToSnbt(savePath, changedChunks, step, progress);

            // Step 3: Update manifest with pending entries (no hash assigned yet)
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Updating manifest (pending entries)...";
            progress?.Report(step);

            var changedSnbtAbsolute = new List<string>();
            foreach (var chunkFileName in changedChunks)
            {
                var match = System.Text.RegularExpressions.Regex.Match(chunkFileName, @"chunk_(-?\d+)_(-?\d+)\.snbt");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var cx) && int.TryParse(match.Groups[2].Value, out var cz))
                {
                    var rx = cx >> 5; var rz = cz >> 5;
                    var full = Path.Combine(gitMcPath, "region", $"r.{rx}.{rz}.mca", chunkFileName);
                    changedSnbtAbsolute.Add(full);
                }
            }
            await _manifestService.UpdateManifestForChangesAsync(gitMcPath, changedSnbtAbsolute);

            // Step 4: Finalize
            currentOperation++;
            step.CurrentProgress = currentOperation;
            step.Message = "Translation complete";
            progress?.Report(step);

            return true;
        }
        catch (Exception ex)
        {
            step.Message = $"Failed to translate changes: {ex.Message}";
            progress?.Report(step);
            return false;
        }
    }

    /// <summary>
    /// Detect changed chunks compared to the last committed state
    /// </summary>
    /// <param name="savePath">Path to the save directory</param>
    /// <returns>List of changed chunk file paths</returns>
    public async Task<List<string>> DetectChangedChunksAsync(string savePath)
    {
        var changedChunks = new List<string>();

        try
        {
            // Get Git status to find modified .mca files
            var gitStatus = await _gitService.GetStatusAsync(savePath);
            var modifiedMcaFiles = gitStatus.ModifiedFiles
                .Concat(gitStatus.UntrackedFiles)
                .Concat(gitStatus.DeletedFiles)
                .Where(f => f.EndsWith(".mca", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            foreach (var mcaFile in modifiedMcaFiles)
            {
                var fullMcaPath = Path.Combine(savePath, mcaFile);

                // Use file hash comparison to determine if the .mca file actually changed
                if (await IsFileActuallyChanged(savePath, mcaFile))
                {
                    // Try to list actual chunks from file; if file missing (deleted), skip export here
                    if (File.Exists(fullMcaPath))
                    {
                        try
                        {
                            var chunks = await _nbtService.ListChunksInRegionAsync(fullMcaPath);
                            foreach (var c in chunks.Where(c => c.IsValid))
                            {
                                changedChunks.Add($"chunk_{c.ChunkX}_{c.ChunkZ}.snbt");
                            }
                        }
                        catch
                        {
                            // Fallback to coarse 32x32 if listing fails
                            var fallback = await GetChunksFromMcaFile(fullMcaPath);
                            changedChunks.AddRange(fallback);
                        }
                    }
                }
            }

            return changedChunks;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error detecting changed chunks: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Check if a file has actually changed by comparing content hashes
    /// This is needed because Git may mark files as modified even when just loaded in game
    /// </summary>
    private async Task<bool> IsFileActuallyChanged(string savePath, string relativePath)
    {
        try
        {
            // Get the current file hash
            var currentFilePath = Path.Combine(savePath, relativePath);
            var currentHash = await ComputeFileHash(currentFilePath);

            // Get the hash from the last commit using git show command
            var gitResult = await _gitService.ExecuteCommandAsync($"git show HEAD:{relativePath}", savePath);
            if (!gitResult.Success || gitResult.OutputLines.Length == 0)
                return true; // File is new or couldn't get last commit version

            var lastCommittedContent = Encoding.UTF8.GetBytes(string.Join("\n", gitResult.OutputLines));
            var lastCommittedHash = ComputeContentHash(lastCommittedContent);

            return currentHash != lastCommittedHash;
        }
        catch
        {
            // If we can't determine, assume it changed
            return true;
        }
    }

    /// <summary>
    /// Get list of chunks that should be exported from an MCA file
    /// For simplicity, we export all chunks from a changed MCA file
    /// </summary>
    private Task<List<string>> GetChunksFromMcaFile(string mcaFilePath)
    {
        var chunks = new List<string>();

        try
        {
            // Extract the region coordinates from the MCA filename
            var fileName = Path.GetFileNameWithoutExtension(mcaFilePath);
            var parts = fileName.Split('.');

            if (parts.Length >= 3 && parts[0] == "r" &&
                int.TryParse(parts[1], out var regionX) &&
                int.TryParse(parts[2], out var regionZ))
            {
                // For now, we'll export all possible chunks in this region
                // In a more sophisticated implementation, we would only export chunks that actually exist
                for (int chunkX = regionX * 32; chunkX < (regionX + 1) * 32; chunkX++)
                {
                    for (int chunkZ = regionZ * 32; chunkZ < (regionZ + 1) * 32; chunkZ++)
                    {
                        chunks.Add($"chunk_{chunkX}_{chunkZ}.snbt");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting chunks from MCA file {mcaFilePath}: {ex.Message}");
        }

        return Task.FromResult(chunks);
    }

    /// <summary>
    /// Export only changed chunks to SNBT format
    /// </summary>
    private async Task ExportChangedChunksToSnbt(string savePath, List<string> changedChunks, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        var gitMcPath = Path.Combine(savePath, "GitMC");
        var regionPath = Path.Combine(gitMcPath, "region");

        var totalChunks = changedChunks.Count;
        var processedChunks = 0;

        foreach (var chunkFileName in changedChunks)
        {
            try
            {
                // Parse chunk coordinates from filename
                var match = System.Text.RegularExpressions.Regex.Match(chunkFileName, @"chunk_(-?\d+)_(-?\d+)\.snbt");
                if (match.Success &&
                    int.TryParse(match.Groups[1].Value, out var chunkX) &&
                    int.TryParse(match.Groups[2].Value, out var chunkZ))
                {
                    // Determine which MCA file this chunk belongs to
                    var regionX = chunkX >> 5; // Divide by 32
                    var regionZ = chunkZ >> 5;
                    var mcaFileName = $"r.{regionX}.{regionZ}.mca";
                    var mcaFilePath = Path.Combine(savePath, "region", mcaFileName);

                    if (File.Exists(mcaFilePath))
                    {
                        // Create output directory structure
                        var outputDir = Path.Combine(regionPath, $"r.{regionX}.{regionZ}.mca");
                        Directory.CreateDirectory(outputDir);

                        // Extract this specific chunk to SNBT
                        var snbtContent = await _nbtService.ExtractChunkDataAsync(mcaFilePath, chunkX, chunkZ);
                        var outputPath = Path.Combine(outputDir, chunkFileName);
                        await File.WriteAllTextAsync(outputPath, snbtContent);
                    }
                }

                processedChunks++;
                step.Message = $"Exported {processedChunks}/{totalChunks} chunks to SNBT...";
                progress?.Report(step);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to export chunk {chunkFileName}: {ex.Message}");
                // Continue with other chunks
            }
        }
    }

    /// <summary>
    /// Stage changed SNBT files and manifest
    /// </summary>
    private async Task StageChangedFiles(string gitMcPath, List<string> changedChunks)
    {
        // Stage the manifest file (relative path)
        await _gitService.StageFileAsync("manifest.json", gitMcPath);

        // Stage each changed SNBT file
        foreach (var chunkFileName in changedChunks)
        {
            var match = System.Text.RegularExpressions.Regex.Match(chunkFileName, @"chunk_(-?\d+)_(-?\d+)\.snbt");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out var chunkX) &&
                int.TryParse(match.Groups[2].Value, out var chunkZ))
            {
                var regionX = chunkX >> 5;
                var regionZ = chunkZ >> 5;
                var relativeSnbtPath = Path.Combine("region", $"r.{regionX}.{regionZ}.mca", chunkFileName);
                var fullSnbtPath = Path.Combine(gitMcPath, relativeSnbtPath);
                if (File.Exists(fullSnbtPath))
                    await _gitService.StageFileAsync(relativeSnbtPath, gitMcPath);
            }
        }
    }

    /// <summary>
    /// Clean up committed SNBT files from working directory while preserving folder structure
    /// </summary>
    private Task CleanupCommittedSnbtFiles(string gitMcPath, List<string> committedChunks, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        var deletedCount = 0;

        foreach (var chunkFileName in committedChunks)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(chunkFileName, @"chunk_(-?\d+)_(-?\d+)\.snbt");
                if (match.Success &&
                    int.TryParse(match.Groups[1].Value, out var chunkX) &&
                    int.TryParse(match.Groups[2].Value, out var chunkZ))
                {
                    var regionX = chunkX >> 5;
                    var regionZ = chunkZ >> 5;
                    var snbtPath = Path.Combine(gitMcPath, "region", $"r.{regionX}.{regionZ}.mca", chunkFileName);

                    if (File.Exists(snbtPath))
                    {
                        File.Delete(snbtPath);
                        deletedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete SNBT file {chunkFileName}: {ex.Message}");
            }
        }

        step.Message = $"Cleaned up {deletedCount} committed SNBT files";
        progress?.Report(step);

        // Note: We don't delete directories to preserve folder structure for performance
        return Task.CompletedTask;
    }

    /// <summary>
    /// Compute SHA-256 hash of a file
    /// </summary>
    private async Task<string> ComputeFileHash(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Compute SHA-256 hash of content
    /// </summary>
    private string ComputeContentHash(byte[] content)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(content);
        return Convert.ToBase64String(hashBytes);
    }

    #endregion
}
