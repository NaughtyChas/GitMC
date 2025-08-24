using System.IO.Compression;

namespace GitMC.Utils.Mca;

/// <summary>
///     Compression types in MCA/MCC files
///     Based on https://zh.minecraft.wiki/w/%E5%8C%BA%E5%9F%9F%E6%96%87%E4%BB%B6%E6%A0%BC%E5%BC%8F specification
/// </summary>
public enum CompressionType : byte
{
    /// <summary>
    ///     GZip compression (RFC1952) - Not actually used in the game
    /// </summary>
    GZip = 1,

    /// <summary>
    ///     Zlib compression (RFC1950) - Default compression algorithm
    /// </summary>
    Zlib = 2,

    /// <summary>
    ///     Uncompressed data
    /// </summary>
    Uncompressed = 3,

    /// <summary>
    ///     LZ4 compression algorithm
    /// </summary>
    Lz4 = 4,

    /// <summary>
    ///     Custom compression algorithm (third-party server implementation)
    ///     The following data header must contain a variant UTF-8 formatted namespace ID
    /// </summary>
    Custom = 127
}

/// <summary>
///     Compression type utility class
/// </summary>
public static class CompressionHelper
{
    /// <summary>
    ///     Check if the compression type is valid
    /// </summary>
    public static bool IsValidCompressionType(byte compressionType)
    {
        return compressionType switch
        {
            1 or 2 or 3 or 4 or 127 => true,
            _ => false
        };
    }

    /// <summary>
    ///     Check if it is oversized mode (the highest bit of the compression type is 1)
    ///     When the chunk data exceeds 1020KiB, the game sets the highest bit and creates an MCC file
    /// </summary>
    public static bool IsOversized(byte compressionType)
    {
        return (compressionType & 0x80) != 0;
    }

    /// <summary>
    ///     Get the actual compression type (remove the oversized flag bit)
    /// </summary>
    public static CompressionType GetActualCompressionType(byte compressionType)
    {
        return (CompressionType)(compressionType & 0x7F);
    }

    /// <summary>
    ///     Set the oversized flag bit
    /// </summary>
    public static byte SetOversizedFlag(CompressionType compressionType)
    {
        return (byte)((byte)compressionType | 0x80);
    }

    /// <summary>
    ///     Get the description of the compression type
    /// </summary>
    public static string GetDescription(CompressionType compressionType)
    {
        return compressionType switch
        {
            CompressionType.GZip => "GZip (RFC1952)",
            CompressionType.Zlib => "Zlib (RFC1950) - Default",
            CompressionType.Uncompressed => "Uncompressed",
            CompressionType.Lz4 => "LZ4",
            CompressionType.Custom => "Custom (Third-party)",
            _ => "Unknown"
        };
    }

    /// <summary>
    ///     Compress data
    /// </summary>
    public static byte[] Compress(byte[] data, CompressionType compressionType)
    {
        return compressionType switch
        {
            CompressionType.GZip => CompressGZip(data),
            CompressionType.Zlib => CompressZlib(data),
            CompressionType.Uncompressed => data,
            CompressionType.Lz4 => CompressLz4(data),
            CompressionType.Custom => throw new NotSupportedException("Custom compression not implemented"),
            _ => throw new ArgumentException($"Invalid compression type: {compressionType}")
        };
    }

    /// <summary>
    ///     Decompress data
    /// </summary>
    public static byte[] Decompress(byte[] compressedData, CompressionType compressionType)
    {
        return compressionType switch
        {
            CompressionType.GZip => DecompressGZip(compressedData),
            CompressionType.Zlib => DecompressZlib(compressedData),
            CompressionType.Uncompressed => compressedData,
            CompressionType.Lz4 => DecompressLz4(compressedData),
            CompressionType.Custom => throw new NotSupportedException("Custom compression not implemented"),
            _ => throw new ArgumentException($"Invalid compression type: {compressionType}")
        };
    }

    private static byte[] CompressGZip(byte[] data)
    {
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
        {
            gzipStream.Write(data, 0, data.Length);
        }

        return memoryStream.ToArray();
    }

