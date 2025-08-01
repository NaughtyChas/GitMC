namespace GitMC.Utils;

/// <summary>
///     String parsing and manipulation utilities
/// </summary>
public static class StringUtils
{
    /// <summary>
    ///     Parse region coordinates from MCA filename
    ///     Example: "r.2.-1.mca" -> (2, -1)
    /// </summary>
    public static (int x, int z) ParseRegionCoordinates(string fileName)
    {
        if (fileName.StartsWith("r."))
        {
            ReadOnlySpan<char> nameSpan = fileName.AsSpan();
            if (nameSpan.Length > 2) // "r." + at least one char
            {
                ReadOnlySpan<char> remaining = nameSpan[2..]; // Skip "r."

                // Find first dot
                int firstDot = remaining.IndexOf('.');
                if (firstDot > 0)
                {
                    ReadOnlySpan<char> xSpan = remaining[..firstDot];
                    ReadOnlySpan<char> afterFirstDot = remaining[(firstDot + 1)..];

                    // Find second dot (or end of string)
                    int secondDot = afterFirstDot.IndexOf('.');
                    ReadOnlySpan<char> zSpan = secondDot >= 0 ? afterFirstDot[..secondDot] : afterFirstDot;

                    if (int.TryParse(xSpan, out int x) && int.TryParse(zSpan, out int z))
                        return (x, z);
                }
            }
        }

        throw new ArgumentException($"Cannot parse region coordinates from filename: {fileName}");
    }
}
