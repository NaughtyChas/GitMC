using System.Text;
using fNbt;

namespace GitMC.Utils.Mca;

/// <summary>
///     MCA file writer for creating region files from chunk data
///     Implements the Minecraft region file format specification for writing
/// </summary>
public class McaRegionWriter : IDisposable
{
    private readonly Dictionary<Point2I, ChunkData> _chunks;
    private readonly string _filePath;
    private bool _disposed;

    public McaRegionWriter(string filePath, Point2I regionCoordinates)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        RegionCoordinates = regionCoordinates;
        _chunks = new Dictionary<Point2I, ChunkData>();
    }

    public McaRegionWriter(string filePath) : this(filePath, CommonHelpers.ExtractRegionCoordinatesFromPath(filePath))
    {
    }

    /// <summary>
    ///     Region coordinates of this writer
    /// </summary>
    public Point2I RegionCoordinates { get; }

    /// <summary>
    ///     Number of chunks currently loaded
    /// </summary>
    public int ChunkCount => _chunks.Count;

    public void Dispose()
    {
        if (!_disposed)
        {
            _chunks.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Add a chunk to the region
    /// </summary>
    /// <param name="chunkCoordinates">Global chunk coordinates</param>
    /// <param name="nbtData">Chunk NBT data</param>
    /// <param name="compressionType">Compression type to use</param>
    /// <param name="timestamp">Chunk timestamp (optional, defaults to current time)</param>
    public void AddChunk(Point2I chunkCoordinates, NbtCompound nbtData,
        CompressionType compressionType = CompressionType.Zlib,
        uint? timestamp = null)
    {
        if (nbtData == null) throw new ArgumentNullException(nameof(nbtData));

        // Validate that the chunk belongs to this region
        Point2I expectedRegion = chunkCoordinates.ChunkToRegion();
        if (expectedRegion != RegionCoordinates)
            throw new ArgumentException(
                $"Chunk {chunkCoordinates} belongs to region {expectedRegion}, not {RegionCoordinates}");

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
    ///     Remove a chunk from the region
    /// </summary>
    public bool RemoveChunk(Point2I chunkCoordinates)
    {
        return _chunks.Remove(chunkCoordinates);
    }

    /// <summary>
    ///     Clear all chunks
    /// </summary>
    public void ClearChunks()
    {
        _chunks.Clear();
    }

    /// <summary>
    ///     Write the region file to disk
    /// </summary>
    public void WriteAsync()
    {
        // Ensure output directory exists
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

        // Write to a temporary file first, then move to final location for atomic operation
        string tempPath = _filePath + ".tmp";
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
            if (File.Exists(_filePath)) File.Delete(_filePath);
            File.Move(tempPath, _filePath);
        }
        catch
        {
            // Clean up temp file on any error
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); }
                catch { }

            throw;
        }
    }

    /// <summary>
    ///     Write the complete region file structure
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
        byte[] chunkDataBytes = chunkDataBuffer.ToArray();
        writer.Write(chunkDataBytes);

        // Step 5: Pad to 4KB boundary if needed
        int totalSize = 8192 + chunkDataBytes.Length; // 8KB headers + chunk data
        int remainder = totalSize % 4096;
        if (remainder != 0)
        {
            int padding = 4096 - remainder;
            writer.Write(new byte[padding]);
        }
    }

    /// <summary>
    ///     Serialize all chunks and calculate their sector positions
    /// </summary>
    private void SerializeChunks(Stream chunkDataStream, Dictionary<int, ChunkSectorInfo> chunkSectors)
    {
        using var writer = new BinaryWriter(chunkDataStream, Encoding.UTF8, true);

        int currentSector = 2; // Start after 8KB headers (2 sectors of 4KB each)

        foreach (KeyValuePair<Point2I, ChunkData> kvp in _chunks)
        {
            Point2I chunkCoordinates = kvp.Key;
            ChunkData chunkData = kvp.Value;

            // Get local coordinates and chunk index
            Point2I localCoords = chunkCoordinates.GetLocalCoordinates();
            int chunkIndex = localCoords.ToChunkIndex();

            // Ensure NBT data is not null
            if (chunkData.NbtData == null)
                throw new InvalidOperationException($"Chunk {chunkCoordinates} has null NBT data");

            // Serialize NBT data
            byte[] nbtData;
            using (var nbtStream = new MemoryStream())
            {
                var nbtFile = new NbtFile(chunkData.NbtData);
                nbtFile.SaveToStream(nbtStream, NbtCompression.None);
                nbtData = nbtStream.ToArray();
            }

            // Compress data
            byte[] compressedData = CompressionHelper.Compress(nbtData, chunkData.CompressionType);

            // Calculate total length (1 byte for compression type + compressed data)
            int totalLength = 1 + compressedData.Length;
            bool isOversized = totalLength > 1044476; // ~1MB limit for in-file storage

            if (isOversized)
                // TODO: Implement external .mcc file writing for oversized chunks
                // For now, we'll compress more aggressively or throw an error
                throw new InvalidOperationException(
                    $"Chunk {chunkCoordinates} is too large ({totalLength} bytes). " +
                    "Oversized chunk support (.mcc files) not yet implemented.");

            // Write chunk data
            long chunkStartPosition = writer.BaseStream.Position;

            // Write length (4 bytes, big endian)
            byte[] lengthBytes = BitConverter.GetBytes((uint)totalLength);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);
            writer.Write(lengthBytes);

            // Write compression type
            writer.Write((byte)chunkData.CompressionType);

            // Write compressed data
            writer.Write(compressedData);

            // Calculate sector count (round up to 4KB sectors)
            int chunkSizeWithHeader = 4 + totalLength; // 4 bytes for length header
            int sectorCount = (chunkSizeWithHeader + 4095) / 4096; // Round up division

            // Pad to sector boundary
            long currentPosition = writer.BaseStream.Position;
            long sectorEndPosition = (currentSector + sectorCount) * 4096L;
            long paddingNeeded = sectorEndPosition - (8192 + currentPosition);

            if (paddingNeeded > 0) writer.Write(new byte[paddingNeeded]);

            // Store sector information
            chunkSectors[chunkIndex] = new ChunkSectorInfo
            {
                SectorOffset = (uint)currentSector,
                SectorCount = (uint)sectorCount,
                Timestamp = chunkData.Timestamp,
                ChunkCoordinates = chunkCoordinates
            };

            currentSector += sectorCount;
        }
    }

    /// <summary>
    ///     Write the chunk location table (first 4KB of region file)
    /// </summary>
    private void WriteChunkLocationTable(BinaryWriter writer, Dictionary<int, ChunkSectorInfo> chunkSectors)
    {
        byte[] locationTable = new byte[4096];

        for (int i = 0; i < 1024; i++)
        {
            uint locationData = 0;

            if (chunkSectors.TryGetValue(i, out ChunkSectorInfo? sectorInfo))
                // Pack sector offset (24 bits) and sector count (8 bits)
                locationData = (sectorInfo.SectorOffset << 8) | (sectorInfo.SectorCount & 0xFF);

            // Write as big endian
            byte[] locationBytes = BitConverter.GetBytes(locationData);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(locationBytes);

            Array.Copy(locationBytes, 0, locationTable, i * 4, 4);
        }

        writer.Write(locationTable);
    }

    /// <summary>
    ///     Write the timestamp table (second 4KB of region file)
    /// </summary>
    private void WriteTimestampTable(BinaryWriter writer, Dictionary<int, ChunkSectorInfo> chunkSectors)
    {
        byte[] timestampTable = new byte[4096];

        for (int i = 0; i < 1024; i++)
        {
            uint timestamp = 0;

            if (chunkSectors.TryGetValue(i, out ChunkSectorInfo? sectorInfo)) timestamp = sectorInfo.Timestamp;

            // Write as big endian
            byte[] timestampBytes = BitConverter.GetBytes(timestamp);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timestampBytes);

            Array.Copy(timestampBytes, 0, timestampTable, i * 4, 4);
        }

        writer.Write(timestampTable);
    }

    /// <summary>
    ///     Internal class to track sector information during writing
    /// </summary>
    private class ChunkSectorInfo
    {
        public uint SectorOffset { get; set; }
        public uint SectorCount { get; set; }
        public uint Timestamp { get; set; }
        public Point2I ChunkCoordinates { get; set; }
    }
}
