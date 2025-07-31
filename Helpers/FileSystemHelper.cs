using GitMC.Utils;

namespace GitMC.Helpers
{
    /// <summary>
    /// Helper class for common file system operations
    /// Centralizes file system logic to improve maintainability and error handling
    /// </summary>
    internal static class FileSystemHelper
    {
        /// <summary>
        /// Safely calculates the total size of a folder and its contents
        /// </summary>
        /// <param name="path">Path to the folder</param>
        /// <returns>Total size in bytes, or 0 if calculation fails</returns>
        public static long CalculateFolderSize(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return 0;

            try
            {
                var directoryInfo = new DirectoryInfo(path);
                return CommonHelpers.CalculateFolderSize(directoryInfo);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Analyzes a save folder and returns metadata about its contents
        /// </summary>
        /// <param name="saveFolderPath">Path to the save folder</param>
        /// <returns>Save folder analysis results</returns>
        public static SaveFolderAnalysis AnalyzeSaveFolder(string saveFolderPath)
        {
            var analysis = new SaveFolderAnalysis
            {
                Path = saveFolderPath,
                Exists = Directory.Exists(saveFolderPath)
            };

            if (!analysis.Exists)
                return analysis;

            try
            {
                // Calculate total size
                analysis.TotalSize = CalculateFolderSize(saveFolderPath);
                analysis.FormattedSize = CommonHelpers.FormatFileSize(analysis.TotalSize);

                // Count files and folders
                analysis.FileCount = Directory.GetFiles(saveFolderPath, "*", SearchOption.AllDirectories).Length;
                analysis.FolderCount = Directory.GetDirectories(saveFolderPath, "*", SearchOption.AllDirectories).Length;

                // Check for common Minecraft save files
                analysis.HasLevelDat = File.Exists(Path.Combine(saveFolderPath, "level.dat"));
                analysis.HasSessionLock = File.Exists(Path.Combine(saveFolderPath, "session.lock"));

                // Get last modified time
                var dirInfo = new DirectoryInfo(saveFolderPath);
                analysis.LastModified = dirInfo.LastWriteTime;

                analysis.IsValid = analysis.HasLevelDat; // Basic validation
            }
            catch (Exception ex)
            {
                analysis.Error = ex.Message;
            }

            return analysis;
        }

        /// <summary>
        /// Safely copies a folder and its contents to a new location
        /// </summary>
        /// <param name="sourcePath">Source folder path</param>
        /// <param name="destinationPath">Destination folder path</param>
        /// <param name="overwrite">Whether to overwrite existing files</param>
        /// <returns>True if copy succeeded, false otherwise</returns>
        public static async Task<bool> CopyFolderAsync(string sourcePath, string destinationPath, bool overwrite = false)
        {
            try
            {
                if (!Directory.Exists(sourcePath))
                    return false;

                // Create destination directory if it doesn't exist
                Directory.CreateDirectory(destinationPath);

                // Copy all files
                foreach (string file in Directory.GetFiles(sourcePath))
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(destinationPath, fileName);

                    await Task.Run(() => File.Copy(file, destFile, overwrite));
                }

                // Recursively copy subdirectories
                foreach (string directory in Directory.GetDirectories(sourcePath))
                {
                    string dirName = Path.GetFileName(directory);
                    string destDir = Path.Combine(destinationPath, dirName);

                    bool result = await CopyFolderAsync(directory, destDir, overwrite);
                    if (!result) return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a path is safe to use (not system directory, etc.)
        /// </summary>
        /// <param name="path">Path to validate</param>
        /// <returns>True if path is safe to use</returns>
        public static bool IsPathSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Get full path to normalize
                string fullPath = Path.GetFullPath(path);

                // Check against system directories
                string systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                return !fullPath.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase) &&
                       !fullPath.StartsWith(windowsRoot, StringComparison.OrdinalIgnoreCase) &&
                       !fullPath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase) &&
                       !fullPath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Results of analyzing a save folder
    /// </summary>
    public class SaveFolderAnalysis
    {
        public string Path { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public long TotalSize { get; set; }
        public string FormattedSize { get; set; } = "0 B";
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public bool HasLevelDat { get; set; }
        public bool HasSessionLock { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsValid { get; set; }
        public string? Error { get; set; }
    }
}
