using fNbt;

namespace GitMC.Utils.Mca;

/// <summary>
///     Abstract base class for chunk data
///     Supports different types such as terrain chunk, entities chunk, POI chunk, etc.
/// </summary>
public abstract class Chunk
{
    protected Chunk(Point2I absoluteLocation)
    {
        AbsoluteLocation = absoluteLocation;
        Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    ///     Absolute coordinates of the chunk
    /// </summary>
    public Point2I AbsoluteLocation { get; set; }

    /// <summary>
    ///     NBT data of the chunk
    /// </summary>
    public NbtCompound? Data { get; set; }

    /// <summary>
    ///     Compression type
    /// </summary>
    public CompressionType CompressionType { get; set; } = CompressionType.Zlib;

    /// <summary>
    ///     Timestamp (epoch seconds)
    /// </summary>
    public uint Timestamp { get; set; }

    /// <summary>
    ///     Whether the chunk is empty
    /// </summary>
    public bool IsEmpty => Data == null || Data.Count == 0;

    /// <summary>
    ///     Whether the chunk is oversized (requires external MCC file storage)
    /// </summary>
    public bool IsOversized { get; set; }

    /// <summary>
    ///     Load chunk data from byte buffer
    /// </summary>
    public virtual void Load(BinaryReader reader, bool raw = false)
    {
        if (IsEmpty) return;

        try
        {
            // Read data length (4 bytes, big endian)
            byte[] lengthBytes = reader.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);
            uint dataLength = BitConverter.ToUInt32(lengthBytes, 0);

            // Read compression type (1 byte)
            byte compressionTypeByte = reader.ReadByte();

            // Check if oversized chunk
            IsOversized = CompressionHelper.IsOversized(compressionTypeByte);
            CompressionType = CompressionHelper.GetActualCompressionType(compressionTypeByte);

            // Read compressed data
            byte[] compressedData =
                reader.ReadBytes((int)(dataLength - 1)); // -1 because compression type occupies 1 byte

            // Decompress data
            byte[] decompressedData;
            if (raw)
                decompressedData = compressedData;
            else
                decompressedData = CompressionHelper.Decompress(compressedData, CompressionType);

            // Parse NBT data
            using var nbtStream = new MemoryStream(decompressedData);
            var nbtFile = new NbtFile();
            nbtFile.LoadFromStream(nbtStream, NbtCompression.None);
            Data = nbtFile.RootTag;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to load chunk at {AbsoluteLocation}: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Save chunk data to stream
    /// </summary>
    public virtual int Save(BinaryWriter writer)
    {
        if (IsEmpty || Data == null) return 0;

        try
        {
            // Serialize NBT data
            byte[] nbtData;
            using (var nbtStream = new MemoryStream())
            {
                var nbtFile = new NbtFile(Data);
                nbtFile.SaveToStream(nbtStream, NbtCompression.None);
                nbtData = nbtStream.ToArray();
            }

            // Compress data
            byte[] compressedData = CompressionHelper.Compress(nbtData, CompressionType);

            // Calculate total length (1 byte for compression type + compressed data)
            uint totalLength = (uint)(1 + compressedData.Length);

            // Write data length (4 bytes, big endian)
            byte[] lengthBytes = BitConverter.GetBytes(totalLength);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthBytes);
            writer.Write(lengthBytes);

            // Write compression type
            byte compressionTypeByte = (byte)CompressionType;
            if (IsOversized) compressionTypeByte = CompressionHelper.SetOversizedFlag(CompressionType);
            writer.Write(compressionTypeByte);

            // Write compressed data
            writer.Write(compressedData);

            return (int)(4 + totalLength); // Return total bytes written
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save chunk at {AbsoluteLocation}: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Get the local coordinate of the chunk within the region
    /// </summary>
    public Point2I GetLocalCoordinate()
    {
        return AbsoluteLocation.GetLocalInRegion();
    }

    /// <summary>
    ///     Get the index of the chunk in the region file
    /// </summary>
    public int GetRegionIndex()
    {
        return AbsoluteLocation.GetRegionIndex();
    }

    /// <summary>
    ///     Clone the chunk
    /// </summary>
    public abstract Chunk Clone();

    public override string ToString()
    {
        return $"{GetType().Name} at {AbsoluteLocation} (Empty: {IsEmpty}, Oversized: {IsOversized})";
    }
}

/// <summary>
///     Terrain chunk (region/*.mca)
/// </summary>
public class RegionChunk : Chunk
{
    public RegionChunk(Point2I absoluteLocation) : base(absoluteLocation)
    {
    }

    public override Chunk Clone()
    {
        var clone = new RegionChunk(AbsoluteLocation)
        {
            Data = Data?.Clone() as NbtCompound,
            CompressionType = CompressionType,
            Timestamp = Timestamp,
            IsOversized = IsOversized
        };
        return clone;
    }
}

/// <summary>
///     Entities chunk (entities/*.mca)
/// </summary>
public class EntitiesChunk : Chunk
{
    public EntitiesChunk(Point2I absoluteLocation) : base(absoluteLocation)
    {
    }

    public override Chunk Clone()
    {
        var clone = new EntitiesChunk(AbsoluteLocation)
        {
            Data = Data?.Clone() as NbtCompound,
            CompressionType = CompressionType,
            Timestamp = Timestamp,
            IsOversized = IsOversized
        };
        return clone;
    }
}

/// <summary>
///     Point of interest chunk (poi/*.mca)
/// </summary>
public class PoiChunk : Chunk
{
    public PoiChunk(Point2I absoluteLocation) : base(absoluteLocation)
    {
    }

    public override Chunk Clone()
    {
        var clone = new PoiChunk(AbsoluteLocation)
        {
            Data = Data?.Clone() as NbtCompound,
            CompressionType = CompressionType,
            Timestamp = Timestamp,
            IsOversized = IsOversized
        };
        return clone;
    }
}
