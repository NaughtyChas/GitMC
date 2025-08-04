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

    public SaveInitializationService(
        IGitService gitService,
        INbtService nbtService,
        IDataStorageService dataStorageService)
    {
        _gitService = gitService;
        _nbtService = nbtService;
        _dataStorageService = dataStorageService;
    }

    public ObservableCollection<SaveInitStep> GetInitializationSteps()
    {
        return new ObservableCollection<SaveInitStep>
        {
            new() { Name = "Setting up storage structure", Description = "Creating GitMC directory structure" },
            new() { Name = "Copying files", Description = "Copying save files to GitMC directory" },
            new() { Name = "Extracting chunks", Description = "Converting files to SNBT format" },
            new() { Name = "Setting up repo", Description = "Initializing Git repository" },
            new()
            {
                Name = "Setting up gitignore", Description = "Configuring files to exclude from version control"
            },
            new() { Name = "Preparing manifest", Description = "Creating initial manifest file" },
            new() { Name = "Initial commit", Description = "Creating first version snapshot" }
        };
    }

    public async Task<bool> InitializeSaveAsync(string savePath, IProgress<SaveInitStep>? progress = null)
    {
        // Validate Git identity is configured before proceeding
        (string? userName, string? userEmail) = await _gitService.GetIdentityAsync();
        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(userEmail))
            throw new InvalidOperationException(
                "Git identity is not configured. Please configure your Git identity in the onboarding process before initializing a save.");

        ObservableCollection<SaveInitStep> steps = GetInitializationSteps();

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
                bool saveRepoSuccess = await _gitService.InitializeRepositoryAsync(savePath);
                if (!saveRepoSuccess)
                {
                    throw new InvalidOperationException("Failed to initialize Git repository in save directory");
                }

                // Initialize Git repository in the GitMC directory
                string gitMcPath = Path.Combine(savePath, "GitMC");
                bool gitMcRepoSuccess = await _gitService.InitializeRepositoryAsync(gitMcPath);
                if (!gitMcRepoSuccess)
                {
                    throw new InvalidOperationException("Failed to initialize Git repository in GitMC directory");
                }

                return true;
            });

            // Step 5: Create .gitignore files for both repositories
            await ExecuteStepAsync(steps[4], progressWrapper, async () =>
            {
                steps[4].Message = "Creating .gitignore files...";

                // Create .gitignore for save directory
                await CreateGitIgnoreFile(savePath);

                // Create .gitignore for GitMC directory
                string gitMcPath = Path.Combine(savePath, "GitMC");
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
                int totalOperations = 6; // GitMC: scan, stage, commit; Save: stage, status check, commit (if needed)
                int currentOperation = 0;

                steps[6].CurrentProgress = currentOperation;
                steps[6].TotalProgress = totalOperations;
                steps[6].Message = "Creating initial commits...";
                progressWrapper.Report(steps[6]);

                // First, create initial commit in GitMC directory (which should have SNBT files)
                string gitMcPath = Path.Combine(savePath, "GitMC");

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

                GitOperationResult gitMcStageResult = await _gitService.StageAllAsync(gitMcPath);
                if (!gitMcStageResult.Success)
                    throw new InvalidOperationException($"Failed to stage files in GitMC directory: {gitMcStageResult.ErrorMessage}");

                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Creating commit in GitMC directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                GitOperationResult gitMcCommitResult =
                    await _gitService.CommitAsync("Initial import", gitMcPath);
                if (!gitMcCommitResult.Success)
                    throw new InvalidOperationException($"Failed to commit in GitMC directory: {gitMcCommitResult.ErrorMessage}");

                // Then, create initial commit in save directory (excluding GitMC due to .gitignore)
                currentOperation++;
                steps[6].CurrentProgress = currentOperation;
                steps[6].Message = "Staging files in save directory...";
                progressWrapper.Report(steps[6]);
                await Task.Delay(100); // Brief pause for UI update

                GitOperationResult saveStageResult = await _gitService.StageAllAsync(savePath);
                if (!saveStageResult.Success)
                    throw new InvalidOperationException($"Failed to stage files in save directory: {saveStageResult.ErrorMessage}");

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
                    steps[6].Message = $"Creating commit for {saveStatus.StagedFiles.Length} files in save directory...";
                    progressWrapper.Report(steps[6]);
                    await Task.Delay(100); // Brief pause for UI update

                    GitOperationResult saveCommitResult =
                        await _gitService.CommitAsync("Initial import", savePath);
                    if (!saveCommitResult.Success)
                        throw new InvalidOperationException($"Failed to commit in save directory: {saveCommitResult.ErrorMessage}");
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
            }); return true;
        }
        catch (Exception ex)
        {
            // Mark current step as failed
            SaveInitStep? currentStep = steps.FirstOrDefault(s => s.Status == SaveInitStepStatus.InProgress);
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

        bool success = await action();

        step.Status = success ? SaveInitStepStatus.Completed : SaveInitStepStatus.Failed;
        step.Message = success ? "Completed" : "Failed";
        progress?.Report(step);

        if (!success)
            throw new InvalidOperationException($"Step '{step.Name}' failed");
    }

    private async Task CreateGitMcStructure(string savePath)
    {
        string gitMcPath = Path.Combine(savePath, "GitMC");
        string regionPath = Path.Combine(gitMcPath, "region");

        Directory.CreateDirectory(gitMcPath);
        Directory.CreateDirectory(regionPath);

        await Task.CompletedTask;
    }

    private async Task CreateGitIgnoreFile(string savePath)
    {
        string gitIgnoreContent = """
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

        string gitIgnorePath = Path.Combine(savePath, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, gitIgnoreContent);
    }

    private async Task CreateGitMcGitIgnoreFile(string gitMcPath)
    {
        string gitIgnoreContent = """
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

        string gitIgnorePath = Path.Combine(gitMcPath, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, gitIgnoreContent);
    }

    private async Task CreateManifestFile(string savePath)
    {
        var manifest = new
        {
            version = "1.0",
            created = DateTime.UtcNow.ToString("O"),
            description = "GitMC save manifest",
            chunks = Array.Empty<object>()
        };

        string manifestPath = Path.Combine(savePath, "GitMC", "manifest.json");
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(manifestPath, manifestJson);
    }

    private async Task CopyAllFiles(string savePath, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        string gitMcPath = Path.Combine(savePath, "GitMC");

        step.Message = "Copying save files to GitMC directory...";
        progress?.Report(step);

        await Task.Run(() =>
        {
            CopyDirectory(new DirectoryInfo(savePath), new DirectoryInfo(gitMcPath));
        });

        // Verify that key files were copied
        string[] expectedFiles = { "level.dat", "level.dat_old" };
        foreach (string expectedFile in expectedFiles)
        {
            string sourcePath = Path.Combine(savePath, expectedFile);
            string targetPath = Path.Combine(gitMcPath, expectedFile);

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
        foreach (FileInfo file in source.GetFiles()) file.CopyTo(Path.Combine(target.FullName, file.Name), true);

        // Copy subdirectories
        foreach (DirectoryInfo subDir in source.GetDirectories())
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
        string gitMcPath = Path.Combine(savePath, "GitMC");

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
        List<FileInfo> filesToProcess = await ScanForFiles(savePath);

        if (filesToProcess.Count == 0)
        {
            step.Message = "No files found to process - this may indicate copy step failed";
            progress?.Report(step);
            return;
        }

        int totalFiles = filesToProcess.Count;
        int processedFiles = 0;
        int multiChunkFiles = 0;
        int totalChunksProcessed = 0;

        // Update step with progress info
        step.CurrentProgress = 0;
        step.TotalProgress = totalFiles;
        step.Message = $"Starting conversion of {totalFiles} files...";
        progress?.Report(step);

        foreach (FileInfo fileInfo in filesToProcess)
        {
            string fileName = fileInfo.Name;

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
            string fileName = Path.GetFileName(filePath);

            // Check if this is an MCA/MCC file - use chunk-based structure even for empty files
            if (extension.Equals(".mca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mcc", StringComparison.OrdinalIgnoreCase))
            {
                // Create chunk folder structure for empty MCA files
                string chunkFolderPath = Path.ChangeExtension(filePath, ".chunks");
                Directory.CreateDirectory(chunkFolderPath);

                // Create region_info.snbt file with metadata about the empty MCA file
                string regionInfoPath = Path.Combine(chunkFolderPath, "region_info.snbt");
                string regionInfoContent = $@"// Empty MCA file: {fileName}
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
                string markerPath = filePath + ".snbt.chunk_mode";
                await File.WriteAllTextAsync(markerPath, chunkFolderPath, Encoding.UTF8);

                // Empty MCA files are treated as multi-chunk structure but with 0 chunks
                return (true, 0);
            }
            else
            {
                // For non-MCA files, create standard single SNBT file
                string snbtPath = filePath + ".snbt";

                // Create SNBT content for empty file
                string emptyFileSnbtContent = $@"// Empty file: {fileName}
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
                    string chunkFolderPath = Path.ChangeExtension(filePath, ".chunks");
                    Directory.CreateDirectory(chunkFolderPath);

                    string regionInfoPath = Path.Combine(chunkFolderPath, "region_info.snbt");
                    await File.WriteAllTextAsync(regionInfoPath,
                        $"// Error processing empty MCA file: {Path.GetFileName(filePath)}\n{{}}",
                        Encoding.UTF8);

                    string markerPath = filePath + ".snbt.chunk_mode";
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
                string snbtPath = filePath + ".snbt";
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
        bool isMultiChunk = false;
        int chunkCount = 0;

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
            {
                try
                {
                    // Use chunk-based processing for MCA files (same as SaveTranslatorPage chunk mode)
                    string chunkFolderPath = Path.ChangeExtension(filePath, ".chunks");
                    await _nbtService.ConvertMcaToChunkFilesAsync(filePath, chunkFolderPath, progressCallback);

                    // Create a marker file to indicate this is chunk-based output (compatible with SaveTranslatorPage)
                    string markerPath = filePath + ".snbt.chunk_mode";
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
                    string errorSnbtPath = filePath + ".snbt";
                    var fileInfo = new FileInfo(filePath);
                    string errorSnbtContent = $@"// Failed to convert MCA file: {Path.GetFileName(filePath)}
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
            }
            else
            {
                // For non-MCA files, use standard single-file conversion
                string snbtPath = filePath + ".snbt";

                try
                {
                    await _nbtService.ConvertToSnbtAsync(filePath, snbtPath, progressCallback);
                }
                catch (Exception ex)
                {
                    // If conversion fails, create a basic SNBT file indicating the issue
                    var fileInfo = new FileInfo(filePath);
                    string errorSnbtContent = $@"// Failed to convert: {Path.GetFileName(filePath)}
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
                    string fileName = Path.GetFileName(filePath);
                    string fallbackContent = $@"// File: {fileName}
// Note: Conversion failed and fallback creation also failed

{{
    ""Error"": ""Failed to process file: {fileName}""
}}";
                    await File.WriteAllTextAsync(snbtPath, fallbackContent, Encoding.UTF8);
                }

                return (isMultiChunk, chunkCount);
            }
        }
        catch (Exception ex)
        {
            // Even if processing fails, ensure some output exists
            if (extension.Equals(".mca", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mcc", StringComparison.OrdinalIgnoreCase))
            {
                // For MCA files, create error SNBT in standard location
                string errorSnbtPath = filePath + ".snbt";
                if (!File.Exists(errorSnbtPath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        string errorSnbtContent = $@"// Processing failed for MCA file: {Path.GetFileName(filePath)}
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
            }
            else
            {
                // For non-MCA files, create standard error SNBT
                string snbtPath = filePath + ".snbt";
                if (!File.Exists(snbtPath))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        string errorSnbtContent = $@"// Processing failed for: {Path.GetFileName(filePath)}
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
            }

            // Return false values but don't prevent the process from continuing
            return (false, 0);
        }
    }
}
