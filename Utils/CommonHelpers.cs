namespace GitMC.Utils;

/// <summary>
///     Common utility methods
/// </summary>
public static class CommonHelpers
{
    /// <summary>
    ///     Formats file size to human-readable format
    /// </summary>
    public static string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size = size / 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>
    ///     Formats relative time to human-readable format
    /// </summary>
    public static string FormatRelativeTime(DateTime dateTime)
    {
        var timeSpan = DateTime.Now - dateTime;
        return timeSpan.TotalDays switch
        {
            < 1 when timeSpan.TotalHours < 1 => $"{(int)timeSpan.TotalMinutes}m ago",
            < 1 => $"{(int)timeSpan.TotalHours}h ago",
            < 7 => $"{(int)timeSpan.TotalDays}d ago",
            < 30 => $"{(int)(timeSpan.TotalDays / 7)}w ago",
            _ => dateTime.ToString("MMM dd, yyyy")
        };
    }

    /// <summary>
    ///     Calculates folder size
    /// </summary>
    public static long CalculateFolderSize(DirectoryInfo directoryInfo)
    {
        try
        {
            return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    ///     Gets welcome message
    /// </summary>
    public static string GetWelcomeMessage()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            < 12 => "Good morning, Crafter! ☀️",
            < 18 => "Good afternoon, Miner! ⛏️",
            _ => "Good evening, Builder! 🌙"
        };
    }

    /// <summary>
    ///     Gets world icon based on world type
    /// </summary>
    public static string GetWorldIcon(string worldType)
    {
        return worldType.ToLower() switch
        {
            "creative" => "🎨",
            "hardcore" => "💀",
            "spectator" => "👻",
            "adventure" => "🗺️",
            _ => "🌍"
        };
    }

    /// <summary>
    ///     Count pattern occurrences in span
    /// </summary>
    public static int CountOccurrences(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern)
    {
        if (pattern.IsEmpty) return 0;

        var count = 0;
        var index = 0;

        while (index <= text.Length - pattern.Length)
        {
            var found = text[index..].IndexOf(pattern);
            if (found == -1) break;

            count++;
            index += found + pattern.Length;
        }

        return count;
    }

    /// <summary>
    ///     Extract region coordinates from MCA file path
    ///     Example: "r.2.-1.mca" -> Point2I(2, -1)
    /// </summary>
    public static Point2I ExtractRegionCoordinatesFromPath(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.StartsWith("r."))
        {
            // Optimized: Parse region coordinates without Split to avoid allocations
            var nameSpan = fileName.AsSpan();
            if (nameSpan.Length > 2) // "r." + at least one char
            {
                var remaining = nameSpan[2..]; // Skip "r."

                // Find first dot
                var firstDot = remaining.IndexOf('.');
                if (firstDot > 0)
                {
                    var xSpan = remaining[..firstDot];
                    var afterFirstDot = remaining[(firstDot + 1)..];

                    // Find second dot (or end of string)
                    var secondDot = afterFirstDot.IndexOf('.');
                    var zSpan = secondDot >= 0 ? afterFirstDot[..secondDot] : afterFirstDot;

                    if (int.TryParse(xSpan, out var x) && int.TryParse(zSpan, out var z))
                        return new Point2I(x, z);
                }
            }
        }

        return new Point2I(0, 0);
    }

    /// <summary>
    ///     Checks if file is Anvil-related based on extension and content
    /// </summary>
    public static async Task<bool> IsAnvilRelatedFileAsync(string filePath, string extension)
    {
        try
        {
            // Direct MCA/MCC files
            if (extension == ".mca" || extension == ".mcc") return true;

            // For SNBT files, check content to see if it's derived from MCA
            if (extension == ".snbt") return await IsSnbtFromMcaFileAsync(filePath);

            return false;
        }
        catch
        {
            // If we can't determine, fall back to extension-based detection
            return extension == ".mca" || extension == ".mcc";
        }
    }

    /// <summary>
    ///     Checks if SNBT file was derived from MCA
    /// </summary>
    private static async Task<bool> IsSnbtFromMcaFileAsync(string snbtPath)
    {
        try
        {
            // Read the first part of the file to check for MCA-specific indicators
            using var reader = new StreamReader(snbtPath);

            // Read first 10KB or entire file if smaller
            var buffer = new char[10240];
            var charsRead = await reader.ReadAsync(buffer, 0, buffer.Length);
            string content = new(buffer, 0, charsRead);

            // Check for MCA-specific indicators:
            // 1. Region file headers
            if (content.Contains("// Region file:") ||
                content.Contains("// Region coordinates:"))
                return true;

            // 2. Chunk headers in MCA format
            if (content.Contains("// Chunk(") && content.Contains("// Total chunks:")) return true;

            // 3. Minecraft chunk structure indicators
            if (content.Contains("xPos:") && content.Contains("zPos:") &&
                (content.Contains("sections:") || content.Contains("block_states:")))
                return true;

            // 4. Level tag with chunk data (typical of MCA-derived SNBT)
            if (content.Contains("Level:") &&
                (content.Contains("Heightmaps:") || content.Contains("Status:")))
                return true;

            // 5. Multiple chunk indicators (chunk count > 1)
            if (content.Contains("# Chunk") && CountOccurrences(content.AsSpan(), "# Chunk".AsSpan()) > 1)
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }
}