    private static byte[] DecompressGZip(byte[] compressedData)
    {
        using var memoryStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();
        gzipStream.CopyTo(resultStream);
        return resultStream.ToArray();
    }

    private static byte[] CompressZlib(byte[] data)
    {
        // Zlib format: [CMF][FLG][...compressed data...][ADLER32]
        using var resultStream = new MemoryStream();

        // Write zlib header
        // CMF (Compression Method and flags): 0x78 (deflate method with 32K window)
        // FLG (FLaGs): calculated to make (CMF*256 + FLG) % 31 == 0
        byte cmf = 0x78;
        byte flg = 0x9C; // This makes (0x78*256 + 0x9C) % 31 == 0

        resultStream.WriteByte(cmf);
        resultStream.WriteByte(flg);

        // Compress data using DeflateStream
        using (var deflateStream = new DeflateStream(resultStream, CompressionMode.Compress, true))
        {
            deflateStream.Write(data, 0, data.Length);
        }

        // Calculate and write Adler-32 checksum
        var adler32 = CalculateAdler32(data);
        resultStream.WriteByte((byte)(adler32 >> 24));
        resultStream.WriteByte((byte)(adler32 >> 16));
        resultStream.WriteByte((byte)(adler32 >> 8));
        resultStream.WriteByte((byte)adler32);

        return resultStream.ToArray();
    }

    private static uint CalculateAdler32(byte[] data)
    {
        const uint modAdler = 65521;
        uint a = 1, b = 0;

        foreach (var bt in data)
        {
            a = (a + bt) % modAdler;
            b = (b + a) % modAdler;
        }

        return (b << 16) | a;
    }

    private static byte[] DecompressZlib(byte[] compressedData)
    {
        // Zlib format: [CMF][FLG][...compressed data...][ADLER32]
        // We need to skip the zlib header (2 bytes) and checksum (4 bytes at end)
        // and extract just the deflate-compressed data

        if (compressedData.Length < 6) throw new ArgumentException("Invalid zlib data: too short");

        // Check zlib header magic bytes
        var cmf = compressedData[0];
        var flg = compressedData[1];

        // Validate zlib header
        if ((cmf * 256 + flg) % 31 != 0) throw new ArgumentException("Invalid zlib header checksum");

        // Extract deflate data (skip 2-byte header, ignore 4-byte checksum at end)
        var deflateData = new byte[compressedData.Length - 6];
        Array.Copy(compressedData, 2, deflateData, 0, deflateData.Length);

        // Decompress using DeflateStream with a buffered approach
        using var memoryStream = new MemoryStream(deflateData);
        using var deflateStream = new DeflateStream(memoryStream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();

        var buffer = new byte[8192]; // 8 KB buffer
        int bytesRead;
        while ((bytesRead = deflateStream.Read(buffer, 0, buffer.Length)) > 0) resultStream.Write(buffer, 0, bytesRead);

        return resultStream.ToArray();
    }

    private static byte[] CompressLz4(byte[] data)
    {
        // Note: This requires the LZ4 NuGet package
        // To maintain compatibility, throw an exception for now
        throw new NotImplementedException("LZ4 compression requires additional NuGet package");
    }

    private static byte[] DecompressLz4(byte[] compressedData)
    {
        // Note: This requires the LZ4 NuGet package
        // To maintain compatibility, throw an exception for now
        throw new NotImplementedException("LZ4 decompression requires additional NuGet package");
    }
}

/// <summary>
///     CompressionType extension methods
/// </summary>
public static class CompressionTypeExtensions
{
    /// <summary>
    ///     Check if the compression type indicates an external file (.mcc)
    /// </summary>
    public static bool IsExternal(this CompressionType type)
    {
        return ((byte)type & 0x80) != 0;
    }

    /// <summary>
    ///     Get the base compression type (without external flag)
    /// </summary>
    public static CompressionType GetBaseType(this CompressionType type)
    {
        return (CompressionType)((byte)type & 0x7F);
    }

    /// <summary>
    ///     Get the external version of the compression type
    /// </summary>
    public static CompressionType GetExternalType(this CompressionType type)
    {
        if (type.IsExternal()) return type;
        return (CompressionType)((byte)type | 0x80);
    }
}