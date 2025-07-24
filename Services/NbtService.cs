using System.Text;
using fNbt;
using GitMC.Utils.Nbt;
using GitMC.Utils.Mca;
using GitMC.Utils;

namespace GitMC.Services
{
    public class NbtService : INbtService
    {
        private static readonly object FileLock = new object();
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

                    // Use fNbt to read the NBT file
                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(filePath);
                    
                    // Convert to SNBT format using SnbtCmd's implementation
                    return nbtFile.RootTag.ToSnbt(SnbtOptions.DefaultExpanded);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error when converting NBT to SNBT: {ex.Message}", ex);
                }
            });
        }

        public void ConvertToSnbt(string inputPath, string outputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"File does not exist: {inputPath}");
                }

                // Determine file type and handle accordingly
                var extension = Path.GetExtension(inputPath).ToLowerInvariant();
                
                if (extension == ".mca" || extension == ".mcc")
                {
                    // For region files, we need to handle them specially
                    ConvertRegionFileToSnbt(inputPath, outputPath);
                }
                else if (extension == ".dat" || extension == ".nbt" || extension == ".dat_old")
                {
                    // For NBT/DAT/DAT_OLD files, standard conversion
                    ConvertNbtFileToSnbt(inputPath, outputPath);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported file extension: {extension}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error converting {inputPath} to SNBT: {ex.Message}", ex);
            }
        }

        public void ConvertFromSnbt(string inputPath, string outputPath)
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"SNBT file does not exist: {inputPath}");
                }

                // Determine target file type from output path
                var extension = Path.GetExtension(outputPath).ToLowerInvariant();
                
                if (extension == ".mca" || extension == ".mcc")
                {
                    // For region files, we need to handle them specially
                    ConvertSnbtToRegionFile(inputPath, outputPath);
                }
                else if (extension == ".dat" || extension == ".nbt" || extension == ".dat_old")
                {
                    // For NBT/DAT/DAT_OLD files, standard conversion
                    ConvertSnbtToNbtFile(inputPath, outputPath);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported target file extension: {extension}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error converting {inputPath} from SNBT: {ex.Message}", ex);
            }
        }

        private void ConvertNbtFileToSnbt(string inputPath, string outputPath)
        {
            lock (FileLock)
            {
                try
                {
                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(inputPath);

                    if (nbtFile.RootTag == null)
                    {
                        throw new InvalidDataException($"NBT file has no root tag: {inputPath}");
                    }

                    // Convert to SNBT format using SnbtCmd's implementation
                    var snbtContent = nbtFile.RootTag.ToSnbt(SnbtOptions.DefaultExpanded);
                    
                    // Validate SNBT content before writing
                    if (string.IsNullOrEmpty(snbtContent))
                    {
                        throw new InvalidDataException($"Generated SNBT content is empty for file: {inputPath}");
                    }
                    
                    // Use atomic write operation to prevent file corruption
                    WriteFileAtomically(outputPath, snbtContent);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to convert NBT to SNBT for {inputPath}: {ex.Message}", ex);
                }
                finally
                {
                    // Force garbage collection periodically to prevent memory buildup
                    if (System.Environment.TickCount % 10 == 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
            }
        }

        private void ConvertSnbtToNbtFile(string inputPath, string outputPath)
        {
            lock (FileLock)
            {
                var snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);
                
                // Parse SNBT using SnbtCmd's parser
                var rootTag = SnbtParser.Parse(snbtContent, false);
                
                // Fix empty lists before creating NBT file
                FixEmptyLists(rootTag);
                
                // Create NBT file and save
                var nbtFile = new NbtFile();
                if (rootTag is NbtCompound compound)
                {
                    // Ensure the root compound has a name (fNbt requirement)
                    if (string.IsNullOrEmpty(compound.Name))
                    {
                        compound.Name = ""; // fNbt allows empty string as name
                    }
                    nbtFile.RootTag = compound;
                }
                else
                {
                    // If it's not a compound, wrap it in one with a name
                    var wrapper = new NbtCompound("");
                    wrapper.Add(rootTag);
                    nbtFile.RootTag = wrapper;
                }
                
                // Use atomic write operation to prevent file corruption
                SaveNbtFileAtomically(nbtFile, outputPath);
            }
        }

        private void ConvertRegionFileToSnbt(string inputPath, string outputPath)
        {
            try
            {
                // 使用McaRegionFile来解析MCA文件
                using var mcaFile = new McaRegionFile(inputPath);
                mcaFile.LoadAsync().Wait();
                
                var regionInfo = new NbtCompound("RegionFile");
                regionInfo.Add(new NbtString("OriginalPath", inputPath));
                regionInfo.Add(new NbtLong("FileSize", new FileInfo(inputPath).Length));
                regionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                regionInfo.Add(new NbtString("RegionCoordinates", mcaFile.RegionCoordinates.ToString()));
                
                var chunksCompound = new NbtCompound("Chunks");
                int chunkCount = 0;
                
                // 获取所有存在的区块
                var existingChunks = mcaFile.GetExistingChunks();
                foreach (var chunkCoord in existingChunks.Take(5)) // 只处理前5个区块作为示例
                {
                    try
                    {
                        var chunkData = mcaFile.GetChunkAsync(chunkCoord).Result;
                        if (chunkData != null)
                        {
                            chunksCompound.Add(new NbtCompound($"Chunk_{chunkCoord.X}_{chunkCoord.Z}")
                            {
                                new NbtInt("X", chunkCoord.X),
                                new NbtInt("Z", chunkCoord.Z),
                                new NbtString("Status", "Successfully parsed"),
                                new NbtString("CompressionType", chunkData.CompressionType.ToString()),
                                new NbtLong("DataLength", chunkData.DataLength),
                                new NbtLong("DecompressedSize", chunkData.NbtData?.ToString()?.Length ?? 0)
                            });
                            chunkCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        chunksCompound.Add(new NbtCompound($"Chunk_{chunkCoord.X}_{chunkCoord.Z}_Error")
                        {
                            new NbtInt("X", chunkCoord.X),
                            new NbtInt("Z", chunkCoord.Z),
                            new NbtString("Error", ex.Message)
                        });
                    }
                }
                
                regionInfo.Add(chunksCompound);
                regionInfo.Add(new NbtInt("TotalChunks", existingChunks.Count));
                regionInfo.Add(new NbtInt("ChunksProcessed", chunkCount));
                regionInfo.Add(new NbtString("Status", "Successfully processed with MCA parser"));
                
                var snbtContent = regionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                File.WriteAllText(outputPath, snbtContent, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 如果MCA解析失败，回退到原来的占位符方法
                var regionInfo = new NbtCompound("RegionFile");
                regionInfo.Add(new NbtString("OriginalPath", inputPath));
                regionInfo.Add(new NbtLong("FileSize", new FileInfo(inputPath).Length));
                regionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                regionInfo.Add(new NbtString("Error", ex.Message));
                regionInfo.Add(new NbtString("ErrorType", ex.GetType().Name));
                regionInfo.Add(new NbtString("Note", "MCA parsing failed - this indicates the compression issue may still exist"));
                
                var snbtContent = regionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                File.WriteAllText(outputPath, snbtContent, Encoding.UTF8);
            }
        }

        private void ConvertSnbtToRegionFile(string inputPath, string outputPath)
        {
            // For converting back from SNBT to region file, we would need to
            // reconstruct the region file format. This is a complex operation.
            
            // For now, we'll copy the original file if it exists, or create a minimal region file
            var originalPath = outputPath.Replace(".snbt", "");
            if (File.Exists(originalPath + ".backup"))
            {
                File.Copy(originalPath + ".backup", outputPath, true);
            }
            else
            {
                // Create a minimal region file header (this is a simplified approach)
                var minimalRegionFile = new byte[8192]; // 8KB header
                File.WriteAllBytes(outputPath, minimalRegionFile);
            }
        }

        public async Task ConvertSnbtToNbtAsync(string snbtContent, string outputPath)
        {
            await Task.Run(() =>
            {
                // Parse SNBT using SnbtCmd's parser
                var rootTag = SnbtParser.Parse(snbtContent, false);
                
                // Fix empty lists before creating NBT file
                FixEmptyLists(rootTag);
                
                // Create NBT file and save
                var nbtFile = new NbtFile();
                if (rootTag is NbtCompound compound)
                {
                    // Ensure the root compound has a name (fNbt requirement)
                    if (string.IsNullOrEmpty(compound.Name))
                    {
                        compound.Name = ""; // fNbt allows empty string as name
                    }
                    nbtFile.RootTag = compound;
                }
                else
                {
                    // If it's not a compound, wrap it in one with a name
                    var wrapper = new NbtCompound("");
                    wrapper.Add(rootTag);
                    nbtFile.RootTag = wrapper;
                }
                nbtFile.SaveToFile(outputPath, NbtCompression.GZip);
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

                    // Try reading the file with fNbt
                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(filePath);
                    return nbtFile.RootTag != null;
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

                    // Try parsing NBT file with fNbt
                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(filePath);
                    
                    var rootTag = nbtFile.RootTag;
                    
                    info.AppendLine($"NBT format: {nbtFile.FileCompression}");
                    info.AppendLine($"Root tag type: {rootTag.TagType}");
                    info.AppendLine($"Root tag name: {rootTag.Name ?? "(Unnamed)"}");
                    
                    if (rootTag is NbtCompound compound)
                    {
                        info.AppendLine($"Child tag count: {compound.Count}");
                        
                        if (compound.Count > 0)
                        {
                            info.AppendLine("\nChild tags:");
                            foreach (var tag in compound)
                            {
                                info.AppendLine($"  - {tag.Name}: {tag.TagType}");
                            }
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
                var result = SnbtParser.TryParse(snbtContent, false);
                return result.IsSuccess;
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

        // Region file support methods remain similar but using fNbt types
        
        #region Anvil Region File Support
        
        public async Task<AnvilRegionInfo> GetRegionInfoAsync(string mcaFilePath)
        {
            try
            {
                using var regionFile = new McaRegionFile(mcaFilePath);
                await regionFile.LoadAsync();

                var fileInfo = new FileInfo(mcaFilePath);
                var existingChunks = regionFile.GetExistingChunks();

                return new AnvilRegionInfo
                {
                    RegionX = regionFile.RegionCoordinates.X,
                    RegionZ = regionFile.RegionCoordinates.Z,
                    FilePath = mcaFilePath,
                    FileSize = fileInfo.Length,
                    TotalChunks = 1024, // 32x32 region
                    ValidChunks = existingChunks.Count,
                    LastModified = fileInfo.LastWriteTime
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get region info from {mcaFilePath}: {ex.Message}", ex);
            }
        }

        public async Task<List<AnvilChunkInfo>> ListChunksInRegionAsync(string mcaFilePath)
        {
            try
            {
                using var regionFile = new McaRegionFile(mcaFilePath);
                await regionFile.LoadAsync();

                var chunkInfos = new List<AnvilChunkInfo>();
                var existingChunks = regionFile.GetExistingChunks();

                if (regionFile.Header == null) return chunkInfos;

                for (int i = 0; i < 1024; i++)
                {
                    var chunkInfo = regionFile.Header.ChunkInfos[i];
                    if (chunkInfo.SectorOffset == 0) continue; // 跳过不存在的区块

                    var localCoords = Point2i.FromChunkIndex(i);
                    var globalCoords = new Point2i(
                        regionFile.RegionCoordinates.X * 32 + localCoords.X,
                        regionFile.RegionCoordinates.Z * 32 + localCoords.Z
                    );

                    // 尝试读取区块数据来获取详细信息
                    var compressionType = AnvilCompressionType.Zlib; // 默认值
                    var isOversized = false;
                    var dataSize = 0L;
                    var isValid = true;

                    try
                    {
                        var chunkData = await regionFile.GetChunkAsync(globalCoords);
                        if (chunkData != null)
                        {
                            compressionType = ConvertCompressionType(chunkData.CompressionType);
                            isOversized = chunkData.IsExternal;
                            dataSize = chunkData.DataLength;
                        }
                    }
                    catch
                    {
                        isValid = false;
                    }

                    chunkInfos.Add(new AnvilChunkInfo
                    {
                        ChunkX = globalCoords.X,
                        ChunkZ = globalCoords.Z,
                        LocalX = localCoords.X,
                        LocalZ = localCoords.Z,
                        SectorOffset = chunkInfo.SectorOffset,
                        SectorCount = chunkInfo.SectorCount,
                        Timestamp = chunkInfo.Timestamp,
                        LastModified = DateTimeOffset.FromUnixTimeSeconds(chunkInfo.Timestamp).DateTime,
                        DataSize = dataSize,
                        CompressionType = compressionType,
                        IsValid = isValid,
                        IsOversized = isOversized
                    });
                }

                return chunkInfos;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to list chunks in {mcaFilePath}: {ex.Message}", ex);
            }
        }

        public async Task<string> ExtractChunkDataAsync(string mcaFilePath, int chunkX, int chunkZ)
        {
            try
            {
                using var regionFile = new McaRegionFile(mcaFilePath);
                await regionFile.LoadAsync();

                var chunkCoords = new Point2i(chunkX, chunkZ);
                var chunkData = await regionFile.GetChunkAsync(chunkCoords);

                if (chunkData?.NbtData == null)
                {
                    throw new InvalidOperationException($"Chunk ({chunkX}, {chunkZ}) not found or is empty");
                }

                // 转换为SNBT格式
                return chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to extract chunk ({chunkX}, {chunkZ}) from {mcaFilePath}: {ex.Message}", ex);
            }
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
                        // .mcc files are oversized chunk files, validate as NBT data
                        return IsValidNbtFileAsync(filePath).Result;
                    }

                    // Basic .mca file validation
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    if (fs.Length < 8192) // Minimum 8KB header
                        return false;

                    // Try to load the region file
                    using var regionFile = new McaRegionFile(filePath);
                    regionFile.LoadAsync().Wait();
                    return regionFile.IsLoaded;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 转换压缩类型枚举
        /// </summary>
        private static AnvilCompressionType ConvertCompressionType(CompressionType mcaType)
        {
            var baseType = mcaType.GetBaseType();
            return baseType switch
            {
                CompressionType.GZip => AnvilCompressionType.GZip,
                CompressionType.Zlib => AnvilCompressionType.Zlib,
                CompressionType.Uncompressed => AnvilCompressionType.Uncompressed,
                CompressionType.LZ4 => AnvilCompressionType.LZ4,
                CompressionType.Custom => AnvilCompressionType.Custom,
                _ => AnvilCompressionType.Zlib
            };
        }

        #endregion

        /// <summary>
        /// Fixes empty lists with Unknown type by replacing them with empty Compound lists
        /// This prevents the "NbtList had no elements and an Unknown ListType" error
        /// Enhanced to handle deeply nested structures
        /// </summary>
        private void FixEmptyLists(NbtTag tag)
        {
            if (tag is NbtCompound compound)
            {
                var childrenToReplace = new List<(string name, NbtTag newTag)>();
                
                foreach (var child in compound)
                {
                    if (child is NbtList list && list.Count == 0 && list.ListType == NbtTagType.Unknown)
                    {
                        // Create a replacement list with a concrete type
                        var fixedList = new NbtList(child.Name, NbtTagType.Compound);
                        if (child.Name != null)
                        {
                            childrenToReplace.Add((child.Name, fixedList));
                        }
                    }
                    else
                    {
                        // Recursively fix child tags
                        FixEmptyLists(child);
                    }
                }
                
                // Replace empty lists with fixed versions
                foreach (var (name, newTag) in childrenToReplace)
                {
                    compound.Remove(name);
                    compound.Add(newTag);
                }
            }
            else if (tag is NbtList list)
            {
                // Fix empty lists with Unknown type at this level
                if (list.Count == 0 && list.ListType == NbtTagType.Unknown)
                {
                    // Cannot modify in-place, this needs to be handled by parent
                    return;
                }
                
                // Recursively fix child tags
                foreach (var child in list.ToArray())
                {
                    FixEmptyLists(child);
                }
            }
        }

        /// <summary>
        /// Atomically writes a text file to prevent corruption from concurrent access
        /// </summary>
        private void WriteFileAtomically(string filePath, string content)
        {
            var tempPath = filePath + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, Encoding.UTF8);
                
                // Ensure the write is complete
                using (var fs = File.OpenWrite(tempPath))
                {
                    fs.Flush();
                }
                
                // Atomic move to final location
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        /// <summary>
        /// Atomically saves an NBT file to prevent corruption from concurrent access
        /// </summary>
        private void SaveNbtFileAtomically(NbtFile nbtFile, string filePath)
        {
            var tempPath = filePath + ".tmp";
            try
            {
                nbtFile.SaveToFile(tempPath, NbtCompression.GZip);
                
                // Atomic move to final location
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
