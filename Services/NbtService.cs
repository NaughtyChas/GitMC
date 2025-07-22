using System.Text;
using SharpNBT;
using SharpNBT.SNBT;

namespace GitMC.Services
{
    public class NbtService : INbtService
    {
        public async Task<string> ConvertNbtToSnbtAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        throw new FileNotFoundException($"File does not exist: {filePath}");
                    }

                    // Use SharpNBT to read the NBT file
                    CompoundTag rootTag;
                    
                    try
                    {
                        // Try reading the compressed NBT file (auto-detect compression type)
                        rootTag = NbtFile.Read(filePath, FormatOptions.Java);
                    }
                    catch
                    {
                        try
                        {
                            // Try reading the Bedrock format
                            rootTag = NbtFile.Read(filePath, FormatOptions.BedrockNetwork);
                        }
                        catch
                        {
                            // Try reading the Bedrock format
                            rootTag = NbtFile.Read(filePath, FormatOptions.BedrockFile);
                        }
                    }

                    // Convert to SNBT format (pretty print)
                    return rootTag.PrettyPrinted();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error when converting NBT to SNBT: {ex.Message}", ex);
                }
            });
        }

        public async Task ConvertSnbtToNbtAsync(string snbtContent, string outputPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(snbtContent))
                    {
                        throw new ArgumentException("SNBT content cannot be empty");
                    }

                    // Parse SNBT string to NBT tag
                    var rootTag = StringNbt.Parse(snbtContent);

                    // Ensure output directory exists
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Determine compression format based on file extension
                    var extension = Path.GetExtension(outputPath).ToLowerInvariant();
                    var compression = extension == ".dat" ? CompressionType.GZip : CompressionType.None;

                    // Write NBT file
                    if (rootTag is { } compound)
                    {
                        NbtFile.Write(outputPath, compound, FormatOptions.Java, compression);
                    }
                    else
                    {
                        // Wrap to a CompoundTag if not a CompoundTag
                        var wrapperTag = new CompoundTag("root");
                        wrapperTag.Add(rootTag);
                        NbtFile.Write(outputPath, wrapperTag, FormatOptions.Java, compression);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error when converting SNBT to NBT: {ex.Message}", ex);
                }
            });
        }

        public async Task<bool> IsValidNbtFileAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        return false;
                    }

                    // Try reading the file
                    try
                    {
                        _ = NbtFile.Read(filePath, FormatOptions.Java);
                        return true;
                    }
                    catch
                    {
                        try
                        {
                            _ = NbtFile.Read(filePath, FormatOptions.BedrockNetwork);
                            return true;
                        }
                        catch
                        {
                            try
                            {
                                _ = NbtFile.Read(filePath, FormatOptions.BedrockFile);
                                return true;
                            }
                            catch
                            {
                                return false;
                            }
                        }
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<string> GetNbtFileInfoAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        return "File does not exist";
                    }

                    var fileInfo = new FileInfo(filePath);
                    
                    var info = new StringBuilder();
                    info.AppendLine($"File path: {filePath}");
                    info.AppendLine($"File size: {fileInfo.Length:N0} bytes");
                    info.AppendLine($"Creation time: {fileInfo.CreationTime}");
                    info.AppendLine($"Last write time: {fileInfo.LastWriteTime}");
                    info.AppendLine();

                    // Try parsing NBT file
                    CompoundTag? rootTag;
                    string compressionType;
                    string formatType;

                    try
                    {
                        rootTag = NbtFile.Read(filePath, FormatOptions.Java);
                        formatType = "Java version";
                        compressionType = DetermineCompressionType(filePath);
                    }
                    catch
                    {
                        try
                        {
                            rootTag = NbtFile.Read(filePath, FormatOptions.BedrockNetwork);
                            formatType = "Bedrock Version (Network)";
                            compressionType = DetermineCompressionType(filePath);
                        }
                        catch
                        {
                            try
                            {
                                rootTag = NbtFile.Read(filePath, FormatOptions.BedrockFile);
                                formatType = "Bedrock Version (File)";
                                compressionType = DetermineCompressionType(filePath);
                            }
                            catch
                            {
                                return info + "Error reading NBT file: Unable to determine format or compression type.";
                            }
                        }
                    }

                    info.AppendLine($"NBT format: {formatType}");
                    info.AppendLine($"Compression type: {compressionType}");
                    info.AppendLine($"Root tag type: {rootTag.Type}");
                    info.AppendLine($"Root tag name: {rootTag.Name ?? "(Unnamed)"}");
                    info.AppendLine($"Child tag count: {rootTag.Count}");

                    if (rootTag.Count > 0)
                    {
                        info.AppendLine("\nChild tags:");
                        foreach (var tag in rootTag)
                        {
                            info.AppendLine($"  - {tag.Name}: {tag.Type}");
                        }
                    }

                    return info.ToString();
                }
                catch (Exception ex)
                {
                    return $"Error reading file info: {ex.Message}";
                }
            });
        }

        public bool IsValidSnbt(string snbtContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(snbtContent))
                {
                    return false;
                }

                _ = StringNbt.Parse(snbtContent);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string DetermineCompressionType(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var firstByte = (byte)stream.ReadByte();
                
                return firstByte switch
                {
                    0x78 => "ZLib compression",
                    0x1F => "GZip compression",
                    0x08 => "Uncompressed (ListTag)",
                    0x0A => "Uncompressed (CompoundTag)",
                    _ => "Unknown compression format"
                };
            }
            catch
            {
                return "Unable to detect";
            }
        }

        #region Anvil Region File Support

        public async Task<AnvilRegionInfo> GetRegionInfoAsync(string mcaFilePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(mcaFilePath))
                        throw new FileNotFoundException($"File does not exist: {mcaFilePath}");

                    var fileInfo = new FileInfo(mcaFilePath);
                    var fileName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    
                    // Parse region coordinates from filename (r.x.z.mca)
                    var parts = fileName.Split('.');
                    if (parts.Length != 3 || parts[0] != "r")
                        throw new InvalidOperationException("Invalid region file name format");

                    int regionX = int.Parse(parts[1]);
                    int regionZ = int.Parse(parts[2]);

                    using var fs = new FileStream(mcaFilePath, FileMode.Open, FileAccess.Read);
                    if (fs.Length < 8192) // 8KB header minimum
                        throw new InvalidOperationException("Invalid region file: too small");

                    // Count valid chunks
                    int validChunks = 0;
                    for (int i = 0; i < 1024; i++)
                    {
                        fs.Seek(i * 4, SeekOrigin.Begin);
                        var locationData = new byte[4];
                        fs.ReadExactly(locationData, 0, 4);
                        
                        if (BitConverter.IsLittleEndian)
                            Array.Reverse(locationData);
                        
                        uint location = BitConverter.ToUInt32(locationData, 0);
                        uint offset = (location >> 8) & 0xFFFFFF;
                        uint sectorCount = location & 0xFF;

                        if (offset >= 2 && sectorCount > 0)
                            validChunks++;
                    }

                    return new AnvilRegionInfo
                    {
                        RegionX = regionX,
                        RegionZ = regionZ,
                        FilePath = mcaFilePath,
                        FileSize = fileInfo.Length,
                        TotalChunks = 1024,
                        ValidChunks = validChunks,
                        LastModified = fileInfo.LastWriteTime
                    };
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error reading region file: {ex.Message}", ex);
                }
            });
        }

        public async Task<List<AnvilChunkInfo>> ListChunksInRegionAsync(string mcaFilePath)
        {
            return await Task.Run(() =>
            {
                var chunks = new List<AnvilChunkInfo>();
                
                try
                {
                    if (!File.Exists(mcaFilePath))
                        throw new FileNotFoundException($"File does not exist: {mcaFilePath}");

                    var regionInfo = GetRegionInfoAsync(mcaFilePath).Result;

                    using var fs = new FileStream(mcaFilePath, FileMode.Open, FileAccess.Read);
                    
                    for (int localZ = 0; localZ < 32; localZ++)
                    {
                        for (int localX = 0; localX < 32; localX++)
                        {
                            int index = localX + localZ * 32;
                            
                            // Read location data
                            fs.Seek(index * 4, SeekOrigin.Begin);
                            var locationData = new byte[4];
                            fs.ReadExactly(locationData, 0, 4);
                            
                            if (BitConverter.IsLittleEndian)
                                Array.Reverse(locationData);
                            
                            uint location = BitConverter.ToUInt32(locationData, 0);
                            uint offset = (location >> 8) & 0xFFFFFF;
                            uint sectorCount = location & 0xFF;

                            // Read timestamp
                            fs.Seek(4096 + index * 4, SeekOrigin.Begin);
                            var timestampData = new byte[4];
                            fs.ReadExactly(timestampData, 0, 4);
                            
                            if (BitConverter.IsLittleEndian)
                                Array.Reverse(timestampData);
                            
                            uint timestamp = BitConverter.ToUInt32(timestampData, 0);

                            bool isValid = offset >= 2 && sectorCount > 0;
                            bool isOversized = false;
                            AnvilCompressionType compressionType = AnvilCompressionType.Zlib;
                            long dataSize = 0;

                            if (isValid)
                            {
                                try
                                {
                                    // Read chunk data header
                                    fs.Seek(offset * 4096, SeekOrigin.Begin);
                                    var chunkHeader = new byte[5];
                                    fs.ReadExactly(chunkHeader, 0, 5);

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(chunkHeader, 0, 4);

                                    dataSize = BitConverter.ToUInt32(chunkHeader, 0);
                                    byte compressionByte = chunkHeader[4];

                                    // Check for oversized chunk
                                    if (dataSize == 1 && (compressionByte & 0x80) != 0)
                                    {
                                        isOversized = true;
                                        compressionType = (AnvilCompressionType)(compressionByte & 0x7F);
                                    }
                                    else
                                    {
                                        compressionType = (AnvilCompressionType)compressionByte;
                                    }
                                }
                                catch
                                {
                                    isValid = false;
                                }
                            }

                            chunks.Add(new AnvilChunkInfo
                            {
                                ChunkX = regionInfo.RegionX * 32 + localX,
                                ChunkZ = regionInfo.RegionZ * 32 + localZ,
                                LocalX = localX,
                                LocalZ = localZ,
                                SectorOffset = offset,
                                SectorCount = sectorCount,
                                Timestamp = timestamp,
                                LastModified = timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime : DateTime.MinValue,
                                DataSize = dataSize,
                                CompressionType = compressionType,
                                IsValid = isValid,
                                IsOversized = isOversized
                            });
                        }
                    }

                    return chunks;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error listing chunks: {ex.Message}", ex);
                }
            });
        }

        public async Task<string> ExtractChunkDataAsync(string mcaFilePath, int chunkX, int chunkZ)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var chunks = ListChunksInRegionAsync(mcaFilePath).Result;
                    var chunk = chunks.FirstOrDefault(c => c.ChunkX == chunkX && c.ChunkZ == chunkZ);
                    
                    if (chunk == null || !chunk.IsValid)
                        throw new InvalidOperationException($"Chunk ({chunkX}, {chunkZ}) not found or invalid");

                    if (chunk.IsOversized)
                    {
                        // Handle oversized chunk (.mcc file)
                        var mccPath = Path.ChangeExtension(mcaFilePath, null) + $".c.{chunkX}.{chunkZ}.mcc";
                        if (File.Exists(mccPath))
                        {
                            return ConvertNbtToSnbtAsync(mccPath).Result;
                        }
                        throw new InvalidOperationException($"Oversized chunk file not found: {mccPath}");
                    }

                    using var fs = new FileStream(mcaFilePath, FileMode.Open, FileAccess.Read);
                    
                    // Seek to chunk data
                    fs.Seek(chunk.SectorOffset * 4096, SeekOrigin.Begin);
                    
                    // Read chunk header
                    var header = new byte[5];
                    fs.ReadExactly(header, 0, 5);
                    
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(header, 0, 4);
                    
                    uint dataLength = BitConverter.ToUInt32(header, 0);
                    byte compressionType = header[4];
                    
                    // Read compressed data
                    var compressedData = new byte[dataLength - 1]; // -1 because dataLength includes compression type byte
                    fs.ReadExactly(compressedData);
                    
                    // Decompress based on compression type
                    byte[] decompressedData;
                    switch ((AnvilCompressionType)compressionType)
                    {
                        case AnvilCompressionType.GZip:
                            using (var gzipStream = new System.IO.Compression.GZipStream(new MemoryStream(compressedData), System.IO.Compression.CompressionMode.Decompress))
                            using (var output = new MemoryStream())
                            {
                                gzipStream.CopyTo(output);
                                decompressedData = output.ToArray();
                            }
                            break;
                            
                        case AnvilCompressionType.Zlib:
                            // SharpNBT handles Zlib automatically
                            decompressedData = compressedData;
                            break;
                            
                        case AnvilCompressionType.Uncompressed:
                            decompressedData = compressedData;
                            break;
                            
                        default:
                            throw new NotSupportedException($"Compression type {compressionType} not supported");
                    }
                    
                    // Create temporary file for NBT processing
                    var tempPath = Path.GetTempFileName();
                    try
                    {
                        File.WriteAllBytes(tempPath, compressionType == (byte)AnvilCompressionType.Zlib ? compressedData : decompressedData);
                        
                        CompoundTag chunkData;
                        if (compressionType == (byte)AnvilCompressionType.Zlib)
                        {
                            chunkData = NbtFile.Read(tempPath, FormatOptions.Java, CompressionType.ZLib);
                        }
                        else
                        {
                            chunkData = NbtFile.Read(tempPath, FormatOptions.Java, CompressionType.None);
                        }
                        
                        return chunkData.PrettyPrinted();
                    }
                    finally
                    {
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error extracting chunk data: {ex.Message}", ex);
                }
            });
        }

        public async Task<bool> IsValidAnvilFileAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(filePath))
                        return false;

                    var extension = Path.GetExtension(filePath).ToLowerInvariant();
                    if (extension != ".mca" && extension != ".mcc")
                        return false;

                    if (extension == ".mcc")
                    {
                        // .mcc files are oversized chunk files, validate as regular NBT
                        return IsValidNbtFileAsync(filePath).Result;
                    }

                    // Validate .mca file structure
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    if (fs.Length < 8192) // Minimum 8KB header
                        return false;

                    // Validate filename format (r.x.z.mca)
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    var parts = fileName.Split('.');
                    if (parts.Length != 3 || parts[0] != "r")
                        return false;

                    if (!int.TryParse(parts[1], out _) || !int.TryParse(parts[2], out _))
                        return false;

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        #endregion
    }
}
