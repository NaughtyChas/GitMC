using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using fNbt;
using GitMC.Utils;

namespace GitMC.Utils.Mca
{
    /// <summary>
    /// MCA/MCC region file parser
    /// Implements Minecraft region file format specification
    /// </summary>
    public class McaRegionFile : IDisposable
    {
        private readonly string _filePath;
        private readonly Stream _stream;
        private bool _isLoaded;
        private McaHeader? _header;
        private bool _disposed;

        /// <summary>
        /// Region file header information
        /// </summary>
        public McaHeader? Header => _header;

        /// <summary>
        /// Region coordinates
        /// </summary>
        public Point2i RegionCoordinates { get; private set; }

        /// <summary>
        /// Whether loaded
        /// </summary>
        public bool IsLoaded => _isLoaded;

        /// <summary>
        /// File path
        /// </summary>
        public string FilePath => _filePath;

        public McaRegionFile(string filePath)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            // Extract region coordinates from file name
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.StartsWith("r."))
            {
                var parts = fileName.Split('.');
                if (parts.Length >= 3 && 
                    int.TryParse(parts[1], out var x) && 
                    int.TryParse(parts[2], out var z))
                {
                    RegionCoordinates = new Point2i(x, z);
                }
            }
        }

        public McaRegionFile(Stream stream, Point2i regionCoordinates)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _filePath = $"r.{regionCoordinates.X}.{regionCoordinates.Z}.mca";
            RegionCoordinates = regionCoordinates;
        }

        /// <summary>
        /// Asynchronously load region file
        /// </summary>
        public async Task LoadAsync()
        {
            if (_isLoaded) return;

            _stream.Seek(0, SeekOrigin.Begin);
            _header = await McaHeader.LoadAsync(_stream);
            _isLoaded = true;
        }

        /// <summary>
        /// Get data of the specified chunk
        /// </summary>
        public async Task<ChunkData?> GetChunkAsync(Point2i chunkCoordinates)
        {
            if (!_isLoaded) await LoadAsync();

            var localCoords = chunkCoordinates.GetLocalCoordinates();
            var chunkIndex = localCoords.ToChunkIndex();

            if (_header == null) return null;

            var chunkInfo = _header.ChunkInfos[chunkIndex];
            if (chunkInfo.SectorOffset == 0) return null; // Chunk does not exist

            return await ReadChunkDataAsync(chunkInfo);
        }

        /// <summary>
        /// Get all existing chunk coordinates
        /// </summary>
        public List<Point2i> GetExistingChunks()
        {
            if (!_isLoaded) throw new InvalidOperationException("Region file not loaded");

            var chunks = new List<Point2i>();
            if (_header == null) return chunks;

            for (int i = 0; i < 1024; i++)
            {
                if (_header.ChunkInfos[i].SectorOffset != 0)
                {
                    var localCoords = Point2i.FromChunkIndex(i);
                    var globalCoords = new Point2i(
                        RegionCoordinates.X * 32 + localCoords.X,
                        RegionCoordinates.Z * 32 + localCoords.Z
                    );
                    chunks.Add(globalCoords);
                }
            }

            return chunks;
        }

        /// <summary>
        /// Read chunk data
        /// </summary>
        private async Task<ChunkData?> ReadChunkDataAsync(ChunkInfo chunkInfo)
        {
            if (chunkInfo.SectorOffset == 0) return null;

            // Locate chunk data position (each sector is 4096 bytes)
            var offset = chunkInfo.SectorOffset * 4096L;
            _stream.Seek(offset, SeekOrigin.Begin);

            // Read chunk header (length + compression type)
            var headerBytes = await ReadExactAsync(_stream, 5);

            var dataLength = BitConverter.ToInt32(headerBytes, 0);
            if (BitConverter.IsLittleEndian)
            {
                dataLength = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(dataLength);
            }

            var compressionTypeByte = headerBytes[4];
            var compressionType = (CompressionType)(compressionTypeByte & 0x7F);
            var isExternal = (compressionTypeByte & 0x80) != 0;

            // If external file, need to read .mcc file
            if (isExternal)
            {
                return await ReadExternalChunkAsync(compressionType);
            }

            // Read compressed data
            var compressedData = await ReadExactAsync(_stream, dataLength - 1); // -1 because compression type already read

            // Decompress and parse NBT
            var nbtData = DecompressAndParseNbt(compressedData, compressionType);
            
            return new ChunkData
            {
                CompressionType = compressionType,
                IsExternal = isExternal,
                DataLength = dataLength,
                NbtData = nbtData,
                Timestamp = chunkInfo.Timestamp
            };
        }

        /// <summary>
        /// Read external chunk file (.mcc)
        /// </summary>
        private async Task<ChunkData?> ReadExternalChunkAsync(CompressionType compressionType)
        {
            var mccPath = Path.ChangeExtension(_filePath, ".mcc");
            if (!File.Exists(mccPath)) return null;

            using var mccStream = new FileStream(mccPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            // MCC file format: data length (4 bytes) + compressed data
            var lengthBytes = await ReadExactAsync(mccStream, 4);
            
            var dataLength = BitConverter.ToInt32(lengthBytes, 0);
            if (BitConverter.IsLittleEndian)
            {
                dataLength = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(dataLength);
            }

            var compressedData = await ReadExactAsync(mccStream, dataLength);

            var nbtData = DecompressAndParseNbt(compressedData, compressionType);

            return new ChunkData
            {
                CompressionType = compressionType,
                IsExternal = true,
                DataLength = dataLength + 4, // include length header
                NbtData = nbtData
            };
        }

        /// <summary>
        /// Decompress and parse NBT data
        /// </summary>
        private NbtCompound? DecompressAndParseNbt(byte[] compressedData, CompressionType compressionType)
        {
            try
            {
                // 使用CompressionHelper解压缩
                var decompressedData = CompressionHelper.Decompress(compressedData, compressionType);

                // 使用fNbt解析NBT数据
                using var memoryStream = new MemoryStream(decompressedData);
                var nbtFile = new NbtFile();
                nbtFile.LoadFromStream(memoryStream, NbtCompression.None);
                return nbtFile.RootTag;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to decompress and parse NBT data: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Read exactly the specified number of bytes
        /// </summary>
        internal static async Task<byte[]> ReadExactAsync(Stream stream, int count)
        {
            var buffer = new byte[count];
            var totalRead = 0;
            
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer, totalRead, count - totalRead);
                if (read == 0)
                    throw new EndOfStreamException($"Unexpected end of stream. Expected {count} bytes, got {totalRead}");
                totalRead += read;
            }
            
            return buffer;
        }

        /// <summary>
        /// Validate region file integrity
        /// </summary>
        public async Task<ValidationResult> ValidateAsync()
        {
            var result = new ValidationResult();

            try
            {
                if (!_isLoaded) await LoadAsync();

                if (_header == null)
                {
                    result.AddError("Failed to load region header");
                    return result;
                }

                // Validate file size
                var expectedMinSize = 8192; // 8KB header
                if (_stream.Length < expectedMinSize)
                {
                    result.AddError($"File too small: {_stream.Length} bytes, expected at least {expectedMinSize}");
                }

                // Validate each chunk
                for (int i = 0; i < 1024; i++)
                {
                    var chunkInfo = _header.ChunkInfos[i];
                    if (chunkInfo.SectorOffset == 0) continue; // skip non-existent chunk

                    try
                    {
                        var chunkData = await ReadChunkDataAsync(chunkInfo);
                        if (chunkData?.NbtData == null)
                        {
                            result.AddWarning($"Chunk {i} exists but NBT data is null");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddError($"Failed to read chunk {i}: {ex.Message}");
                    }
                }

                result.IsValid = result.Errors.Count == 0;
            }
            catch (Exception ex)
            {
                result.AddError($"Validation failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Extract the specified chunk to a standalone NBT file
        /// </summary>
        public async Task<bool> ExtractChunkAsync(Point2i chunkCoordinates, string outputPath)
        {
            try
            {
                var chunkData = await GetChunkAsync(chunkCoordinates);
                if (chunkData?.NbtData == null) return false;

                var nbtFile = new NbtFile(chunkData.NbtData);
                nbtFile.SaveToFile(outputPath, NbtCompression.None);
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stream?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Region file header information
    /// </summary>
    public class McaHeader
    {
        public ChunkInfo[] ChunkInfos { get; } = new ChunkInfo[1024];
        public uint[] Timestamps { get; } = new uint[1024];

        public static async Task<McaHeader> LoadAsync(Stream stream)
        {
            var header = new McaHeader();

            // Read chunk info table (4KB)
            var infoBuffer = await McaRegionFile.ReadExactAsync(stream, 4096);

            for (int i = 0; i < 1024; i++)
            {
                var offset = i * 4;
                var value = BitConverter.ToUInt32(infoBuffer, offset);
                if (BitConverter.IsLittleEndian)
                {
                    value = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value);
                }

                header.ChunkInfos[i] = new ChunkInfo
                {
                    SectorOffset = (value >> 8) & 0xFFFFFF,
                    SectorCount = value & 0xFF
                };
            }

            // Read timestamp table (4KB)
            var timestampBuffer = await McaRegionFile.ReadExactAsync(stream, 4096);

            for (int i = 0; i < 1024; i++)
            {
                var offset = i * 4;
                var timestamp = BitConverter.ToUInt32(timestampBuffer, offset);
                if (BitConverter.IsLittleEndian)
                {
                    timestamp = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(timestamp);
                }
                header.Timestamps[i] = timestamp;
                header.ChunkInfos[i].Timestamp = timestamp;
            }

            return header;
        }
    }

    /// <summary>
    /// Chunk info
    /// </summary>
    public class ChunkInfo
    {
        public uint SectorOffset { get; set; }
        public uint SectorCount { get; set; }
        public uint Timestamp { get; set; }
    }

    /// <summary>
    /// Chunk data
    /// </summary>
    public class ChunkData
    {
        public CompressionType CompressionType { get; set; }
        public bool IsExternal { get; set; }
        public int DataLength { get; set; }
        public NbtCompound? NbtData { get; set; }
        public uint Timestamp { get; set; }
    }

    /// <summary>
    /// Validation result
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public void AddError(string error) => Errors.Add(error);
        public void AddWarning(string warning) => Warnings.Add(warning);
    }
}
