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
                    // For nbt/dat/dat_old files, standard conversion
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
        
        /// <summary>
        /// To prevent UI blocking issue we use this async method of file conversion:
        /// </summary>
        public async Task ConvertToSnbtAsync(string inputPath, string outputPath, IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report($"Checking file: {Path.GetFileName(inputPath)}...");
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"File does not exist: {inputPath}");
                }

                // Check file type and their actions
                var extension = Path.GetExtension(inputPath).ToLowerInvariant();
                
                if (extension == ".mca" || extension == ".mcc")
                {
                    // Special handler for region files
                    progress?.Report("Translating mca to snbt...");
                    // Use await instead of .Wait() to prevent blocking
                    await ConvertRegionFileToSnbtAsync(inputPath, outputPath, progress);
                }
                else if (extension == ".dat" || extension == ".nbt" || extension == ".dat_old")
                {
                    // Standard nbt/dat conversion
                    progress?.Report("Translating nbt/dat file to snbt...");
                    await Task.Run(() => ConvertNbtFileToSnbt(inputPath, outputPath));
                }
                else
                {
                    throw new NotSupportedException($"Extension is not supported: {extension}");
                }
                
                progress?.Report($"Conversion complete! Saved to {Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                progress?.Report($"Conversion failed: {ex.Message}");
                throw new InvalidOperationException($"Converting {inputPath} to snbt failed: {ex.Message}", ex);
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
        
        /// <summary>
        /// Async snbt conversion method with progress returning
        /// </summary>
        public async Task ConvertFromSnbtAsync(string inputPath, string outputPath, IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report($"Reading snbt files: {Path.GetFileName(inputPath)}...");
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException($"snbt file does not exist: {inputPath}");
                }

                // Check target file type
                var extension = Path.GetExtension(outputPath).ToLowerInvariant();
                
                if (extension == ".mca" || extension == ".mcc")
                {
                    progress?.Report("Converting to mca from snbt...");
                    await ConvertSnbtToRegionFileAsync(inputPath, outputPath, progress);
                }
                else if (extension == ".dat" || extension == ".nbt" || extension == ".dat_old")
                {
                    // Standard nbt/dat conversion
                    progress?.Report("Converting snbt to nbt...");
                    await Task.Run(() => ConvertSnbtToNbtFile(inputPath, outputPath));
                }
                else
                {
                    throw new NotSupportedException($"File extension not supported: {extension}");
                }
                
                progress?.Report($"Conversion complete! Saved to {Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                progress?.Report($"Error in conversion: {ex.Message}");
                throw new InvalidOperationException($"Error when converting {inputPath} to target file : {ex.Message}", ex);
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
                    if (Environment.TickCount % 10 == 0)
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
                    var wrapper = new NbtCompound("") { rootTag };
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
                    var emptyRegionInfo = new NbtCompound("EmptyRegion");
                    emptyRegionInfo.Add(new NbtString("OriginalPath", inputPath));
                    emptyRegionInfo.Add(new NbtString("RegionCoordinates", mcaFile.RegionCoordinates.ToString()));
                    emptyRegionInfo.Add(new NbtString("Note", "This region file contains no chunks"));

                    var emptySnbtContent = emptyRegionInfo.ToSnbt(SnbtOptions.DefaultExpanded);

                    // Use Stream instead
                    using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(emptySnbtContent);
                    }

                    return;
                }

                var snbtContent = new StringBuilder();

                // If there is only one chunk, write in NBT directly
                if (existingChunks.Count == 1)
                {
                    var chunkCoord = existingChunks[0];
                    var chunkData = mcaFile.GetChunkAsync(chunkCoord).Result;

                    // Add region information header even for single chunk
                    snbtContent.AppendLine($"// Region file: {Path.GetFileName(inputPath)}");
                    snbtContent.AppendLine($"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                    snbtContent.AppendLine($"// Total chunks: 1");
                    snbtContent.AppendLine();

                    if (chunkData?.NbtData != null)
                    {
                        snbtContent.AppendLine($"// Chunk({chunkCoord.X},{chunkCoord.Z})");
                        snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                    }
                }
                else
                {
                    // Create multiple SNBT entries if there are multiple chunks
                    // Add a header with region information
                    snbtContent.AppendLine($"// Region file: {Path.GetFileName(inputPath)}");
                    snbtContent.AppendLine($"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                    snbtContent.AppendLine($"// Total chunks: {existingChunks.Count}");
                    snbtContent.AppendLine();
                    
                    foreach (var chunkCoord in existingChunks)
                    {
                        try
                        {
                            var chunkData = mcaFile.GetChunkAsync(chunkCoord).Result;
                            if (chunkData?.NbtData != null)
                            {
                                // Simplified chunk header
                                snbtContent.AppendLine($"// Chunk({chunkCoord.X},{chunkCoord.Z})");
                                snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                                snbtContent.AppendLine(); // Just add a blank line between chunks
                            }
                        }
                        catch (Exception ex)
                        {
                            snbtContent.AppendLine($"// ERROR in chunk {chunkCoord.X}, {chunkCoord.Z}: {ex.Message}");
                            snbtContent.AppendLine();
                        }
                    }
                }

                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(snbtContent.ToString());
                        writer.Flush();
                        fs.Flush();
                    }
                }
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
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(errorSnbtContent);
                        writer.Flush();
                    }
                }
            }
        }
        
        /// <summary>
        /// Async translate region file to snbt
        /// </summary>
        private async Task ConvertRegionFileToSnbtAsync(string inputPath, string outputPath, IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report($"Loading region file: {Path.GetFileName(inputPath)}...");
                
                // Parse mca file using McaRegionFile
                using var mcaFile = new McaRegionFile(inputPath);
                await mcaFile.LoadAsync();

                // Get current chunk
                progress?.Report("Scanning chunk...");
                var existingChunks = mcaFile.GetExistingChunks();

                if (existingChunks.Count == 0)
                {
                    progress?.Report("No chunk is found，creating empty region info...");
                    // ... and do stuff just like the text said ↑
                    var emptyRegionInfo = new NbtCompound("EmptyRegion");
                    emptyRegionInfo.Add(new NbtString("OriginalPath", inputPath));
                    emptyRegionInfo.Add(new NbtString("RegionCoordinates", mcaFile.RegionCoordinates.ToString()));
                    emptyRegionInfo.Add(new NbtString("Note", "This region file contains no chunks"));

                    var emptySnbtContent = emptyRegionInfo.ToSnbt(SnbtOptions.DefaultExpanded);

                    // Write file using stream
                    using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        await writer.WriteAsync(emptySnbtContent);
                        await writer.FlushAsync();
                        await fs.FlushAsync();
                    }

                    progress?.Report("Created empty region info");
                    return;
                }

                var snbtContent = new StringBuilder();
                progress?.Report($"Found {existingChunks.Count} chunks，converting...");

                // Directly write to nbt if there is only one chunk
                if (existingChunks.Count == 1)
                {
                    var chunkCoord = existingChunks[0];
                    progress?.Report($"Converting single chunk ({chunkCoord.X}, {chunkCoord.Z})...");
                    var chunkData = await mcaFile.GetChunkAsync(chunkCoord);

                    // Add region information header even for single chunk
                    snbtContent.AppendLine($"// Region file: {Path.GetFileName(inputPath)}");
                    snbtContent.AppendLine($"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                    snbtContent.AppendLine($"// Total chunks: 1");
                    snbtContent.AppendLine();

                    if (chunkData?.NbtData != null)
                    {
                        snbtContent.AppendLine($"// Chunk({chunkCoord.X},{chunkCoord.Z})");
                        snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                    }
                }
                else
                {
                    // Create multiple SNBT entries when there are multiple chunks
                    // Add a header with region information
                    snbtContent.AppendLine($"// Region file: {Path.GetFileName(inputPath)}");
                    snbtContent.AppendLine($"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                    snbtContent.AppendLine($"// Total chunks: {existingChunks.Count}");
                    snbtContent.AppendLine();
                    
                    int processedCount = 0;
                    
                    foreach (var chunkCoord in existingChunks)
                    {
                        try
                        {
                            // Report progress every 10 chunks
                            if (processedCount % 10 == 0 || processedCount == existingChunks.Count - 1)
                            {
                                progress?.Report($"Converting chunk {processedCount + 1}/{existingChunks.Count}...");
                            }
                            
                            var chunkData = await mcaFile.GetChunkAsync(chunkCoord);
                            if (chunkData?.NbtData != null)
                            {
                                // Simplified chunk header
                                snbtContent.AppendLine($"// Chunk({chunkCoord.X},{chunkCoord.Z})");
                                snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                                snbtContent.AppendLine(); // Just add a blank line between chunks
                            }
                        }
                        catch (Exception ex)
                        {
                            snbtContent.AppendLine($"// ERROR in chunk {chunkCoord.X}, {chunkCoord.Z}: {ex.Message}");
                            snbtContent.AppendLine();
                        }
                        
                        processedCount++;
                    }
                }

                progress?.Report("Saving snbt files...");
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        await writer.WriteAsync(snbtContent.ToString());
                        await writer.FlushAsync();
                        await fs.FlushAsync();
                    }
                }
                
                progress?.Report($"snbt conversion complete! Converted {existingChunks.Count} chunks");
            }
            catch (Exception ex)
            {
                progress?.Report($"Error when translating region file: {ex.Message}");
                
                // If mca parsing error occur, use error message 
                var errorInfo = new NbtCompound("MCAParsingError");
                errorInfo.Add(new NbtString("OriginalPath", inputPath));
                errorInfo.Add(new NbtLong("FileSize", new FileInfo(inputPath).Length));
                errorInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                errorInfo.Add(new NbtString("Error", ex.Message));
                errorInfo.Add(new NbtString("ErrorType", ex.GetType().Name));
                errorInfo.Add(new NbtString("Note", "MCA parsing failed - this indicates a compression or parsing issue"));

                var errorSnbtContent = errorInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        await writer.WriteAsync(errorSnbtContent);
                        await writer.FlushAsync();
                    }
                }
                
                throw;
            }
        }

        public void ConvertSnbtToRegionFile(string inputPath, string outputPath)
        {
            try
            {
                var snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);
                
                // Parse SNBT with multiple chunks
                var chunks = new Dictionary<Point2i, NbtCompound>();
                
                // Check if single or multiple chunks
                // First, check for old format with separators
                if (snbtContent.Contains("// SNBT for chunk") && snbtContent.Contains("// =========================================="))
                {
                    // Split it up using equals:
                    ReadOnlySpan<char> content = snbtContent.AsSpan();
                    const string separator = "// ==========================================";
                    
                    int currentPos = 0;
                    while (currentPos < content.Length)
                    {
                        // Find next separator
                        int sepIndex = content.Slice(currentPos).IndexOf(separator.AsSpan());
                        int sectionEnd = sepIndex >= 0 ? currentPos + sepIndex : content.Length;
                        
                        // Extract section without allocating substring
                        ReadOnlySpan<char> section = content.Slice(currentPos, sectionEnd - currentPos);
                        
                        // Process section if it contains chunk header
                        if (section.IndexOf("// SNBT for chunk".AsSpan()) >= 0)
                        {
                            ProcessChunkSectionSpan(section, chunks);
                        }
                        
                        // Move to next section
                        currentPos = sepIndex >= 0 ? sectionEnd + separator.Length : content.Length;
                    }
                }
                else if (snbtContent.Contains("// SNBT for chunk") || snbtContent.Contains("// Chunk("))
                {
                    // Check if this is multiple chunks without separators (new format)
                    // Count the number of chunk headers to determine if it's multi-chunk
                    int chunkCount = 0;
                    var lines = snbtContent.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("// SNBT for chunk") || line.Contains("// Chunk("))
                        {
                            chunkCount++;
                        }
                    }
                    
                    if (chunkCount > 1)
                    {
                        // Multiple chunks without separators - parse each chunk section
                        ProcessMultipleChunksWithoutSeparator(snbtContent, chunks);
                    }
                    else
                    {
                        // Single chunk - process with span to avoid String.Split
                        ReadOnlySpan<char> content = snbtContent.AsSpan();
                        ProcessChunkSectionSpan(content, chunks);
                    }
                }
                else
                {
                    // If not in standard format, try to read entire info as single chunk
                    var rootNbt = SnbtParser.Parse(snbtContent, false) as NbtCompound;
                    if (rootNbt != null)
                    {
                        // Make sure the root tag has a name
                        if (string.IsNullOrEmpty(rootNbt.Name))
                        {
                            rootNbt.Name = "";
                        }
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
                
                // Calculate correct region coordinates from chunk coordinates based on first chunk
                var firstChunk = chunks.First().Key;
                int regionX = (int)Math.Floor(firstChunk.X / 32.0);
                int regionZ = (int)Math.Floor(firstChunk.Z / 32.0);
                var regionCoords = new Point2i(regionX, regionZ);
                
                // Verify all chunks belong to the same region
                foreach (var chunk in chunks)
                {
                    int chunkRegionX = (int)Math.Floor(chunk.Key.X / 32.0);
                    int chunkRegionZ = (int)Math.Floor(chunk.Key.Z / 32.0);
                    
                    if (chunkRegionX != regionX || chunkRegionZ != regionZ)
                    {
                        throw new InvalidOperationException($"Chunk ({chunk.Key.X}, {chunk.Key.Z}) belongs to region ({chunkRegionX}, {chunkRegionZ}), not ({regionX}, {regionZ})");
                    }
                }
                
                // Create MCA writer with correct region coordinates
                using var mcaWriter = new McaRegionWriter(outputPath, regionCoords);
                
                // Add all chunks to the writer
                foreach (var chunk in chunks)
                {
                    // Fix any empty lists with Unknown type before writing
                    FixEmptyLists(chunk.Value);
                    
                    mcaWriter.AddChunk(
                        chunk.Key, 
                        chunk.Value, 
                        CompressionType.Zlib, // Default to Zlib compression
                        (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    );
                }
                
                // Write the MCA file
                mcaWriter.WriteAsync();
                
                // Also create reconstruction info for debugging
                var reconstructionInfo = new NbtCompound("McaReconstruction");
                reconstructionInfo.Add(new NbtString("OriginalSnbtPath", inputPath));
                reconstructionInfo.Add(new NbtString("TargetMcaPath", outputPath));
                reconstructionInfo.Add(new NbtInt("ChunksFound", chunks.Count));
                reconstructionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                reconstructionInfo.Add(new NbtString("RegionX", regionCoords.X.ToString()));
                reconstructionInfo.Add(new NbtString("RegionZ", regionCoords.Z.ToString()));
                reconstructionInfo.Add(new NbtString("Status", "Successfully converted to MCA"));
                
                var chunksList = new NbtList("Chunks", NbtTagType.Compound);
                foreach (var chunk in chunks)
                {
                    var chunkInfo = new NbtCompound();
                    chunkInfo.Add(new NbtInt("X", chunk.Key.X));
                    chunkInfo.Add(new NbtInt("Z", chunk.Key.Z));
                    chunkInfo.Add(new NbtString("Status", "Written to MCA"));
                    
                    // Add some basic chunk validation info
                    var xPos = chunk.Value.Get<NbtInt>("xPos")?.Value ?? -999;
                    var zPos = chunk.Value.Get<NbtInt>("zPos")?.Value ?? -999;
                    chunkInfo.Add(new NbtString("ValidationStatus", 
                        (xPos == chunk.Key.X && zPos == chunk.Key.Z) ? "Coordinates match" : "Coordinate mismatch"));
                    
                    chunksList.Add(chunkInfo);
                }
                reconstructionInfo.Add(chunksList);
                
                // Save reconstruction info
                var infoSnbt = reconstructionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                var infoPath = Path.ChangeExtension(outputPath, ".reconstruction.snbt");
                using (var fs = new FileStream(infoPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(infoSnbt);
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                // Create error log if conversion fails
                var errorInfo = $"// Error converting SNBT to MCA: {ex.Message}\n// Original file: {inputPath}\n// Target file: {outputPath}\n// Time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";
                using (var fs = new FileStream(outputPath + ".error.txt", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(errorInfo);
                        writer.Flush();
                    }
                }

                // Create an empty MCA file
                var emptyMcaData = new byte[8192];
                File.WriteAllBytes(outputPath, emptyMcaData);
                
                // Gonna re-throw exception to let the exception be successfully intercepted by the caller
                throw;
            }
        }
        
        /// <summary>
        /// Async snbt -> region
        /// </summary>
        public async Task ConvertSnbtToRegionFileAsync(string inputPath, string outputPath, IProgress<string>? progress = null)
        {
            await Task.Run(() =>
            {
                try
                {
                    progress?.Report($"Reading snbt file {Path.GetFileName(inputPath)}...");
                    var snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);
                    
                    progress?.Report("Parsing snbt content...");
                    // Parse SNBT with multiple chunks
                    var chunks = new Dictionary<Point2i, NbtCompound>();
                    
                    // Check if single or multiple chunks
                    if (snbtContent.Contains("// SNBT for chunk") && snbtContent.Contains("// =========================================="))
                    {
                        progress?.Report("This snbt file contain multiple chunks, processing...");
                        // Multiple chunks - use spans and string reading to avoid massive string splitting
                        ReadOnlySpan<char> content = snbtContent.AsSpan();
                        const string separator = "// ==========================================";
                        
                        int currentPos = 0;
                        while (currentPos < content.Length)
                        {
                            // Find next separator
                            int sepIndex = content.Slice(currentPos).IndexOf(separator.AsSpan());
                            int sectionEnd = sepIndex >= 0 ? currentPos + sepIndex : content.Length;
                            
                            // Extract section without allocating substring
                            ReadOnlySpan<char> section = content.Slice(currentPos, sectionEnd - currentPos);
                            
                            // Process section if it contains chunk header
                            if (section.IndexOf("// SNBT for chunk".AsSpan()) >= 0)
                            {
                                ProcessChunkSectionSpan(section, chunks);
                            }
                            
                            // Move to next section
                            currentPos = sepIndex >= 0 ? sectionEnd + separator.Length : content.Length;
                        }
                    }
                    else if (snbtContent.Contains("// SNBT for chunk") || snbtContent.Contains("// Chunk("))
                    {
                        // Check if this is multiple chunks without separators (new format)
                        // Count the number of chunk headers to determine if it's multi-chunk
                        int totalChunks = 0;
                        var lines = snbtContent.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("// SNBT for chunk") || line.Contains("// Chunk("))
                            {
                                totalChunks++;
                            }
                        }
                        
                        if (totalChunks > 1)
                        {
                            progress?.Report($"This snbt file contains {totalChunks} chunks, processing...");
                            // Multiple chunks without separators - parse each chunk section
                            ProcessMultipleChunksWithoutSeparator(snbtContent, chunks);
                        }
                        else
                        {
                            progress?.Report("This snbt file contain single chunk，processing...");
                            // Single chunk - process with span to avoid String.Split
                            ReadOnlySpan<char> content = snbtContent.AsSpan();
                            ProcessChunkSectionSpan(content, chunks);
                        }
                    }
                    else
                    {
                        progress?.Report("Expecting a standard format，read as a single chunk file instead...");
                        // If not in standard format, try to read entire info as single chunk
                        var rootNbt = SnbtParser.Parse(snbtContent, false) as NbtCompound;
                        if (rootNbt != null)
                        {
                            // Make sure NBT compound has a name
                            if (string.IsNullOrEmpty(rootNbt.Name))
                            {
                                rootNbt.Name = "";
                            }
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
                    
                    progress?.Report($"Parsing success! Found {chunks.Count} chunks");
                    
                    // Calculate correct region coordinates from chunk coordinates based on first chunk
                    var firstChunk = chunks.First().Key;
                    int regionX = (int)Math.Floor(firstChunk.X / 32.0);
                    int regionZ = (int)Math.Floor(firstChunk.Z / 32.0);
                    var regionCoords = new Point2i(regionX, regionZ);
                    
                    // Verify all chunks belong to the same region
                    foreach (var chunk in chunks)
                    {
                        int chunkRegionX = (int)Math.Floor(chunk.Key.X / 32.0);
                        int chunkRegionZ = (int)Math.Floor(chunk.Key.Z / 32.0);
                        
                        if (chunkRegionX != regionX || chunkRegionZ != regionZ)
                        {
                            throw new InvalidOperationException($"Chunk ({chunk.Key.X}, {chunk.Key.Z}) belongs to region ({chunkRegionX}, {chunkRegionZ}), not ({regionX}, {regionZ})");
                        }
                    }
                    
                    progress?.Report($"Creating region file：r.{regionCoords.X}.{regionCoords.Z}.mca...");
                    
                    // Create MCA writer
                    using var mcaWriter = new McaRegionWriter(outputPath, regionCoords);
                    
                    // Add all chunks to the writer
                    int chunkCount = 0;
                    foreach (var chunk in chunks)
                    {
                        chunkCount++;
                        if (chunkCount % 10 == 0 || chunkCount == chunks.Count)
                        {
                            progress?.Report($"Adding chunk：{chunkCount}/{chunks.Count}");
                        }
                        
                        // Fix any empty lists with Unknown type before writing
                        FixEmptyLists(chunk.Value);
                        
                        mcaWriter.AddChunk(
                            chunk.Key, 
                            chunk.Value, 
                            CompressionType.Zlib, // Default to Zlib compression
                            (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        );
                    }
                    
                    // Write the MCA file
                    progress?.Report("Writing into mca file...");
                    mcaWriter.WriteAsync();
                    
                    // Also create reconstruction info for debugging
                    progress?.Report("Generating reconstruction file info...");
                    var reconstructionInfo = new NbtCompound("McaReconstruction");
                    reconstructionInfo.Add(new NbtString("OriginalSnbtPath", inputPath));
                    reconstructionInfo.Add(new NbtString("TargetMcaPath", outputPath));
                    reconstructionInfo.Add(new NbtInt("ChunksFound", chunks.Count));
                    reconstructionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                    reconstructionInfo.Add(new NbtString("RegionX", regionCoords.X.ToString()));
                    reconstructionInfo.Add(new NbtString("RegionZ", regionCoords.Z.ToString()));
                    reconstructionInfo.Add(new NbtString("Status", "Successfully converted to MCA"));
                    
                    var chunksList = new NbtList("Chunks", NbtTagType.Compound);
                    foreach (var chunk in chunks)
                    {
                        var chunkInfo = new NbtCompound();
                        chunkInfo.Add(new NbtInt("X", chunk.Key.X));
                        chunkInfo.Add(new NbtInt("Z", chunk.Key.Z));
                        chunkInfo.Add(new NbtString("Status", "Written to MCA"));
                        
                        // Add some basic chunk validation info
                        var xPos = chunk.Value.Get<NbtInt>("xPos")?.Value ?? -999;
                        var zPos = chunk.Value.Get<NbtInt>("zPos")?.Value ?? -999;
                        chunkInfo.Add(new NbtString("ValidationStatus", 
                            (xPos == chunk.Key.X && zPos == chunk.Key.Z) ? "Coordinates match" : "Coordinate mismatch"));
                        
                        chunksList.Add(chunkInfo);
                    }
                    reconstructionInfo.Add(chunksList);
                    
                    // Save reconstruction info
                    var infoSnbt = reconstructionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                    var infoPath = Path.ChangeExtension(outputPath, ".reconstruction.snbt");
                    using (var fs = new FileStream(infoPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        using (var writer = new StreamWriter(fs, Encoding.UTF8))
                        {
                            writer.Write(infoSnbt);
                            writer.Flush();
                        }
                    }
                    
                    progress?.Report($"Conversion complete! mca file has been successfully created: {Path.GetFileName(outputPath)}");
                }
                catch (Exception ex)
                {
                    progress?.Report($"Conversion failed: {ex.Message}");
                    
                    // Create error log if conversion fails
                    var errorInfo = $"// Error converting SNBT to MCA: {ex.Message}\n// Original file: {inputPath}\n// Target file: {outputPath}\n// Time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";
                    using (var fs = new FileStream(outputPath + ".error.txt", FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        using (var writer = new StreamWriter(fs, Encoding.UTF8))
                        {
                            writer.Write(errorInfo);
                            writer.Flush();
                        }
                    }

                    // Create an empty MCA file
                    var emptyMcaData = new byte[8192];
                    File.WriteAllBytes(outputPath, emptyMcaData);
                    
                    throw;
                }
            });
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
                // Use StreamWriter to improve memory efficiency
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs, Encoding.UTF8))
                {
                    writer.Write(content);
                    writer.Flush();
                    fs.Flush();
                }
                
                // Ensure to write is complete
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
        
        // Optimized: Process chunk section using spans to avoid string allocations
        private void ProcessChunkSectionSpan(ReadOnlySpan<char> section, Dictionary<Point2i, NbtCompound> chunks)
        {
            // Find the header line with chunk coordinates
            int lineStart = 0;
            string? headerLine = null;
            
            while (lineStart < section.Length)
            {
                int lineEnd = section.Slice(lineStart).IndexOf('\n');
                if (lineEnd < 0) lineEnd = section.Length - lineStart;
                
                ReadOnlySpan<char> line = section.Slice(lineStart, lineEnd);
                
                // Check if this line contains chunk header - support both old and new format
                if (line.IndexOf("// SNBT for chunk".AsSpan()) >= 0 || line.IndexOf("// Chunk(".AsSpan()) >= 0)
                {
                    headerLine = line.ToString();
                    break;
                }
                
                lineStart += lineEnd + 1;
            }
            
            if (headerLine != null)
            {
                int x = 0;
                int z = 0;
                bool coordinatesFound = false;
                
                // Extract chunk coordinates using regex for different formats
                // Try old format: "// SNBT for chunk X, Z:"
                var oldFormatMatch = System.Text.RegularExpressions.Regex.Match(headerLine, @"chunk (-?\d+), (-?\d+):");
                // Try new format: "// Chunk(X,Z)"
                var newFormatMatch = System.Text.RegularExpressions.Regex.Match(headerLine, @"Chunk\((-?\d+),(-?\d+)\)");
                
                if (oldFormatMatch.Success)
                {
                    x = int.Parse(oldFormatMatch.Groups[1].Value);
                    z = int.Parse(oldFormatMatch.Groups[2].Value);
                    coordinatesFound = true;
                }
                else if (newFormatMatch.Success)
                {
                    x = int.Parse(newFormatMatch.Groups[1].Value);
                    z = int.Parse(newFormatMatch.Groups[2].Value);
                    coordinatesFound = true;
                }
                
                if (!coordinatesFound)
                {
                    // Can't parse coordinates, skip this section
                    return;
                }
                
                var chunkCoord = new Point2i(x, z);
                
                // Build SNBT content excluding comment lines
                var snbtBuilder = new StringBuilder();
                lineStart = 0;
                
                while (lineStart < section.Length)
                {
                    int lineEnd = section.Slice(lineStart).IndexOf('\n');
                    if (lineEnd < 0) lineEnd = section.Length - lineStart;
                    
                    ReadOnlySpan<char> line = section.Slice(lineStart, lineEnd);
                    
                    // Skip comment lines and empty lines
                    ReadOnlySpan<char> trimmedLine = line.Trim();
                    if (!trimmedLine.IsEmpty && !trimmedLine.StartsWith("//".AsSpan()))
                    {
                        if (snbtBuilder.Length > 0)
                            snbtBuilder.AppendLine();
                        snbtBuilder.Append(line);
                    }
                    
                    lineStart += lineEnd + 1;
                }
                
                string chunkSnbt = snbtBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkSnbt))
                {
                    try
                    {
                        // Ensure the SNBT content is valid JSON format
                        if (chunkSnbt.StartsWith("{") && chunkSnbt.EndsWith("}"))
                        {
                            // Use failable to parse SNBT, so it won't throw an exception on trailing data
                            var parseResult = SnbtParser.TryParse(chunkSnbt, false);
                            if (parseResult.IsSuccess && parseResult.Result is NbtCompound chunkNbt)
                            {
                                // Make sure the root tag has a name
                                if (string.IsNullOrEmpty(chunkNbt.Name))
                                {
                                    chunkNbt.Name = "";
                                }
                                chunks[chunkCoord] = chunkNbt;
                            }
                            else
                            {
                                Console.WriteLine($"Warning: Parsed SNBT for chunk {x},{z} did not result in a valid NBT compound: {parseResult.Exception?.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Invalid SNBT format for chunk {x},{z} - must start with '{{' and end with '}}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log parsing error but continue with other chunks
                        Console.WriteLine($"Failed to parse chunk {x},{z}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Empty SNBT content for chunk {x},{z}");
                }
            }
        }

        /// <summary>
        /// Process multiple chunks without separators - used for new SNBT format
        /// where chunks are simply placed one after another with "// Chunk(X,Z)" headers
        /// </summary>
        private void ProcessMultipleChunksWithoutSeparator(string snbtContent, Dictionary<Point2i, NbtCompound> chunks)
        {
            var lines = snbtContent.Split('\n');
            var currentChunkLines = new List<string>();
            Point2i? currentChunkCoord = null;
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                
                // Check if this is a chunk header line
                if (line.Contains("// SNBT for chunk") || line.Contains("// Chunk("))
                {
                    // If we have a previous chunk, process it
                    if (currentChunkCoord.HasValue && currentChunkLines.Count > 0)
                    {
                        ProcessChunkFromLines(currentChunkLines, currentChunkCoord.Value, chunks);
                    }
                    
                    // Start a new chunk
                    currentChunkCoord = ExtractChunkCoordinatesFromHeader(line);
                    currentChunkLines.Clear();
                }
                else
                {
                    // Add this line to current chunk (if we have one)
                    if (currentChunkCoord.HasValue)
                    {
                        currentChunkLines.Add(line);
                    }
                }
            }
            
            // Process the last chunk
            if (currentChunkCoord.HasValue && currentChunkLines.Count > 0)
            {
                ProcessChunkFromLines(currentChunkLines, currentChunkCoord.Value, chunks);
            }
        }
        
        /// <summary>
        /// Extract chunk coordinates from header line
        /// Supports both "// SNBT for chunk X, Z:" and "// Chunk(X,Z)" formats
        /// </summary>
        private Point2i? ExtractChunkCoordinatesFromHeader(string headerLine)
        {
            // Try old format: "// SNBT for chunk X, Z:"
            var oldFormatMatch = System.Text.RegularExpressions.Regex.Match(headerLine, @"chunk (-?\d+), (-?\d+):");
            if (oldFormatMatch.Success)
            {
                int x = int.Parse(oldFormatMatch.Groups[1].Value);
                int z = int.Parse(oldFormatMatch.Groups[2].Value);
                return new Point2i(x, z);
            }
            
            // Try new format: "// Chunk(X,Z)"
            var newFormatMatch = System.Text.RegularExpressions.Regex.Match(headerLine, @"Chunk\((-?\d+),(-?\d+)\)");
            if (newFormatMatch.Success)
            {
                int x = int.Parse(newFormatMatch.Groups[1].Value);
                int z = int.Parse(newFormatMatch.Groups[2].Value);
                return new Point2i(x, z);
            }
            
            return null;
        }
        
        /// <summary>
        /// Process a chunk from a list of lines
        /// </summary>
        private void ProcessChunkFromLines(List<string> lines, Point2i chunkCoord, Dictionary<Point2i, NbtCompound> chunks)
        {
            try
            {
                // Build SNBT content excluding comment lines and empty lines
                var snbtBuilder = new StringBuilder();
                
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    // Skip comment lines and empty lines
                    if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("//"))
                    {
                        if (snbtBuilder.Length > 0)
                            snbtBuilder.AppendLine();
                        snbtBuilder.Append(line);
                    }
                }
                
                string chunkSnbt = snbtBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkSnbt))
                {
                    // Ensure the SNBT content is valid JSON format
                    if (chunkSnbt.StartsWith("{") && chunkSnbt.EndsWith("}"))
                    {
                        // Use failable to parse SNBT, so it won't throw an exception on trailing data
                        var parseResult = SnbtParser.TryParse(chunkSnbt, false);
                        if (parseResult.IsSuccess && parseResult.Result is NbtCompound chunkNbt)
                        {
                            // Make sure the root tag has a name
                            if (string.IsNullOrEmpty(chunkNbt.Name))
                            {
                                chunkNbt.Name = "";
                            }
                            chunks[chunkCoord] = chunkNbt;
                            Console.WriteLine($"Successfully parsed chunk {chunkCoord.X},{chunkCoord.Z}");
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Parsed SNBT for chunk {chunkCoord.X},{chunkCoord.Z} did not result in a valid NBT compound: {parseResult.Exception?.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Invalid SNBT format for chunk {chunkCoord.X},{chunkCoord.Z} - must start with '{{' and end with '}}'");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                }
            }
            catch (Exception ex)
            {
                // Log parsing error but continue with other chunks
                Console.WriteLine($"Failed to parse chunk {chunkCoord.X},{chunkCoord.Z}: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract region coordinates from MCA file path
        /// Example: "r.2.-1.mca" -> Point2i(2, -1)
        /// </summary>
        private static Point2i ExtractRegionCoordinatesFromMcaPath(string filePath)
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
            
            // If we can't extract from filename, try to infer from chunks
            // For now, default to region 0,0
            return new Point2i(0, 0);
        }
    }
}
