using System.Security.Cryptography;
using System.Text;

namespace GitMC.Utils;

/// <summary>
/// Utilities for writing and reading translation stamp files to enable strict
/// version matching and divergence detection between original files and SNBT.
/// </summary>
public static class StampUtils
{
    public record Stamp(string OriginalPath, string? OriginalHash, DateTime OriginalLastWriteUtc,
                        string Translator, string FormatVersion, DateTime TranslatedAtUtc,
                        string? Notes = null);

    private const string CurrentFormat = "1"; // bump if structure changes

    public static string GetStampPathForFile(string snbtOrChunkPath)
    {
        return snbtOrChunkPath + ".stamp.json";
    }

    public static async Task WriteStampAsync(string originalPath, string snbtOrChunkPath, string translator,
        string? notes = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var originalHash = await TryComputeFileHashAsync(originalPath, cancellationToken);
            var stamp = new Stamp(
                OriginalPath: Path.GetFullPath(originalPath),
                OriginalHash: originalHash,
                OriginalLastWriteUtc: File.Exists(originalPath) ? File.GetLastWriteTimeUtc(originalPath) : DateTime.MinValue,
                Translator: translator,
                FormatVersion: CurrentFormat,
                TranslatedAtUtc: DateTime.UtcNow,
                Notes: notes
            );

            var json = System.Text.Json.JsonSerializer.Serialize(stamp, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            var stampPath = GetStampPathForFile(snbtOrChunkPath);
            var dir = Path.GetDirectoryName(stampPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(stampPath, json, cancellationToken);
        }
        catch
        {
            // best-effort; failure to stamp should not break workflow
        }
    }

    /// <summary>
    /// Write a stamp using a precomputed hash for the original file to avoid recomputing it repeatedly.
    /// </summary>
    public static async Task WriteStampAsyncWithHash(string originalPath, string snbtOrChunkPath, string translator,
        string? precomputedOriginalHash, string? notes = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var stamp = new Stamp(
                OriginalPath: Path.GetFullPath(originalPath),
                OriginalHash: precomputedOriginalHash,
                OriginalLastWriteUtc: File.Exists(originalPath) ? File.GetLastWriteTimeUtc(originalPath) : DateTime.MinValue,
                Translator: translator,
                FormatVersion: CurrentFormat,
                TranslatedAtUtc: DateTime.UtcNow,
                Notes: notes
            );

            var json = System.Text.Json.JsonSerializer.Serialize(stamp, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            var stampPath = GetStampPathForFile(snbtOrChunkPath);
            var dir = Path.GetDirectoryName(stampPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(stampPath, json, cancellationToken);
        }
        catch
        {
            // best-effort; failure to stamp should not break workflow
        }
    }

    public static bool TryReadStamp(string snbtOrChunkPath, out Stamp? stamp)
    {
        stamp = null;
        try
        {
            var stampPath = GetStampPathForFile(snbtOrChunkPath);
            if (!File.Exists(stampPath)) return false;
            var json = File.ReadAllText(stampPath);
            stamp = System.Text.Json.JsonSerializer.Deserialize<Stamp>(json);
            return stamp != null;
        }
        catch { return false; }
    }

    public static async Task<string?> TryComputeFileHashAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(path);
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToBase64String(hash);
        }
        catch { return null; }
    }

    public static bool IsUpToDateByStamp(Stamp stamp)
    {
        try
        {
            if (!File.Exists(stamp.OriginalPath)) return false;
            var currentUtc = File.GetLastWriteTimeUtc(stamp.OriginalPath);
            if (currentUtc > stamp.OriginalLastWriteUtc.AddSeconds(1)) return false;
            // If hash is present, verify content match strictly
            if (!string.IsNullOrEmpty(stamp.OriginalHash))
            {
                var nowHash = TryComputeFileHashAsync(stamp.OriginalPath).GetAwaiter().GetResult();
                if (!string.Equals(nowHash, stamp.OriginalHash, StringComparison.Ordinal)) return false;
            }
            return true;
        }
        catch { return false; }
    }
}
