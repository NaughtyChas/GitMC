namespace GitMC.Utils.Mca
{
    /// <summary>
    /// Compression types in MCA/MCC files
    /// Based on https://zh.minecraft.wiki/w/%E5%8C%BA%E5%9F%9F%E6%96%87%E4%BB%B6%E6%A0%BC%E5%BC%8F specification
    /// </summary>
    public enum CompressionType : byte
    {
        /// <summary>
        /// GZip compression (RFC1952) - Not actually used in the game
        /// </summary>
        GZip = 1,

        /// <summary>
        /// Zlib compression (RFC1950) - Default compression algorithm
        /// </summary>
        Zlib = 2,

        /// <summary>
        /// Uncompressed data
        /// </summary>
        Uncompressed = 3,

        /// <summary>
        /// LZ4 compression algorithm
        /// </summary>
        LZ4 = 4,

        /// <summary>
        /// Custom compression algorithm (third-party server implementation)
        /// The following data header must contain a variant UTF-8 formatted namespace ID
        /// </summary>
        Custom = 127
    }

    /// <summary>
    /// Compression type utility class
    /// </summary>
    public static class CompressionHelper
    {
        /// <summary>
        /// Check if the compression type is valid
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
        /// Check if it is oversized mode (the highest bit of the compression type is 1)
        /// When the chunk data exceeds 1020KiB, the game sets the highest bit and creates an MCC file
        /// </summary>
        public static bool IsOversized(byte compressionType)
        {
            return (compressionType & 0x80) != 0;
        }

        /// <summary>
        /// Get the actual compression type (remove the oversized flag bit)
        /// </summary>
        public static CompressionType GetActualCompressionType(byte compressionType)
        {
            return (CompressionType)(compressionType & 0x7F);
        }

        /// <summary>
        /// Set the oversized flag bit
        /// </summary>
        public static byte SetOversizedFlag(CompressionType compressionType)
        {
            return (byte)((byte)compressionType | 0x80);
        }

        /// <summary>
        /// Get the description of the compression type
        /// </summary>
        public static string GetDescription(CompressionType compressionType)
        {
            return compressionType switch
            {
                CompressionType.GZip => "GZip (RFC1952)",
                CompressionType.Zlib => "Zlib (RFC1950) - Default",
                CompressionType.Uncompressed => "Uncompressed",
                CompressionType.LZ4 => "LZ4",
                CompressionType.Custom => "Custom (Third-party)",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Compress data
        /// </summary>
        public static byte[] Compress(byte[] data, CompressionType compressionType)
        {
            return compressionType switch
            {
                CompressionType.GZip => CompressGZip(data),
                CompressionType.Zlib => CompressZlib(data),
                CompressionType.Uncompressed => data,
                CompressionType.LZ4 => CompressLZ4(data),
                CompressionType.Custom => throw new NotSupportedException("Custom compression not implemented"),
                _ => throw new ArgumentException($"Invalid compression type: {compressionType}")
            };
        }

        /// <summary>
        /// Decompress data
        /// </summary>
        public static byte[] Decompress(byte[] compressedData, CompressionType compressionType)
        {
            return compressionType switch
            {
                CompressionType.GZip => DecompressGZip(compressedData),
                CompressionType.Zlib => DecompressZlib(compressedData),
                CompressionType.Uncompressed => compressedData,
                CompressionType.LZ4 => DecompressLZ4(compressedData),
                CompressionType.Custom => throw new NotSupportedException("Custom compression not implemented"),
                _ => throw new ArgumentException($"Invalid compression type: {compressionType}")
            };
        }

        private static byte[] CompressGZip(byte[] data)
        {
            using var memoryStream = new MemoryStream();
            using (var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Compress))
            {
                gzipStream.Write(data, 0, data.Length);
            }
            return memoryStream.ToArray();
        }

        private static byte[] DecompressGZip(byte[] compressedData)
        {
            using var memoryStream = new MemoryStream(compressedData);
            using var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            gzipStream.CopyTo(resultStream);
            return resultStream.ToArray();
        }

        private static byte[] CompressZlib(byte[] data)
        {
            // .NET's DeflateStream implements the
            using var memoryStream = new MemoryStream();
            using (var deflateStream = new System.IO.Compression.DeflateStream(memoryStream, System.IO.Compression.CompressionMode.Compress))
            {
                deflateStream.Write(data, 0, data.Length);
            }
            return memoryStream.ToArray();
        }

        private static byte[] DecompressZlib(byte[] compressedData)
        {
            using var memoryStream = new MemoryStream(compressedData);
            using var deflateStream = new System.IO.Compression.DeflateStream(memoryStream, System.IO.Compression.CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            deflateStream.CopyTo(resultStream);
            return resultStream.ToArray();
        }

        private static byte[] CompressLZ4(byte[] data)
        {
            // 注意：这需要 LZ4 NuGet 包
            // 为了保持兼容性，暂时抛出异常
            throw new NotImplementedException("LZ4 compression requires additional NuGet package");
        }

        private static byte[] DecompressLZ4(byte[] compressedData)
        {
            // 注意：这需要 LZ4 NuGet 包
            // 为了保持兼容性，暂时抛出异常
            throw new NotImplementedException("LZ4 decompression requires additional NuGet package");
        }
    }

    /// <summary>
    /// CompressionType 扩展方法
    /// </summary>
    public static class CompressionTypeExtensions
    {
        /// <summary>
        /// 检查压缩类型是否指示外部文件(.mcc)
        /// </summary>
        public static bool IsExternal(this CompressionType type)
        {
            return ((byte)type & 0x80) != 0;
        }

        /// <summary>
        /// 获取基础压缩类型（不带外部标志）
        /// </summary>
        public static CompressionType GetBaseType(this CompressionType type)
        {
            return (CompressionType)((byte)type & 0x7F);
        }

        /// <summary>
        /// 获取外部版本的压缩类型
        /// </summary>
        public static CompressionType GetExternalType(this CompressionType type)
        {
            if (type.IsExternal()) return type;
            return (CompressionType)((byte)type | 0x80);
        }
    }
}
