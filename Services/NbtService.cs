using System.Text;
using fNbt;
using GitMC.Utils.Nbt;

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
            var nbtFile = new NbtFile();
            nbtFile.LoadFromFile(inputPath);

            // Convert to SNBT format using SnbtCmd's implementation
            var snbtContent = nbtFile.RootTag.ToSnbt(SnbtOptions.DefaultExpanded);
            File.WriteAllText(outputPath, snbtContent, Encoding.UTF8);
        }

        private void ConvertSnbtToNbtFile(string inputPath, string outputPath)
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
            nbtFile.SaveToFile(outputPath, NbtCompression.GZip);
        }

        private void ConvertRegionFileToSnbt(string inputPath, string outputPath)
        {
            // For region files (.mca), we need to extract and convert each chunk
            // This is a simplified implementation - a full implementation would need
            // to handle the region file format properly
            
            // For now, we'll create a placeholder SNBT file indicating the region file
            var regionInfo = new NbtCompound("RegionFile");
            regionInfo.Add(new NbtString("OriginalPath", inputPath));
            regionInfo.Add(new NbtLong("FileSize", new FileInfo(inputPath).Length));
            regionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
            regionInfo.Add(new NbtString("Note", "Region file conversion placeholder - full implementation needed"));
            
            var snbtContent = regionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
            File.WriteAllText(outputPath, snbtContent, Encoding.UTF8);
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

        /// <summary>
        /// Fixes empty lists with Unknown type by replacing them with empty Compound lists
        /// This prevents the "NbtList had no elements and an Unknown ListType" error
        /// </summary>
        private void FixEmptyLists(NbtTag tag)
        {
            if (tag is NbtCompound compound)
            {
                foreach (var child in compound.ToArray()) // ToArray to avoid modification during iteration
                {
                    FixEmptyLists(child);
                }
            }
            else if (tag is NbtList list)
            {
                // Fix empty lists with Unknown type
                if (list.Count == 0 && list.ListType == NbtTagType.Unknown)
                {
                    // Set a concrete type for empty lists (Compound is most common)
                    // We need to replace the list because ListType is read-only
                    var fixedList = new NbtList(list.Name, NbtTagType.Compound);
                    
                    // Replace in parent if it's in a compound
                    if (list.Parent is NbtCompound parent && list.Name != null)
                    {
                        parent.Remove(list.Name);
                        parent.Add(fixedList);
                    }
                }
                else
                {
                    // Recursively fix child tags
                    foreach (var child in list.ToArray())
                    {
                        FixEmptyLists(child);
                    }
                }
            }
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
        // ... (keeping existing anvil region methods for now)
        
        #region Anvil Region File Support - Placeholder
        
        public Task<AnvilRegionInfo> GetRegionInfoAsync(string mcaFilePath)
        {
            // Implementation would be similar but using fNbt types
            throw new NotImplementedException("Region file support needs to be implemented with fNbt");
        }

        public Task<List<AnvilChunkInfo>> ListChunksInRegionAsync(string mcaFilePath)
        {
            throw new NotImplementedException("Region file support needs to be implemented with fNbt");
        }

        public Task<string> ExtractChunkDataAsync(string mcaFilePath, int chunkX, int chunkZ)
        {
            throw new NotImplementedException("Region file support needs to be implemented with fNbt");
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

                    // Basic .mca file validation
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    return fs.Length >= 8192; // Minimum 8KB header
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
