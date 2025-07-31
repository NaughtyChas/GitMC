using System.Text;
using System.Text.RegularExpressions;
using fNbt;
using GitMC.Utils;
using GitMC.Utils.Mca;
using GitMC.Utils.Nbt;

namespace GitMC.Services;

public class NbtService : INbtService
{
    private static readonly object FileLock = new();

    public async Task<string> ConvertNbtToSnbtAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) throw new FileNotFoundException($"File does not exist: {filePath}");

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
            if (!File.Exists(inputPath)) throw new FileNotFoundException($"File does not exist: {inputPath}");

            // Determine file type and handle accordingly
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();

            if (extension == ".mca" || extension == ".mcc")
                // For region files, we need to handle them specially
                ConvertRegionFileToSnbt(inputPath, outputPath).Wait();
            else if (extension == ".dat" || extension == ".nbt" || extension == ".dat_old")
                // For nbt/dat/dat_old files, standard conversion
                ConvertNbtFileToSnbt(inputPath, outputPath);
            else
                throw new NotSupportedException($"Unsupported file extension: {extension}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error converting {inputPath} to SNBT: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     To prevent UI blocking issue we use this async method of file conversion:
    /// </summary>
    public async Task ConvertToSnbtAsync(string inputPath, string outputPath, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Checking file: {Path.GetFileName(inputPath)}...");
            if (!File.Exists(inputPath)) throw new FileNotFoundException($"File does not exist: {inputPath}");

            // Check file type and their actions
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();

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
            if (!File.Exists(inputPath)) throw new FileNotFoundException($"SNBT file does not exist: {inputPath}");

            // Determine target file type from output path
            string extension = Path.GetExtension(outputPath).ToLowerInvariant();

            if (extension == ".mca" || extension == ".mcc")
                // For region files, we need to handle them specially
                ConvertSnbtToRegionFile(inputPath, outputPath);
            else if (extension == ".dat" || extension == ".nbt" || extension == ".dat_old")
                // For NBT/DAT/DAT_OLD files, standard conversion
                ConvertSnbtToNbtFile(inputPath, outputPath);
            else
                throw new NotSupportedException($"Unsupported target file extension: {extension}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error converting {inputPath} from SNBT: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Async snbt conversion method with progress returning
    /// </summary>
    public async Task ConvertFromSnbtAsync(string inputPath, string outputPath, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Reading snbt files: {Path.GetFileName(inputPath)}...");
            if (!File.Exists(inputPath)) throw new FileNotFoundException($"snbt file does not exist: {inputPath}");

            // Check target file type
            string extension = Path.GetExtension(outputPath).ToLowerInvariant();

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
            throw new InvalidOperationException($"Error when converting {inputPath} to target file : {ex.Message}",
                ex);
        }
    }

    public void ConvertSnbtToRegionFile(string inputPath, string outputPath)
    {
        try
        {
            string snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);

            // Parse SNBT with multiple chunks
            var chunks = new Dictionary<Point2I, NbtCompound>();

            // Check if single or multiple chunks
            // First, check for old format with separators
            if (snbtContent.Contains("// SNBT for chunk") &&
                snbtContent.Contains("// =========================================="))
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
                    if (section.IndexOf("// SNBT for chunk".AsSpan()) >= 0) ProcessChunkSectionSpan(section, chunks);

                    // Move to next section
                    currentPos = sepIndex >= 0 ? sectionEnd + separator.Length : content.Length;
                }
            }
            else if (snbtContent.Contains("// SNBT for chunk") || snbtContent.Contains("// Chunk("))
            {
                // Check if this is multiple chunks without separators (new format)
                // Count the number of chunk headers to determine if it's multi-chunk
                int chunkCount = 0;

                // Optimized: Use span-based line enumeration instead of Split
                ReadOnlySpan<char> contentSpan = snbtContent.AsSpan();
                ReadOnlySpan<char> remaining = contentSpan;

                while (!remaining.IsEmpty)
                {
                    int lineEnd = remaining.IndexOf('\n');
                    ReadOnlySpan<char> line;

                    if (lineEnd >= 0)
                    {
                        line = remaining[..lineEnd];
                        remaining = remaining[(lineEnd + 1)..];
                    }
                    else
                    {
                        line = remaining;
                        remaining = ReadOnlySpan<char>.Empty;
                    }

                    // Trim carriage return if present
                    if (!line.IsEmpty && line[^1] == '\r')
                        line = line[..^1];

                    if (line.Contains("// SNBT for chunk".AsSpan(), StringComparison.Ordinal) ||
                        line.Contains("// Chunk(".AsSpan(), StringComparison.Ordinal))
                        chunkCount++;
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
                    if (string.IsNullOrEmpty(rootNbt.Name)) rootNbt.Name = "";

                    // Extract cords from NBT tag
                    int xPos = rootNbt.Get<NbtInt>("xPos")?.Value ?? 0;
                    int zPos = rootNbt.Get<NbtInt>("zPos")?.Value ?? 0;
                    chunks[new Point2I(xPos, zPos)] = rootNbt;
                }
            }

            if (chunks.Count == 0) throw new InvalidOperationException("No valid chunk data found in SNBT file");

            // Calculate correct region coordinates from chunk coordinates based on first chunk
            Point2I firstChunk = chunks.First().Key;
            int regionX = (int)Math.Floor(firstChunk.X / 32.0);
            int regionZ = (int)Math.Floor(firstChunk.Z / 32.0);
            var regionCoords = new Point2I(regionX, regionZ);

            // Verify all chunks belong to the same region
            foreach (KeyValuePair<Point2I, NbtCompound> chunk in chunks)
            {
                int chunkRegionX = (int)Math.Floor(chunk.Key.X / 32.0);
                int chunkRegionZ = (int)Math.Floor(chunk.Key.Z / 32.0);

                if (chunkRegionX != regionX || chunkRegionZ != regionZ)
                    throw new InvalidOperationException(
                        $"Chunk ({chunk.Key.X}, {chunk.Key.Z}) belongs to region ({chunkRegionX}, {chunkRegionZ}), not ({regionX}, {regionZ})");
            }

            // Create MCA writer with correct region coordinates
            using var mcaWriter = new McaRegionWriter(outputPath, regionCoords);

            // Add all chunks to the writer
            foreach (KeyValuePair<Point2I, NbtCompound> chunk in chunks)
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
            reconstructionInfo.Add(
                new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
            reconstructionInfo.Add(new NbtString("RegionX", regionCoords.X.ToString()));
            reconstructionInfo.Add(new NbtString("RegionZ", regionCoords.Z.ToString()));
            reconstructionInfo.Add(new NbtString("Status", "Successfully converted to MCA"));

            var chunksList = new NbtList("Chunks", NbtTagType.Compound);
            foreach (KeyValuePair<Point2I, NbtCompound> chunk in chunks)
            {
                var chunkInfo = new NbtCompound();
                chunkInfo.Add(new NbtInt("X", chunk.Key.X));
                chunkInfo.Add(new NbtInt("Z", chunk.Key.Z));
                chunkInfo.Add(new NbtString("Status", "Written to MCA"));

                // Add some basic chunk validation info
                int xPos = chunk.Value.Get<NbtInt>("xPos")?.Value ?? -999;
                int zPos = chunk.Value.Get<NbtInt>("zPos")?.Value ?? -999;
                chunkInfo.Add(new NbtString("ValidationStatus",
                    xPos == chunk.Key.X && zPos == chunk.Key.Z ? "Coordinates match" : "Coordinate mismatch"));

                chunksList.Add(chunkInfo);
            }

            reconstructionInfo.Add(chunksList);

            // Save reconstruction info
            string infoSnbt = reconstructionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
            string infoPath = Path.ChangeExtension(outputPath, ".reconstruction.snbt");
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
            string errorInfo =
                $"// Error converting SNBT to MCA: {ex.Message}\n// Original file: {inputPath}\n// Target file: {outputPath}\n// Time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";
            using (var fs = new FileStream(outputPath + ".error.txt", FileMode.Create, FileAccess.Write,
                       FileShare.None))
            {
                using (var writer = new StreamWriter(fs, Encoding.UTF8))
                {
                    writer.Write(errorInfo);
                    writer.Flush();
                }
            }

            // Create an empty MCA file
            byte[] emptyMcaData = new byte[8192];
            File.WriteAllBytes(outputPath, emptyMcaData);

            // Gonna re-throw exception to let the exception be successfully intercepted by the caller
            throw;
        }
    }

    /// <summary>
    ///     Async snbt -> region
    /// </summary>
    public async Task ConvertSnbtToRegionFileAsync(string inputPath, string outputPath,
        IProgress<string>? progress = null)
    {
        await Task.Run(() =>
        {
            try
            {
                progress?.Report($"Reading snbt file {Path.GetFileName(inputPath)}...");
                string snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);

                progress?.Report("Parsing snbt content...");
                // Parse SNBT with multiple chunks
                var chunks = new Dictionary<Point2I, NbtCompound>();

                // Check if single or multiple chunks
                if (snbtContent.Contains("// SNBT for chunk") &&
                    snbtContent.Contains("// =========================================="))
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
                            ProcessChunkSectionSpan(section, chunks);

                        // Move to next section
                        currentPos = sepIndex >= 0 ? sectionEnd + separator.Length : content.Length;
                    }
                }
                else if (snbtContent.Contains("// SNBT for chunk") || snbtContent.Contains("// Chunk("))
                {
                    // Check if this is multiple chunks without separators (new format)
                    // Count the number of chunk headers to determine if it's multi-chunk
                    int totalChunks = 0;

                    // Optimized: Use span-based line enumeration instead of Split
                    ReadOnlySpan<char> contentSpan = snbtContent.AsSpan();
                    ReadOnlySpan<char> remaining = contentSpan;

                    while (!remaining.IsEmpty)
                    {
                        int lineEnd = remaining.IndexOf('\n');
                        ReadOnlySpan<char> line;

                        if (lineEnd >= 0)
                        {
                            line = remaining[..lineEnd];
                            remaining = remaining[(lineEnd + 1)..];
                        }
                        else
                        {
                            line = remaining;
                            remaining = ReadOnlySpan<char>.Empty;
                        }

                        // Trim carriage return if present
                        if (!line.IsEmpty && line[^1] == '\r')
                            line = line[..^1];

                        if (line.Contains("// SNBT for chunk".AsSpan(), StringComparison.Ordinal) ||
                            line.Contains("// Chunk(".AsSpan(), StringComparison.Ordinal))
                            totalChunks++;
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
                        if (string.IsNullOrEmpty(rootNbt.Name)) rootNbt.Name = "";

                        // Extract cords from NBT tag
                        int xPos = rootNbt.Get<NbtInt>("xPos")?.Value ?? 0;
                        int zPos = rootNbt.Get<NbtInt>("zPos")?.Value ?? 0;
                        chunks[new Point2I(xPos, zPos)] = rootNbt;
                    }
                }

                if (chunks.Count == 0) throw new InvalidOperationException("No valid chunk data found in SNBT file");

                progress?.Report($"Parsing success! Found {chunks.Count} chunks");

                // Calculate correct region coordinates from chunk coordinates based on first chunk
                Point2I firstChunk = chunks.First().Key;
                int regionX = (int)Math.Floor(firstChunk.X / 32.0);
                int regionZ = (int)Math.Floor(firstChunk.Z / 32.0);
                var regionCoords = new Point2I(regionX, regionZ);

                // Verify all chunks belong to the same region
                foreach (KeyValuePair<Point2I, NbtCompound> chunk in chunks)
                {
                    int chunkRegionX = (int)Math.Floor(chunk.Key.X / 32.0);
                    int chunkRegionZ = (int)Math.Floor(chunk.Key.Z / 32.0);

                    if (chunkRegionX != regionX || chunkRegionZ != regionZ)
                        throw new InvalidOperationException(
                            $"Chunk ({chunk.Key.X}, {chunk.Key.Z}) belongs to region ({chunkRegionX}, {chunkRegionZ}), not ({regionX}, {regionZ})");
                }

                progress?.Report($"Creating region file：r.{regionCoords.X}.{regionCoords.Z}.mca...");

                // Create MCA writer
                using var mcaWriter = new McaRegionWriter(outputPath, regionCoords);

                // Add all chunks to the writer
                int chunkCount = 0;
                foreach (KeyValuePair<Point2I, NbtCompound> chunk in chunks)
                {
                    chunkCount++;
                    if (chunkCount % 10 == 0 || chunkCount == chunks.Count)
                        progress?.Report($"Adding chunk：{chunkCount}/{chunks.Count}");

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
                reconstructionInfo.Add(new NbtString("ConversionTime",
                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
                reconstructionInfo.Add(new NbtString("RegionX", regionCoords.X.ToString()));
                reconstructionInfo.Add(new NbtString("RegionZ", regionCoords.Z.ToString()));
                reconstructionInfo.Add(new NbtString("Status", "Successfully converted to MCA"));

                var chunksList = new NbtList("Chunks", NbtTagType.Compound);
                foreach (KeyValuePair<Point2I, NbtCompound> chunk in chunks)
                {
                    var chunkInfo = new NbtCompound();
                    chunkInfo.Add(new NbtInt("X", chunk.Key.X));
                    chunkInfo.Add(new NbtInt("Z", chunk.Key.Z));
                    chunkInfo.Add(new NbtString("Status", "Written to MCA"));

                    // Add some basic chunk validation info
                    int xPos = chunk.Value.Get<NbtInt>("xPos")?.Value ?? -999;
                    int zPos = chunk.Value.Get<NbtInt>("zPos")?.Value ?? -999;
                    chunkInfo.Add(new NbtString("ValidationStatus",
                        xPos == chunk.Key.X && zPos == chunk.Key.Z
                            ? "Coordinates match"
                            : "Coordinate mismatch"));

                    chunksList.Add(chunkInfo);
                }

                reconstructionInfo.Add(chunksList);

                // Save reconstruction info
                string infoSnbt = reconstructionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
                string infoPath = Path.ChangeExtension(outputPath, ".reconstruction.snbt");
                using (var fs = new FileStream(infoPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(infoSnbt);
                        writer.Flush();
                    }
                }

                progress?.Report(
                    $"Conversion complete! mca file has been successfully created: {Path.GetFileName(outputPath)}");
            }
            catch (Exception ex)
            {
                progress?.Report($"Conversion failed: {ex.Message}");

                // Create error log if conversion fails
                string errorInfo =
                    $"// Error converting SNBT to MCA: {ex.Message}\n// Original file: {inputPath}\n// Target file: {outputPath}\n// Time: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}";
                using (var fs = new FileStream(outputPath + ".error.txt", FileMode.Create, FileAccess.Write,
                           FileShare.None))
                {
                    using (var writer = new StreamWriter(fs, Encoding.UTF8))
                    {
                        writer.Write(errorInfo);
                        writer.Flush();
                    }
                }

                // Create an empty MCA file
                byte[] emptyMcaData = new byte[8192];
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
            NbtTag rootTag = SnbtParser.Parse(snbtContent, false);

            // Fix empty lists before creating NBT file
            FixEmptyLists(rootTag);

            // Create NBT file and save
            var nbtFile = new NbtFile();
            if (rootTag is NbtCompound compound)
            {
                // Ensure the root compound has a name (fNbt requirement)
                if (string.IsNullOrEmpty(compound.Name)) compound.Name = ""; // fNbt allows empty string as name

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
                if (!File.Exists(filePath)) return false;

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
                if (!File.Exists(filePath)) return "File does not exist";

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

                NbtCompound rootTag = nbtFile.RootTag;

                info.AppendLine($"NBT format: {nbtFile.FileCompression}");
                info.AppendLine($"Root tag type: {rootTag.TagType}");
                info.AppendLine($"Root tag name: {rootTag.Name ?? "(Unnamed)"}");

                if (rootTag is NbtCompound compound)
                {
                    info.AppendLine($"Child tag count: {compound.Count}");

                    if (compound.Count > 0)
                    {
                        info.AppendLine("\nChild tags:");
                        foreach (NbtTag tag in compound) info.AppendLine($"  - {tag.Name}: {tag.TagType}");
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
            Failable<NbtTag> result = SnbtParser.TryParse(snbtContent, false);
            return result.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    // Anvil Region File Support

    public async Task<AnvilRegionInfo> GetRegionInfoAsync(string mcaFilePath)
    {
        try
        {
            using var regionFile = new McaRegionFile(mcaFilePath);
            await regionFile.LoadAsync();

            var fileInfo = new FileInfo(mcaFilePath);
            List<Point2I> existingChunks = regionFile.GetExistingChunks();

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
            List<Point2I> existingChunks = regionFile.GetExistingChunks();

            if (regionFile.Header == null) return chunkInfos;

            for (int i = 0; i < 1024; i++)
            {
                ChunkInfo chunkInfo = regionFile.Header.ChunkInfos[i];
                if (chunkInfo.SectorOffset == 0) continue; // Skip chunks that does not exist

                var localCoords = Point2I.FromChunkIndex(i);
                var globalCoords = new Point2I(
                    regionFile.RegionCoordinates.X * 32 + localCoords.X,
                    regionFile.RegionCoordinates.Z * 32 + localCoords.Z
                );

                // Attempt to read detailed info from chunks
                AnvilCompressionType compressionType = AnvilCompressionType.Zlib;
                // For Java saves default is Zlib compression
                // I think wiki mentioned that server.config can determine the compression level
                // Maybe we have to add support for that in late stage of development.

                bool isOversized = false;
                long dataSize = 0L;
                bool isValid = true;

                try
                {
                    ChunkData? chunkData = await regionFile.GetChunkAsync(globalCoords);
                    if (chunkData != null)
                    {
                        compressionType = ConvertCompressionType(chunkData.CompressionType);
                        isOversized = chunkData.IsExternal;
                        dataSize = chunkData.DataLength;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"Error occurred while reading chunk data: {ex.Message}");
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

            var chunkCoords = new Point2I(chunkX, chunkZ);
            ChunkData? chunkData = await regionFile.GetChunkAsync(chunkCoords);

            if (chunkData?.NbtData == null)
                throw new InvalidOperationException($"Chunk ({chunkX}, {chunkZ}) not found or is empty");

            // Convert to SNBT
            return chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to extract chunk ({chunkX}, {chunkZ}) from {mcaFilePath}: {ex.Message}", ex);
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

                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (extension != ".mca" && extension != ".mcc")
                    return false;

                if (extension == ".mcc")
                    // .mcc files are oversized chunk files, validate as NBT data
                    return IsValidNbtFileAsync(filePath).Result;

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

    private void ConvertNbtFileToSnbt(string inputPath, string outputPath)
    {
        lock (FileLock)
        {
            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(inputPath);

                if (nbtFile.RootTag == null) throw new InvalidDataException($"NBT file has no root tag: {inputPath}");

                // Convert to SNBT format using SnbtCmd's implementation
                string snbtContent = nbtFile.RootTag.ToSnbt(SnbtOptions.DefaultExpanded);

                // Validate SNBT content before writing
                if (string.IsNullOrEmpty(snbtContent))
                    throw new InvalidDataException($"Generated SNBT content is empty for file: {inputPath}");

                // Prevent file corruption
                WriteFileAtomically(outputPath, snbtContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert NBT to SNBT for {inputPath}: {ex.Message}",
                    ex);
            }
        }
    }

    private void ConvertSnbtToNbtFile(string inputPath, string outputPath)
    {
        lock (FileLock)
        {
            string snbtContent = File.ReadAllText(inputPath, Encoding.UTF8);

            // Parse SNBT using SnbtCmd's parser
            NbtTag rootTag = SnbtParser.Parse(snbtContent, false);

            // Fix empty lists before creating NBT file
            FixEmptyLists(rootTag);

            // Create NBT file and save
            var nbtFile = new NbtFile();
            if (rootTag is NbtCompound compound)
            {
                // Ensure the root compound has a name (fNbt requirement)
                if (string.IsNullOrEmpty(compound.Name)) compound.Name = "";

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

    private async Task ConvertRegionFileToSnbt(string inputPath, string outputPath)
    {
        try
        {
            // Use McaRegionFile to parse MCA files
            using var mcaFile = new McaRegionFile(inputPath);
            await mcaFile.LoadAsync();

            // Get existing chunks
            List<Point2I> existingChunks = mcaFile.GetExistingChunks();

            if (existingChunks.Count == 0)
            {
                // If there are no chunks, create empty file info
                var emptyRegionInfo = new NbtCompound("EmptyRegion");
                emptyRegionInfo.Add(new NbtString("OriginalPath", inputPath));
                emptyRegionInfo.Add(new NbtString("RegionCoordinates", mcaFile.RegionCoordinates.ToString()));
                emptyRegionInfo.Add(new NbtString("Note", "This region file contains no chunks"));

                string emptySnbtContent = emptyRegionInfo.ToSnbt(SnbtOptions.DefaultExpanded);

                // Use Stream with larger buffer for better performance
                await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 65536))
                await using (var writer = new StreamWriter(fs, Encoding.UTF8, 65536))
                {
                    await writer.WriteAsync(emptySnbtContent);
                    await writer.FlushAsync();
                }

                return;
            }

            // Use more efficient memory allocation for large files
            int initialCapacity = existingChunks.Count > 100 ? existingChunks.Count * 2048 : 8192;
            var snbtContent = new StringBuilder(initialCapacity);

            // If there is only one chunk, write in NBT directly
            if (existingChunks.Count == 1)
            {
                Point2I chunkCoord = existingChunks[0];
                ChunkData? chunkData = await mcaFile.GetChunkAsync(chunkCoord);

                // Add region information header even for single chunk
                snbtContent.AppendLine($"// Region file: {Path.GetFileName(inputPath)}");
                snbtContent.AppendLine(
                    $"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                snbtContent.AppendLine("// Total chunks: 1");
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
                snbtContent.AppendLine(
                    $"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                snbtContent.AppendLine($"// Total chunks: {existingChunks.Count}");
                snbtContent.AppendLine();

                int processedCount = 0;

                foreach (Point2I chunkCoord in existingChunks)
                {
                    try
                    {
                        ChunkData? chunkData = await mcaFile.GetChunkAsync(chunkCoord);
                        if (chunkData?.NbtData != null)
                        {
                            // Simplified chunk header
                            snbtContent.AppendLine($"// Chunk({chunkCoord.X},{chunkCoord.Z})");
                            snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                            snbtContent.AppendLine(); // Just add a blank line between chunks
                        }

                        // Yield control to prevent UI freezing, but less frequently for better performance
                        if (processedCount % 50 == 0) await Task.Yield();
                    }
                    catch (Exception ex)
                    {
                        snbtContent.AppendLine($"// ERROR in chunk {chunkCoord.X}, {chunkCoord.Z}: {ex.Message}");
                        snbtContent.AppendLine();
                    }

                    processedCount++;
                }
            }

            // Use larger buffer size for better I/O performance
            await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             131072))
            await using (var writer = new StreamWriter(fs, Encoding.UTF8, 131072))
            {
                await writer.WriteAsync(snbtContent.ToString());
                await writer.FlushAsync();
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
            errorInfo.Add(new NbtString("Note",
                "MCA parsing failed - this indicates a compression or parsing issue"));

            string errorSnbtContent = errorInfo.ToSnbt(SnbtOptions.DefaultExpanded);
            await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                await writer.WriteAsync(errorSnbtContent);
                await writer.FlushAsync();
            }
        }
    }

    /// <summary>
    ///     Async translate region file to snbt
    /// </summary>
    private async Task ConvertRegionFileToSnbtAsync(string inputPath, string outputPath,
        IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Loading region file: {Path.GetFileName(inputPath)}...");

            // Parse mca file using McaRegionFile
            using var mcaFile = new McaRegionFile(inputPath);
            await mcaFile.LoadAsync();

            // Get current chunk
            List<Point2I> existingChunks = mcaFile.GetExistingChunks();

            if (existingChunks.Count == 0)
            {
                progress?.Report("No chunk is found, creating empty region info...");
                // ... and do stuff just like the text said ↑
                var emptyRegionInfo = new NbtCompound("EmptyRegion");
                emptyRegionInfo.Add(new NbtString("OriginalPath", inputPath));
                emptyRegionInfo.Add(new NbtString("RegionCoordinates", mcaFile.RegionCoordinates.ToString()));
                emptyRegionInfo.Add(new NbtString("Note", "This region file contains no chunks"));

                string emptySnbtContent = emptyRegionInfo.ToSnbt(SnbtOptions.DefaultExpanded);

                // Write file using stream
                await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 65536))
                await using (var writer = new StreamWriter(fs, Encoding.UTF8, 65536))
                {
                    await writer.WriteAsync(emptySnbtContent);
                    await writer.FlushAsync();
                }

                progress?.Report("Created empty region info");
                return;
            }

            progress?.Report($"Found {existingChunks.Count} chunks，converting...");

            // Use more efficient memory allocation for large files
            int initialCapacity = existingChunks.Count > 100 ? existingChunks.Count * 2048 : 8192;
            var snbtContent = new StringBuilder(initialCapacity);

            // Directly write to nbt if there is only one chunk
            if (existingChunks.Count == 1)
            {
                Point2I chunkCoord = existingChunks[0];
                progress?.Report($"Converting single chunk ({chunkCoord.X}, {chunkCoord.Z})...");
                ChunkData? chunkData = await mcaFile.GetChunkAsync(chunkCoord);

                // Add region information header even for single chunk
                snbtContent.AppendLine($"// Region file: {Path.GetFileName(inputPath)}");
                snbtContent.AppendLine(
                    $"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                snbtContent.AppendLine("// Total chunks: 1");
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
                snbtContent.AppendLine(
                    $"// Region coordinates: {mcaFile.RegionCoordinates.X}, {mcaFile.RegionCoordinates.Z}");
                snbtContent.AppendLine($"// Total chunks: {existingChunks.Count}");
                snbtContent.AppendLine();

                int processedCount = 0;

                foreach (Point2I chunkCoord in existingChunks)
                {
                    try
                    {
                        // Report progress much less frequently for better performance
                        if (processedCount % 50 == 0 || processedCount == existingChunks.Count - 1)
                            progress?.Report($"Converting chunk {processedCount + 1}/{existingChunks.Count}...");

                        ChunkData? chunkData = await mcaFile.GetChunkAsync(chunkCoord);
                        if (chunkData?.NbtData != null)
                        {
                            // Simplified chunk header
                            snbtContent.AppendLine($"// Chunk({chunkCoord.X},{chunkCoord.Z})");
                            snbtContent.AppendLine(chunkData.NbtData.ToSnbt(SnbtOptions.DefaultExpanded));
                            snbtContent.AppendLine(); // Just add a blank line between chunks
                        }

                        // Yield control to prevent UI freezing, but less frequently
                        if (processedCount % 25 == 0) await Task.Yield();
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
            // Use larger buffer size for better I/O performance
            await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
                             131072))
            await using (var writer = new StreamWriter(fs, Encoding.UTF8, 131072))
            {
                await writer.WriteAsync(snbtContent.ToString());
                await writer.FlushAsync();
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
            errorInfo.Add(new NbtString("Note",
                "MCA parsing failed - this indicates a compression or parsing issue"));

            string errorSnbtContent = errorInfo.ToSnbt(SnbtOptions.DefaultExpanded);
            await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                await writer.WriteAsync(errorSnbtContent);
                await writer.FlushAsync();
            }

            throw;
        }
    }

    private string DetermineCompressionType(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            byte firstByte = (byte)stream.ReadByte();

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

    /// <summary>
    ///     Convert compression enum
    /// </summary>
    private static AnvilCompressionType ConvertCompressionType(CompressionType mcaType)
    {
        CompressionType baseType = mcaType.GetBaseType();
        return baseType switch
        {
            CompressionType.GZip => AnvilCompressionType.GZip,
            CompressionType.Zlib => AnvilCompressionType.Zlib,
            CompressionType.Uncompressed => AnvilCompressionType.Uncompressed,
            CompressionType.Lz4 => AnvilCompressionType.Lz4,
            CompressionType.Custom => AnvilCompressionType.Custom,
            _ => AnvilCompressionType.Zlib
        };
    }

    /// <summary>
    ///     Fixes empty lists with Unknown type
    /// </summary>
    private void FixEmptyLists(NbtTag tag)
    {
        if (tag is NbtCompound compound)
        {
            var childrenToReplace = new List<(string name, NbtTag newTag)>();

            foreach (NbtTag child in compound)
                if (child is NbtList list && list.Count == 0 && list.ListType == NbtTagType.Unknown)
                {
                    // Create a replacement list with a concrete type
                    var fixedList = new NbtList(child.Name, NbtTagType.Compound);
                    if (child.Name != null) childrenToReplace.Add((child.Name, fixedList));
                }
                else
                {
                    // Recursively fix child tags
                    FixEmptyLists(child);
                }

            // Replace empty lists with fixed versions
            foreach ((string name, NbtTag newTag) in childrenToReplace)
            {
                compound.Remove(name);
                compound.Add(newTag);
            }
        }
        else if (tag is NbtList list)
        {
            // Fix empty lists with Unknown type at this level
            if (list.Count == 0 && list.ListType == NbtTagType.Unknown)
                // Cannot modify in-place, this needs to be handled by parent
                return;

            // Recursively fix child tags
            foreach (NbtTag child in list.ToArray()) FixEmptyLists(child);
        }
    }

    /// <summary>
    ///     Atomically writes a text file to prevent corruption from concurrent access
    /// </summary>
    private void WriteFileAtomically(string filePath, string content)
    {
        string tempPath = filePath + ".tmp";
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
            using (FileStream fs = File.OpenWrite(tempPath))
            {
                fs.Flush();
            }

            // Atomic move to final location
            if (File.Exists(filePath)) File.Delete(filePath);

            File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    ///     Atomically saves an NBT file to prevent corruption from concurrent access
    /// </summary>
    private void SaveNbtFileAtomically(NbtFile nbtFile, string filePath)
    {
        string tempPath = filePath + ".tmp";
        try
        {
            nbtFile.SaveToFile(tempPath, NbtCompression.GZip);

            // Atomic move to final location
            if (File.Exists(filePath)) File.Delete(filePath);

            File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // Optimized: Process chunk section using spans to avoid string allocations
    private void ProcessChunkSectionSpan(ReadOnlySpan<char> section, Dictionary<Point2I, NbtCompound> chunks)
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
            Match oldFormatMatch = Regex.Match(headerLine, @"chunk (-?\d+), (-?\d+):");
            // Try new format: "// Chunk(X,Z)"
            Match newFormatMatch = Regex.Match(headerLine, @"Chunk\((-?\d+),(-?\d+)\)");

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
                // Can't parse coordinates, skip this section
                return;

            var chunkCoord = new Point2I(x, z);

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
                try
                {
                    // Ensure the SNBT content is valid JSON format
                    if (chunkSnbt.StartsWith("{") && chunkSnbt.EndsWith("}"))
                    {
                        // Use failable to parse SNBT, so it won't throw an exception on trailing data
                        Failable<NbtTag> parseResult = SnbtParser.TryParse(chunkSnbt, false);
                        if (parseResult.IsSuccess && parseResult.Result is NbtCompound chunkNbt)
                        {
                            // Make sure the root tag has a name
                            if (string.IsNullOrEmpty(chunkNbt.Name)) chunkNbt.Name = "";

                            chunks[chunkCoord] = chunkNbt;
                        }
                        else
                        {
                            Console.WriteLine(
                                $@"Warning: Parsed SNBT for chunk {x},{z} did not result in a valid NBT compound: {parseResult.Exception?.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            $@"Warning: Invalid SNBT format for chunk {x},{z} - must start with '{{' and end with '}}'");
                    }
                }
                catch (Exception ex)
                {
                    // Log parsing error but continue with other chunks
                    Console.WriteLine($@"Failed to parse chunk {x},{z}: {ex.Message}");
                }
            else
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {x},{z}");
        }
    }

    /// <summary>
    ///     Process multiple chunks without separators - used for new SNBT format
    ///     where chunks are simply placed one after another with "// Chunk(X,Z)" headers
    /// </summary>
    private void ProcessMultipleChunksWithoutSeparator(string snbtContent, Dictionary<Point2I, NbtCompound> chunks)
    {
        // Optimized: Use span-based line enumeration instead of Split to avoid massive string allocations
        var currentChunkLines = new StringBuilder();
        Point2I? currentChunkCoord = null;

        ReadOnlySpan<char> contentSpan = snbtContent.AsSpan();
        ReadOnlySpan<char> remaining = contentSpan;

        while (!remaining.IsEmpty)
        {
            int lineEnd = remaining.IndexOf('\n');
            ReadOnlySpan<char> line;

            if (lineEnd >= 0)
            {
                line = remaining[..lineEnd];
                remaining = remaining[(lineEnd + 1)..];
            }
            else
            {
                line = remaining;
                remaining = ReadOnlySpan<char>.Empty;
            }

            // Trim carriage return if present
            if (!line.IsEmpty && line[^1] == '\r')
                line = line[..^1];

            // Check if this is a chunk header line
            if (line.Contains("// SNBT for chunk".AsSpan(), StringComparison.Ordinal) ||
                line.Contains("// Chunk(".AsSpan(), StringComparison.Ordinal))
            {
                // If we have a previous chunk, process it
                if (currentChunkCoord.HasValue && currentChunkLines.Length > 0)
                    ProcessChunkFromStringBuilder(currentChunkLines, currentChunkCoord.Value, chunks);

                // Start a new chunk
                currentChunkCoord = ExtractChunkCoordinatesFromHeaderSpan(line);
                currentChunkLines.Clear();
            }
            else
            {
                // Add this line to current chunk (if we have one)
                if (currentChunkCoord.HasValue)
                {
                    if (currentChunkLines.Length > 0)
                        currentChunkLines.AppendLine();
                    currentChunkLines.Append(line);
                }
            }
        }

        // Process the last chunk
        if (currentChunkCoord.HasValue && currentChunkLines.Length > 0)
            ProcessChunkFromStringBuilder(currentChunkLines, currentChunkCoord.Value, chunks);
    }

    /// <summary>
    ///     Process chunk from StringBuilder with boundary parameters (ultra-optimized version)
    /// </summary>
    private void ProcessChunkFromStringBuilder(StringBuilder allContent, int startIdx, int endIdx,
        Point2I chunkCoord, Dictionary<Point2I, NbtCompound> chunks)
    {
        try
        {
            if (startIdx >= endIdx || startIdx >= allContent.Length)
            {
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                return;
            }

            // Ultra-optimized: Find content boundaries without creating intermediate strings
            int contentStart = startIdx;
            int contentEnd = Math.Min(endIdx, allContent.Length - 1);

            // Find first non-whitespace character within bounds
            while (contentStart <= contentEnd && char.IsWhiteSpace(allContent[contentStart]))
                contentStart++;

            // Find last non-whitespace character within bounds
            while (contentEnd >= contentStart && char.IsWhiteSpace(allContent[contentEnd]))
                contentEnd--;

            if (contentStart > contentEnd)
            {
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                return;
            }

            // Validate JSON structure without string creation
            if (allContent[contentStart] != '{' || allContent[contentEnd] != '}')
            {
                Console.WriteLine(
                    $@"Warning: Invalid SNBT format for chunk {chunkCoord.X},{chunkCoord.Z} - must start with '{{' and end with '}}'");
                return;
            }

            // Create minimal string only for parsing (final allocation)
            string snbtContent = allContent.ToString(contentStart, contentEnd - contentStart + 1);

            Failable<NbtTag> parseResult = SnbtParser.TryParse(snbtContent, false);
            if (parseResult.IsSuccess && parseResult.Result is NbtCompound compound)
            {
                // CRITICAL: Ensure the root tag has a name to prevent "Root tag must be named" error
                if (string.IsNullOrEmpty(compound.Name)) compound.Name = "";

                chunks[chunkCoord] = compound;
                Console.WriteLine($@"Successfully parsed chunk {chunkCoord.X},{chunkCoord.Z}");
            }
            else
            {
                Console.WriteLine(
                    $@"Warning: Parsed SNBT for chunk {chunkCoord.X},{chunkCoord.Z} did not result in a valid NBT compound: {parseResult.Exception?.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"Failed to parse chunk {chunkCoord.X},{chunkCoord.Z}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Process chunk from StringBuilder content (optimized version)
    /// </summary>
    private void ProcessChunkFromStringBuilder(StringBuilder chunkContent, Point2I chunkCoord,
        Dictionary<Point2I, NbtCompound> chunks)
    {
        // Optimized: Use span-based operations to avoid massive string allocations
        if (chunkContent.Length == 0)
        {
            Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
            return;
        }

        try
        {
            // Process content directly from StringBuilder chunks to avoid ToString() allocation
            // First, find the actual SNBT content boundaries (trim leading/trailing whitespace)
            int startIndex = 0;
            int endIndex = chunkContent.Length - 1;

            // Find first non-whitespace character
            while (startIndex < chunkContent.Length && char.IsWhiteSpace(chunkContent[startIndex]))
                startIndex++;

            // Find last non-whitespace character
            while (endIndex >= startIndex && char.IsWhiteSpace(chunkContent[endIndex]))
                endIndex--;

            if (startIndex > endIndex)
            {
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                return;
            }

            // Check if content starts with '{' and ends with '}'
            if (chunkContent[startIndex] != '{' || chunkContent[endIndex] != '}')
            {
                Console.WriteLine(
                    $@"Warning: Invalid SNBT format for chunk {chunkCoord.X},{chunkCoord.Z} - must start with '{{' and end with '}}'");
                return;
            }

            // Only now create the minimal string needed for parsing
            // Create substring from trimmed bounds to avoid full ToString()
            string snbtContent = chunkContent.ToString(startIndex, endIndex - startIndex + 1);

            Failable<NbtTag> parseResult = SnbtParser.TryParse(snbtContent, false);
            if (parseResult.IsSuccess && parseResult.Result is NbtCompound compound)
            {
                // CRITICAL: Ensure the root tag has a name to prevent "Root tag must be named" error
                if (string.IsNullOrEmpty(compound.Name)) compound.Name = "";

                chunks[chunkCoord] = compound;
                Console.WriteLine($@"Successfully parsed chunk {chunkCoord.X},{chunkCoord.Z}");
            }
            else
            {
                Console.WriteLine(
                    $@"Warning: Parsed SNBT for chunk {chunkCoord.X},{chunkCoord.Z} did not result in a valid NBT compound: {parseResult.Exception?.Message}");
            }
        }
        catch (Exception ex)
        {
            // Log parsing error but continue with other chunks
            Console.WriteLine($@"Failed to parse chunk {chunkCoord.X},{chunkCoord.Z}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Extract chunk coordinates from header line using span (optimized version)
    ///     Supports both "// SNBT for chunk X, Z:" and "// Chunk(X,Z)" formats
    /// </summary>
    private Point2I? ExtractChunkCoordinatesFromHeaderSpan(ReadOnlySpan<char> headerLine)
    {
        // Try old format: "// SNBT for chunk X, Z:"
        string headerString = headerLine.ToString(); // Only convert when needed for regex
        Match oldFormatMatch = Regex.Match(headerString, @"chunk (-?\d+), (-?\d+):");
        if (oldFormatMatch.Success)
        {
            int x = int.Parse(oldFormatMatch.Groups[1].Value);
            int z = int.Parse(oldFormatMatch.Groups[2].Value);
            return new Point2I(x, z);
        }

        // Try new format: "// Chunk(X,Z)"
        Match newFormatMatch = Regex.Match(headerString, @"Chunk\((-?\d+),(-?\d+)\)");
        if (newFormatMatch.Success)
        {
            int x = int.Parse(newFormatMatch.Groups[1].Value);
            int z = int.Parse(newFormatMatch.Groups[2].Value);
            return new Point2I(x, z);
        }

        return null;
    }

    /// <summary>
    ///     Process a chunk from a list of lines (fully optimized to eliminate massive string allocations)
    /// </summary>
    private void ProcessChunkFromLines(List<string> lines, Point2I chunkCoord,
        Dictionary<Point2I, NbtCompound> chunks)
    {
        try
        {
            // Ultra-optimized: Avoid all intermediate string allocations by directly processing lines
            if (lines.Count == 0)
            {
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                return;
            }

            // Phase 1: Find actual content lines and calculate exact capacity needed
            var contentLines = new List<string>();
            int totalCapacity = 0;

            foreach (string line in lines)
            {
                // Use span to avoid trim allocation
                ReadOnlySpan<char> trimmedSpan = line.AsSpan().Trim();

                // Skip comment lines and empty lines
                if (!trimmedSpan.IsEmpty && !trimmedSpan.StartsWith("//".AsSpan()))
                {
                    contentLines.Add(line);
                    totalCapacity += line.Length;
                    if (contentLines.Count > 1)
                        totalCapacity += Environment.NewLine.Length;
                }
            }

            if (contentLines.Count == 0)
            {
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                return;
            }

            // Phase 2: Build content with exact pre-allocation (eliminates StringBuilder expansion)
            var snbtBuilder = new StringBuilder(totalCapacity + 100); // Small buffer for safety

            for (int i = 0; i < contentLines.Count; i++)
            {
                if (i > 0)
                    snbtBuilder.AppendLine();
                snbtBuilder.Append(contentLines[i]);
            }

            // Phase 3: Direct character-level validation without ToString() or Trim()
            if (snbtBuilder.Length == 0)
            {
                Console.WriteLine($@"Warning: Empty SNBT content for chunk {chunkCoord.X},{chunkCoord.Z}");
                return;
            }

            // Find content bounds using StringBuilder indexer (no string allocations)
            int startIndex = 0;
            int endIndex = snbtBuilder.Length - 1;

            // Find first non-whitespace character
            while (startIndex < snbtBuilder.Length && char.IsWhiteSpace(snbtBuilder[startIndex]))
                startIndex++;

            // Find last non-whitespace character
            while (endIndex >= startIndex && char.IsWhiteSpace(snbtBuilder[endIndex]))
                endIndex--;

            // Validate JSON structure without creating strings
            if (startIndex > endIndex || snbtBuilder[startIndex] != '{' || snbtBuilder[endIndex] != '}')
            {
                Console.WriteLine(
                    $@"Warning: Invalid SNBT format for chunk {chunkCoord.X},{chunkCoord.Z} - must start with '{{' and end with '}}'");
                return;
            }

            // Phase 4: Create minimal string only when absolutely necessary for parsing
            string chunkSnbt = snbtBuilder.ToString(startIndex, endIndex - startIndex + 1);

            // Phase 5: Parse and ensure proper root tag naming
            Failable<NbtTag> parseResult = SnbtParser.TryParse(chunkSnbt, false);
            if (parseResult.IsSuccess && parseResult.Result is NbtCompound chunkNbt)
            {
                // CRITICAL: Ensure the root tag has a name to prevent "Root tag must be named" error
                if (string.IsNullOrEmpty(chunkNbt.Name)) chunkNbt.Name = "";

                chunks[chunkCoord] = chunkNbt;
                Console.WriteLine($@"Successfully parsed chunk {chunkCoord.X},{chunkCoord.Z}");
            }
            else
            {
                Console.WriteLine(
                    $@"Warning: Parsed SNBT for chunk {chunkCoord.X},{chunkCoord.Z} did not result in a valid NBT compound: {parseResult.Exception?.Message}");
            }
        }
        catch (Exception ex)
        {
            // Log parsing error but continue with other chunks
            Console.WriteLine($@"Failed to parse chunk {chunkCoord.X},{chunkCoord.Z}: {ex.Message}");
        }
    }

    // Chunk-based MCA processing methods
    /// <summary>
    ///     Convert MCA file to individual chunk SNBT files in a folder
    /// </summary>
    public async Task ConvertMcaToChunkFilesAsync(string mcaFilePath, string outputFolderPath, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Loading MCA file: {Path.GetFileName(mcaFilePath)}...");

            // Load the MCA file
            using var mcaFile = new McaRegionFile(mcaFilePath);
            await mcaFile.LoadAsync();

            // Get existing chunks
            List<Point2I> existingChunks = mcaFile.GetExistingChunks();

            if (existingChunks.Count == 0)
            {
                progress?.Report("No chunks found in MCA file");
                return;
            }

            progress?.Report($"Found {existingChunks.Count} chunks, creating individual files...");

            // Create output directory if it doesn't exist
            Directory.CreateDirectory(outputFolderPath);

            // Create region info file with comprehensive metadata
            var regionInfo = new NbtCompound("RegionInfo");
            regionInfo.Add(new NbtString("OriginalMcaPath", mcaFilePath));
            regionInfo.Add(new NbtInt("RegionX", mcaFile.RegionCoordinates.X));
            regionInfo.Add(new NbtInt("RegionZ", mcaFile.RegionCoordinates.Z));
            regionInfo.Add(new NbtInt("TotalChunks", existingChunks.Count));
            regionInfo.Add(new NbtString("ConversionTime", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
            regionInfo.Add(new NbtString("ConversionType", "MCA to individual chunk files"));

            // Add chunk index with metadata to avoid per-file headers
            var chunkIndex = new NbtList("ChunkIndex", NbtTagType.Compound);
            foreach (Point2I chunkCoord in existingChunks)
            {
                try
                {
                    ChunkData? chunkData = await mcaFile.GetChunkAsync(chunkCoord);
                    if (chunkData?.NbtData != null)
                    {
                        var chunkMeta = new NbtCompound();
                        chunkMeta.Add(new NbtInt("X", chunkCoord.X));
                        chunkMeta.Add(new NbtInt("Z", chunkCoord.Z));
                        chunkMeta.Add(new NbtString("Compression", chunkData.CompressionType.ToString()));
                        chunkMeta.Add(new NbtInt("DataLength", chunkData.DataLength));
                        chunkMeta.Add(new NbtString("FileName", $"chunk_{chunkCoord.X}_{chunkCoord.Z}.snbt"));
                        chunkIndex.Add(chunkMeta);
                    }
                }
                catch
                {
                    // Skip chunks that can't be read
                }
            }
            regionInfo.Add(chunkIndex);

            string regionInfoPath = Path.Combine(outputFolderPath, "region_info.snbt");
            string regionInfoSnbt = regionInfo.ToSnbt(SnbtOptions.DefaultExpanded);
            await File.WriteAllTextAsync(regionInfoPath, regionInfoSnbt, Encoding.UTF8);

            // Process each chunk
            int processedCount = 0;
            foreach (Point2I chunkCoord in existingChunks)
            {
                try
                {
                    if (processedCount % 10 == 0 || processedCount == existingChunks.Count - 1)
                        progress?.Report($"Processing chunk {processedCount + 1}/{existingChunks.Count}: ({chunkCoord.X}, {chunkCoord.Z})");

                    ChunkData? chunkData = await mcaFile.GetChunkAsync(chunkCoord);
                    if (chunkData?.NbtData != null)
                    {
                        // Create filename with chunk coordinates: chunk_X_Z.snbt
                        string chunkFileName = $"chunk_{chunkCoord.X}_{chunkCoord.Z}.snbt";
                        string chunkFilePath = Path.Combine(outputFolderPath, chunkFileName);

                        // Use standard SNBT format without verbose headers to minimize file size
                        // Metadata is stored in region_info.snbt instead of per-chunk headers
                        string chunkSnbt = chunkData.NbtData.ToSnbt(SnbtOptions.Default);

                        await File.WriteAllTextAsync(chunkFilePath, chunkSnbt, Encoding.UTF8);
                    }

                    processedCount++;

                    // Yield control occasionally to prevent UI freezing
                    if (processedCount % 25 == 0)
                        await Task.Yield();
                }
                catch (Exception ex)
                {
                    progress?.Report($"Error processing chunk ({chunkCoord.X}, {chunkCoord.Z}): {ex.Message}");
                    // Continue with other chunks
                }
            }

            progress?.Report($"Conversion complete! Created {processedCount} chunk files in {outputFolderPath}");
        }
        catch (Exception ex)
        {
            progress?.Report($"Failed to convert MCA to chunk files: {ex.Message}");
            throw new InvalidOperationException($"Error converting {mcaFilePath} to chunk files: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Convert folder of chunk SNBT files back to a single MCA file
    /// </summary>
    public async Task ConvertChunkFilesToMcaAsync(string chunkFolderPath, string outputMcaPath, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Reading chunk files from: {chunkFolderPath}...");

            if (!Directory.Exists(chunkFolderPath))
                throw new DirectoryNotFoundException($"Chunk folder does not exist: {chunkFolderPath}");

            // Find all chunk SNBT files
            string[] chunkFiles = Directory.GetFiles(chunkFolderPath, "chunk_*.snbt");

            if (chunkFiles.Length == 0)
                throw new InvalidOperationException($"No chunk files found in {chunkFolderPath}");

            progress?.Report($"Found {chunkFiles.Length} chunk files, processing...");

            var chunks = new Dictionary<Point2I, NbtCompound>();
            Point2I? regionCoords = null;

            // Read region info if available
            string regionInfoPath = Path.Combine(chunkFolderPath, "region_info.snbt");
            if (File.Exists(regionInfoPath))
            {
                try
                {
                    string regionInfoSnbt = await File.ReadAllTextAsync(regionInfoPath, Encoding.UTF8);
                    var regionInfo = SnbtParser.Parse(regionInfoSnbt, false) as NbtCompound;
                    if (regionInfo != null)
                    {
                        int regionX = regionInfo.Get<NbtInt>("RegionX")?.Value ?? 0;
                        int regionZ = regionInfo.Get<NbtInt>("RegionZ")?.Value ?? 0;
                        regionCoords = new Point2I(regionX, regionZ);
                        progress?.Report($"Found region info: ({regionX}, {regionZ})");
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"Warning: Could not read region info: {ex.Message}");
                }
            }

            // Process each chunk file
            int processedCount = 0;
            foreach (string chunkFile in chunkFiles)
            {
                try
                {
                    if (processedCount % 10 == 0 || processedCount == chunkFiles.Length - 1)
                        progress?.Report($"Processing file {processedCount + 1}/{chunkFiles.Length}: {Path.GetFileName(chunkFile)}");

                    // Extract chunk coordinates from filename: chunk_X_Z.snbt
                    string fileName = Path.GetFileNameWithoutExtension(chunkFile);
                    if (!fileName.StartsWith("chunk_"))
                        continue;

                    string[] parts = fileName.Split('_');
                    if (parts.Length != 3 || !int.TryParse(parts[1], out int chunkX) || !int.TryParse(parts[2], out int chunkZ))
                    {
                        progress?.Report($"Warning: Invalid chunk filename format: {fileName}");
                        continue;
                    }

                    var chunkCoord = new Point2I(chunkX, chunkZ);

                    // Read and parse chunk SNBT
                    string chunkSnbt = await File.ReadAllTextAsync(chunkFile, Encoding.UTF8);

                    // Remove header comments and extract SNBT data
                    var lines = chunkSnbt.Split('\n');
                    var snbtLines = new List<string>();
                    bool foundSnbtStart = false;

                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (!foundSnbtStart && (trimmedLine.StartsWith("//") || string.IsNullOrEmpty(trimmedLine)))
                            continue;

                        foundSnbtStart = true;
                        snbtLines.Add(line);
                    }

                    string cleanSnbt = string.Join('\n', snbtLines);
                    var chunkNbt = SnbtParser.Parse(cleanSnbt, false) as NbtCompound;

                    if (chunkNbt != null)
                    {
                        // Ensure root tag has a name
                        if (string.IsNullOrEmpty(chunkNbt.Name))
                            chunkNbt.Name = "";

                        // Fix empty lists
                        FixEmptyLists(chunkNbt);

                        chunks[chunkCoord] = chunkNbt;
                    }

                    processedCount++;

                    // Yield control occasionally
                    if (processedCount % 25 == 0)
                        await Task.Yield();
                }
                catch (Exception ex)
                {
                    progress?.Report($"Error processing chunk file {Path.GetFileName(chunkFile)}: {ex.Message}");
                    // Continue with other files
                }
            }

            if (chunks.Count == 0)
                throw new InvalidOperationException("No valid chunk data found");

            progress?.Report($"Successfully parsed {chunks.Count} chunks, creating MCA file...");

            // Determine region coordinates if not found in region info
            if (regionCoords == null)
            {
                Point2I firstChunk = chunks.First().Key;
                int regionX = (int)Math.Floor(firstChunk.X / 32.0);
                int regionZ = (int)Math.Floor(firstChunk.Z / 32.0);
                regionCoords = new Point2I(regionX, regionZ);
                progress?.Report($"Calculated region coordinates from chunks: ({regionX}, {regionZ})");
            }

            // Verify all chunks belong to the same region
            foreach (var chunk in chunks)
            {
                int chunkRegionX = (int)Math.Floor(chunk.Key.X / 32.0);
                int chunkRegionZ = (int)Math.Floor(chunk.Key.Z / 32.0);

                if (chunkRegionX != regionCoords.Value.X || chunkRegionZ != regionCoords.Value.Z)
                    throw new InvalidOperationException(
                        $"Chunk ({chunk.Key.X}, {chunk.Key.Z}) belongs to region ({chunkRegionX}, {chunkRegionZ}), not ({regionCoords.Value.X}, {regionCoords.Value.Z})");
            }

            // Create MCA file
            using var mcaWriter = new McaRegionWriter(outputMcaPath, regionCoords.Value);

            // Add all chunks to the writer
            int chunkCount = 0;
            foreach (var chunk in chunks)
            {
                chunkCount++;
                if (chunkCount % 10 == 0 || chunkCount == chunks.Count)
                    progress?.Report($"Adding chunk {chunkCount}/{chunks.Count} to MCA file");

                mcaWriter.AddChunk(
                    chunk.Key,
                    chunk.Value,
                    CompressionType.Zlib, // Default to Zlib compression
                    (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                );
            }

            // Write the MCA file
            progress?.Report("Writing MCA file...");
            mcaWriter.WriteAsync();

            progress?.Report($"Successfully created MCA file: {Path.GetFileName(outputMcaPath)} with {chunks.Count} chunks");
        }
        catch (Exception ex)
        {
            progress?.Report($"Failed to convert chunk files to MCA: {ex.Message}");
            throw new InvalidOperationException($"Error converting chunk files to {outputMcaPath}: {ex.Message}", ex);
        }
    }
}
