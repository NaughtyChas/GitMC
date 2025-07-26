using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using fNbt;
using GitMC.Utils;

namespace GitMC.Utils.Mca
{
    /// <summary>
    /// MCA file writer for creating region files from chunk data
    /// Implements the Minecraft region file format specification for writing
    /// </summary>
    public class McaRegionWriter : IDisposable
    {
        private readonly string _filePath;
        private readonly Point2i _regionCoordinates;
        private readonly Dictionary<Point2i, ChunkData> _chunks;
        private bool _disposed;

        /// <summary>
        /// Region coordinates of this writer
        /// </summary>
        public Point2i RegionCoordinates => _regionCoordinates;

        /// <summary>
        /// Number of chunks currently loaded
        /// </summary>
        public int ChunkCount => _chunks.Count;

        public McaRegionWriter(string filePath, Point2i regionCoordinates)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _regionCoordinates = regionCoordinates;
            _chunks = new Dictionary<Point2i, ChunkData>();
        }

        public McaRegionWriter(string filePath) : this(filePath, ExtractRegionCoordinatesFromPath(filePath))
        {
        }

        /// <summary>
        /// Add a chunk to the region
        /// </summary>
        /// <param name="chunkCoordinates">Global chunk coordinates</param>
        /// <param name="nbtData">Chunk NBT data</param>
        /// <param name="compressionType">Compression type to use</param>
        /// <param name="timestamp">Chunk timestamp (optional, defaults to current time)</param>
        public void AddChunk(Point2i chunkCoordinates, NbtCompound nbtData, 
            CompressionType compressionType = CompressionType.Zlib, 
            uint? timestamp = null)
        {
            if (nbtData == null) throw new ArgumentNullException(nameof(nbtData));

            // Validate that the chunk belongs to this region
            var expectedRegion = chunkCoordinates.ChunkToRegion();
            if (expectedRegion != _regionCoordinates)
            {
                throw new ArgumentException(
                    $"Chunk {chunkCoordinates} belongs to region {expectedRegion}, not {_regionCoordinates}");
            }

            var chunkData = new ChunkData
            {
                CompressionType = compressionType,
                IsExternal = false, // We'll determine this during writing based on size
                DataLength = 0, // Will be calculated during serialization
                NbtData = nbtData,
                Timestamp = timestamp ?? (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            _chunks[chunkCoordinates] = chunkData;
        }

        /// <summary>
        /// Remove a chunk from the region
        /// </summary>
        public bool RemoveChunk(Point2i chunkCoordinates)
        {
            return _chunks.Remove(chunkCoordinates);
        }

        /// <summary>
        /// Clear all chunks
        /// </summary>
        public void ClearChunks()
        {
            _chunks.Clear();
        }

        /// <summary>
        /// Write the region file to disk
        /// </summary>
        public void WriteAsync()
        {
            // Ensure output directory exists
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temporary file first, then move to final location for atomic operation
            var tempPath = _filePath + ".tmp";
            try
            {
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fileStream))
                {
                    WriteRegionFileAsync(writer);
                    
                    writer.Flush();
                    fileStream.Flush();
                }

                // Atomic move to final location
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
                File.Move(tempPath, _filePath);
            }
            catch
            {
                // Clean up temp file on any error
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        /// <summary>
        /// Write the complete region file structure
        /// </summary>
        private void WriteRegionFileAsync(BinaryWriter writer)
        {
            // Step 1: Serialize all chunks and calculate their positions
            var chunkSectors = new Dictionary<int, ChunkSectorInfo>();
            var chunkDataBuffer = new MemoryStream();
            
            SerializeChunks(chunkDataBuffer, chunkSectors);

            // Step 2: Write chunk location table (4KB)
            WriteChunkLocationTable(writer, chunkSectors);

            // Step 3: Write timestamp table (4KB)
            WriteTimestampTable(writer, chunkSectors);

            // Step 4: Write chunk data
            var chunkDataBytes = chunkDataBuffer.ToArray();
            writer.Write(chunkDataBytes);

            // Step 5: Pad to 4KB boundary if needed
            var totalSize = 8192 + chunkDataBytes.Length; // 8KB headers + chunk data
            var remainder = totalSize % 4096;
            if (remainder != 0)
            {
                var padding = 4096 - remainder;
                writer.Write(new byte[padding]);
            }
        }

        /// <summary>
        /// Serialize all chunks and calculate their sector positions
        /// </summary>
        private void SerializeChunks(Stream chunkDataStream, Dictionary<int, ChunkSectorInfo> chunkSectors)
        {
            using var writer = new BinaryWriter(chunkDataStream, System.Text.Encoding.UTF8, leaveOpen: true);
            
            var currentSector = 2; // Start after 8KB headers (2 sectors of 4KB each)

            foreach (var kvp in _chunks)
            {
                var chunkCoordinates = kvp.Key;
                var chunkData = kvp.Value;

                // Get local coordinates and chunk index
                var localCoords = chunkCoordinates.GetLocalCoordinates();
                var chunkIndex = localCoords.ToChunkIndex();

                // Ensure NBT data is not null
                if (chunkData.NbtData == null)
                {
                    throw new InvalidOperationException($"Chunk {chunkCoordinates} has null NBT data");
                }

                // Serialize NBT data
                byte[] nbtData;
                using (var nbtStream = new MemoryStream())
                {
                    var nbtFile = new NbtFile(chunkData.NbtData);
                    nbtFile.SaveToStream(nbtStream, NbtCompression.None);
                    nbtData = nbtStream.ToArray();
                }

                // Compress data
                var compressedData = CompressionHelper.Compress(nbtData, chunkData.CompressionType);
                
                // Calculate total length (1 byte for compression type + compressed data)
                var totalLength = 1 + compressedData.Length;
                var isOversized = totalLength > 1044476; // ~1MB limit for in-file storage

                if (isOversized)
                {
                    // TODO: Implement external .mcc file writing for oversized chunks
                    // For now, we'll compress more aggressively or throw an error
                    throw new InvalidOperationException(
                        $"Chunk {chunkCoordinates} is too large ({totalLength} bytes). " +
                        "Oversized chunk support (.mcc files) not yet implemented.");
                }

                // Write chunk data
                var chunkStartPosition = writer.BaseStream.Position;
                
                // Write length (4 bytes, big endian)
                var lengthBytes = BitConverter.GetBytes((uint)totalLength);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthBytes);
                writer.Write(lengthBytes);

                // Write compression type
                writer.Write((byte)chunkData.CompressionType);

                // Write compressed data
                writer.Write(compressedData);

                // Calculate sector count (round up to 4KB sectors)
                var chunkSizeWithHeader = 4 + totalLength; // 4 bytes for length header
                var sectorCount = (chunkSizeWithHeader + 4095) / 4096; // Round up division

                // Pad to sector boundary
                var currentPosition = writer.BaseStream.Position;
                var sectorEndPosition = (currentSector + sectorCount) * 4096L;
                var paddingNeeded = sectorEndPosition - (8192 + currentPosition);
                
                if (paddingNeeded > 0)
                {
                    writer.Write(new byte[paddingNeeded]);
                }

                // Store sector information
                chunkSectors[chunkIndex] = new ChunkSectorInfo
                {
                    SectorOffset = (uint)currentSector,
                    SectorCount = (uint)sectorCount,
                    Timestamp = chunkData.Timestamp,
                    ChunkCoordinates = chunkCoordinates
                };

                currentSector += (int)sectorCount;
            }
        }

        /// <summary>
        /// Write the chunk location table (first 4KB of region file)
        /// </summary>
        private void WriteChunkLocationTable(BinaryWriter writer, Dictionary<int, ChunkSectorInfo> chunkSectors)
        {
            var locationTable = new byte[4096];
            
            for (int i = 0; i < 1024; i++)
            {
                uint locationData = 0;
                
                if (chunkSectors.TryGetValue(i, out var sectorInfo))
                {
                    // Pack sector offset (24 bits) and sector count (8 bits)
                    locationData = (sectorInfo.SectorOffset << 8) | (sectorInfo.SectorCount & 0xFF);
                }

                // Write as big endian
                var locationBytes = BitConverter.GetBytes(locationData);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(locationBytes);
                
                Array.Copy(locationBytes, 0, locationTable, i * 4, 4);
            }

            writer.Write(locationTable);
        }

        /// <summary>
        /// Write the timestamp table (second 4KB of region file)
        /// </summary>
        private void WriteTimestampTable(BinaryWriter writer, Dictionary<int, ChunkSectorInfo> chunkSectors)
        {
            var timestampTable = new byte[4096];
            
            for (int i = 0; i < 1024; i++)
            {
                uint timestamp = 0;
                
                if (chunkSectors.TryGetValue(i, out var sectorInfo))
                {
                    timestamp = sectorInfo.Timestamp;
                }

                // Write as big endian
                var timestampBytes = BitConverter.GetBytes(timestamp);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(timestampBytes);
                
                Array.Copy(timestampBytes, 0, timestampTable, i * 4, 4);
            }

            writer.Write(timestampTable);
        }

        /// <summary>
        /// Extract region coordinates from file path
        /// </summary>
        private static Point2i ExtractRegionCoordinatesFromPath(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.StartsWith("r."))
            {
                var parts = fileName.Split('.');
                if (parts.Length >= 3 && 
                    int.TryParse(parts[1], out var x) && 
                    int.TryParse(parts[2], out var z))
                {
                    return new Point2i(x, z);
                }
            }
            
            throw new ArgumentException($"Cannot extract region coordinates from file path: {filePath}");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _chunks.Clear();
                _disposed = true;
            }
        }

        /// <summary>
        /// Internal class to track sector information during writing
        /// </summary>
        private class ChunkSectorInfo
        {
            public uint SectorOffset { get; set; }
            public uint SectorCount { get; set; }
            public uint Timestamp { get; set; }
            public Point2i ChunkCoordinates { get; set; }
        }
    }
}
