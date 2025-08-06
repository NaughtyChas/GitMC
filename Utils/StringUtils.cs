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
                        return (x, z);
                }
            }
        }

        throw new ArgumentException($"Cannot parse region coordinates from filename: {fileName}");
    }
}