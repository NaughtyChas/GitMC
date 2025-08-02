using System.Collections.ObjectModel;
using System.Text.Json;
using GitMC.Models;

namespace GitMC.Services;

/// <summary>
/// Service for managing save initialization process
/// </summary>
public class SaveInitializationService : ISaveInitializationService
{
    private readonly IGitService _gitService;
    private readonly INbtService _nbtService;
    private readonly IDataStorageService _dataStorageService;

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
            new()
            {
                Name = "Setting up repo",
                Description = "Initializing Git repository"
            },
            new()
            {
                Name = "Setting up storage structure",
                Description = "Creating GitMC directory structure"
            },
            new()
            {
                Name = "Setting up gitignore",
                Description = "Configuring files to exclude from version control"
            },
            new()
            {
                Name = "Preparing manifest",
                Description = "Creating initial manifest file"
            },
            new()
            {
                Name = "Extracting chunks",
                Description = "Converting region files to SNBT format"
            },
            new()
            {
                Name = "Initial commit",
                Description = "Creating first version snapshot"
            }
        };
    }

    public async Task<bool> InitializeSaveAsync(string savePath, IProgress<SaveInitStep>? progress = null)
    {
        var steps = GetInitializationSteps();

        try
        {
            // Step 1: Initialize Git repository
            await ExecuteStepAsync(steps[0], progress, async () =>
            {
                steps[0].Message = "Initializing Git repository...";
                return await _gitService.InitializeRepositoryAsync(savePath);
            });

            // Step 2: Create GitMC directory structure
            await ExecuteStepAsync(steps[1], progress, async () =>
            {
                steps[1].Message = "Creating GitMC directory structure...";
                await CreateGitMcStructure(savePath);
                return true;
            });

            // Step 3: Create .gitignore file
            await ExecuteStepAsync(steps[2], progress, async () =>
            {
                steps[2].Message = "Creating .gitignore file...";
                await CreateGitIgnoreFile(savePath);
                return true;
            });

            // Step 4: Create manifest file
            await ExecuteStepAsync(steps[3], progress, async () =>
            {
                steps[3].Message = "Creating manifest file...";
                await CreateManifestFile(savePath);
                return true;
            });

            // Step 5: Extract chunks to SNBT
            await ExecuteStepAsync(steps[4], progress, async () =>
            {
                steps[4].Message = "Converting region files to SNBT...";
                await ExtractChunksToSnbt(savePath, steps[4], progress);
                return true;
            });

            // Step 6: Initial commit
            await ExecuteStepAsync(steps[5], progress, async () =>
            {
                steps[5].Message = "Creating initial commit...";
                var stageResult = await _gitService.StageAllAsync(savePath);
                if (!stageResult.Success)
                    throw new InvalidOperationException($"Failed to stage files: {stageResult.ErrorMessage}");

                var commitResult = await _gitService.CommitAsync("Initial import: SNBT snapshot of all world chunks", savePath);
                return commitResult.Success;
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

            # Keep GitMC directory
            !GitMC/
            """;

        string gitIgnorePath = Path.Combine(savePath, ".gitignore");
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

    private async Task ExtractChunksToSnbt(string savePath, SaveInitStep step, IProgress<SaveInitStep>? progress)
    {
        string regionPath = Path.Combine(savePath, "region");
        string gitMcRegionPath = Path.Combine(savePath, "GitMC", "region");

        if (!Directory.Exists(regionPath))
        {
            step.Message = "No region files found, skipping chunk extraction";
            progress?.Report(step);
            return;
        }

        var mcaFiles = Directory.GetFiles(regionPath, "*.mca");
        int totalFiles = mcaFiles.Length;
        int processedFiles = 0;

        // Update step with progress info
        step.CurrentProgress = 0;
        step.TotalProgress = totalFiles;
        step.Message = $"Starting extraction of {totalFiles} region files...";
        progress?.Report(step);

        foreach (string mcaFile in mcaFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(mcaFile);
            string outputDir = Path.Combine(gitMcRegionPath, fileName + ".mca");

            // Update progress information before processing
            step.Message = $"Processing {fileName}";
            progress?.Report(step);

            try
            {
                // Check if file is empty or very small before processing
                var fileInfo = new FileInfo(mcaFile);
                if (fileInfo.Length < 8192) // MCA header is 8KB, files smaller than this are likely empty or corrupted
                {
                    step.Message = $"Skipping empty or corrupted file: {fileName}";
                    progress?.Report(step);
                    processedFiles++;
                    // Update progress after skipping
                    step.CurrentProgress = processedFiles;
                    progress?.Report(step);
                    continue;
                }

                Directory.CreateDirectory(outputDir);

                // Use INbtService to convert MCA to SNBT
                string snbtOutputPath = Path.Combine(outputDir, "region.snbt");
                await _nbtService.ConvertToSnbtAsync(mcaFile, snbtOutputPath);
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
        step.Message = $"Completed processing {processedFiles}/{totalFiles} region files";
        progress?.Report(step);
    }
}
