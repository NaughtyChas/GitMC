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
                    
                    // Prevent file corruption
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
                        compound.Name = "";
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
                // Use McaRegionFile to parse MCA files
                using var mcaFile = new McaRegionFile(inputPath);
                mcaFile.LoadAsync().Wait();
                
                // Get existing chunks
                var existingChunks = mcaFile.GetExistingChunks();
                
                if (existingChunks.Count == 0)
                {
                    // If there are no chunks, create empty file info
                    // p.s. I am still thinking how the converted snbt file will be stored to be compact and easy to read
                    //      may receive a refactor soon.
                    var emptyRegionInfo = new NbtCompound("EmptyRegion");
                    emptyRegionInfo.Add(new NbtString("OriginalPath", inputPath));
                    emptyRegionInfo.Add(new NbtString("RegionCoordinates", mcaFile.RegionCoordinates.ToString()));
                    emptyRegionInfo.Add(new NbtString("Note", "This region file contains no chunks"));
                    
                    var emptySnbtContent = emptyRegionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                    File.WriteAllText(outputPath, emptySnbtContent, Encoding.UTF8);
                    return;
                }
                
                var snbtContent = new StringBuilder();
                
                // If there is only one chunk, write in NBT directly
                if (existingChunks.Count == 1)
                {
                    var chunkCoord = existingChunks[0];
                    var chunkData = mcaFile.GetChunkAsync(chunkCoord).Result;
                    
                    if (chunkData?.NbtData != null)
                    {
                        snbtContent.AppendLine($"// SNBT for chunk {chunkCoord.X}, {chunkCoord.Z}:");
                        snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                    }
                }
                else
                {
                    // Create multiple SNBT entries if there are multiple chunks
                    bool isFirst = true;
                    foreach (var chunkCoord in existingChunks)
                    {
                        try
                        {
                            var chunkData = mcaFile.GetChunkAsync(chunkCoord).Result;
                            if (chunkData?.NbtData != null)
                            {
                                if (!isFirst)
                                {
                                    snbtContent.AppendLine("// ==========================================");
                                }
                                
                                snbtContent.AppendLine($"// SNBT for chunk {chunkCoord.X}, {chunkCoord.Z}:");
                                snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                                isFirst = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (!isFirst)
                            {
                                snbtContent.AppendLine();
                                snbtContent.AppendLine("// ==========================================");
                                snbtContent.AppendLine();
                            }
                            
                            snbtContent.AppendLine($"// ERROR in chunk {chunkCoord.X}, {chunkCoord.Z}: {ex.Message}");
                            isFirst = false;
                        }
                    }
                }
                
                File.WriteAllText(outputPath, snbtContent.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // If MCA parsing error, switch back to error message
                var errorInfo = new NbtCompound("MCAParsingError");
                errorInfo.Add(new NbtString("OriginalPath", inputPath));
                errorInfo.Add(new NbtLong("FileSize", new FileInfo(inputPath).Length));
                errorInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                errorInfo.Add(new NbtString("Error", ex.Message));
                errorInfo.Add(new NbtString("ErrorType", ex.GetType().Name));
                errorInfo.Add(new NbtString("Note", "MCA parsing failed - this indicates a compression or parsing issue"));
                
                var errorSnbtContent = errorInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                File.WriteAllText(outputPath, errorSnbtContent, Encoding.UTF8);
            }
        }

        private void ConvertSnbtToRegionFile(string inputPath, string outputPath)
        {
            try
            {
                var snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);
                
                // Parse SNBT with multiple chunks
                var chunks = new Dictionary<Point2i, NbtCompound>();
                
                // Check if single or multiple chunks
                if (snbtContent.Contains("// SNBT for chunk") && snbtContent.Contains("// =========================================="))
                {
                    // Split it up using equals:
                    // but I wanna change it in the future, what a waste in storage space T_T
                    var chunkSections = snbtContent.Split(new[] { "// ==========================================" }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var section in chunkSections)
                    {
                        if (section.Trim().StartsWith("// SNBT for chunk"))
                        {
                            var lines = section.Split('\n');
                            var headerLine = lines.FirstOrDefault(l => l.Trim().StartsWith("// SNBT for chunk"));
                            
                            if (headerLine != null)
                            {
                                // Extract chunk cord
                                var coordMatch = System.Text.RegularExpressions.Regex.Match(headerLine, @"chunk (-?\d+), (-?\d+):");
                                if (coordMatch.Success)
                                {
                                    var x = int.Parse(coordMatch.Groups[1].Value);
                                    var z = int.Parse(coordMatch.Groups[2].Value);
                                    var chunkCoord = new Point2i(x, z);
                                    
                                    // Extract SNBT content except comment line
                                    var snbtLines = lines.Where(l => !l.Trim().StartsWith("//")).ToArray();
                                    var chunkSnbt = string.Join("\n", snbtLines);
                                    
                                    if (!string.IsNullOrWhiteSpace(chunkSnbt))
                                    {
                                        var chunkNbt = SnbtParser.Parse(chunkSnbt, false) as NbtCompound;
                                        if (chunkNbt != null)
                                        {
                                            chunks[chunkCoord] = chunkNbt;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (snbtContent.Contains("// SNBT for chunk"))
                {
                    // Single chunk:
                    var lines = snbtContent.Split('\n');
                    var headerLine = lines.FirstOrDefault(l => l.Trim().StartsWith("// SNBT for chunk"));
                    
                    if (headerLine != null)
                    {
                        var coordMatch = System.Text.RegularExpressions.Regex.Match(headerLine, @"chunk (-?\d+), (-?\d+):");
                        if (coordMatch.Success)
                        {
                            var x = int.Parse(coordMatch.Groups[1].Value);
                            var z = int.Parse(coordMatch.Groups[2].Value);
                            var chunkCoord = new Point2i(x, z);
                            
                            var snbtLines = lines.Where(l => !l.Trim().StartsWith("//")).ToArray();
                            var chunkSnbt = string.Join("\n", snbtLines);
                            
                            if (!string.IsNullOrWhiteSpace(chunkSnbt))
                            {
                                var chunkNbt = SnbtParser.Parse(chunkSnbt, false) as NbtCompound;
                                if (chunkNbt != null)
                                {
                                    chunks[chunkCoord] = chunkNbt;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // If not in standard format, try to read entire info as single chunk
                    var rootNbt = SnbtParser.Parse(snbtContent, false) as NbtCompound;
                    if (rootNbt != null)
                    {
                        // Extract cords from NBT tag
                        var xPos = rootNbt.Get<NbtInt>("xPos")?.Value ?? 0;
                        var zPos = rootNbt.Get<NbtInt>("zPos")?.Value ?? 0;
                        chunks[new Point2i(xPos, zPos)] = rootNbt;
                    }
                }
                
                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException("No valid chunk data found in SNBT file");
                }
                
                // Reconstruction of MCA files will be implemented here
                
                // Placeholder for now:
                var reconstructionInfo = new NbtCompound("McaReconstruction");
                reconstructionInfo.Add(new NbtString("OriginalSnbtPath", inputPath));
                reconstructionInfo.Add(new NbtString("TargetMcaPath", outputPath));
                reconstructionInfo.Add(new NbtInt("ChunksFound", chunks.Count));
                reconstructionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                reconstructionInfo.Add(new NbtString("Note", "MCA reconstruction from SNBT - this is a complex operation that requires specialized MCA writing logic"));
                
                var chunksList = new NbtList("Chunks", NbtTagType.Compound);
                foreach (var chunk in chunks)
                {
                    var chunkInfo = new NbtCompound();
                    chunkInfo.Add(new NbtInt("X", chunk.Key.X));
                    chunkInfo.Add(new NbtInt("Z", chunk.Key.Z));
                    chunkInfo.Add(new NbtString("Status", "Parsed successfully"));
                    chunksList.Add(chunkInfo);
                }
                reconstructionInfo.Add(chunksList);
                
                // Save as *.reconstruct.snbt for now
                var infoSnbt = reconstructionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                File.WriteAllText(outputPath + ".reconstruction.snbt", infoSnbt, Encoding.UTF8);
                
                // Create a 8KB sized placeholder MCA file
                var minimalMcaData = new byte[8192];
                File.WriteAllBytes(outputPath, minimalMcaData);
            }
            catch (Exception ex)
            {
                // Create error log if conversion fails
                var errorInfo = $"// Error converting SNBT to MCA: {ex.Message}\n// Original file: {inputPath}\n// Target file: {outputPath}\n// Time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";
                File.WriteAllText(outputPath + ".error.txt", errorInfo, Encoding.UTF8);
                
                // Create an empty MCA file
                var emptyMcaData = new byte[8192];
                File.WriteAllBytes(outputPath, emptyMcaData);
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
                    if (chunkInfo.SectorOffset == 0) continue; // Skip chunks that does not exist

                    var localCoords = Point2i.FromChunkIndex(i);
                    var globalCoords = new Point2i(
                        regionFile.RegionCoordinates.X * 32 + localCoords.X,
                        regionFile.RegionCoordinates.Z * 32 + localCoords.Z
                    );

                    // Attempt to read detailed info from chunks
                    var compressionType = AnvilCompressionType.Zlib; 
                    // For Java saves default is Zlib compression
                    // I think wiki mentioned that server.config can determine the compression level
                    // Maybe we have to add support for that in late stage of development.
                    
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

                // Convert to SNBT
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
                    if (fs.Length < 8192)
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
        /// Convert compression enum
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
