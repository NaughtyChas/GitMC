using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
                        rootTag = NbtFile.Read(filePath, FormatOptions.Java, CompressionType.AutoDetect);
                    }
                    catch
                    {
                        try
                        {
                            // Try reading the Bedrock format
                            rootTag = NbtFile.Read(filePath, FormatOptions.BedrockNetwork, CompressionType.AutoDetect);
                        }
                        catch
                        {
                            // Try reading the Bedrock format
                            rootTag = NbtFile.Read(filePath, FormatOptions.BedrockFile, CompressionType.AutoDetect);
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
                    if (rootTag is CompoundTag compound)
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
                        _ = NbtFile.Read(filePath, FormatOptions.Java, CompressionType.AutoDetect);
                        return true;
                    }
                    catch
                    {
                        try
                        {
                            _ = NbtFile.Read(filePath, FormatOptions.BedrockNetwork, CompressionType.AutoDetect);
                            return true;
                        }
                        catch
                        {
                            try
                            {
                                _ = NbtFile.Read(filePath, FormatOptions.BedrockFile, CompressionType.AutoDetect);
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
                    CompoundTag? rootTag = null;
                    string compressionType = "Unknown";
                    string formatType = "Unknown";

                    try
                    {
                        rootTag = NbtFile.Read(filePath, FormatOptions.Java, CompressionType.AutoDetect);
                        formatType = "Java version";
                        compressionType = DetermineCompressionType(filePath);
                    }
                    catch
                    {
                        try
                        {
                            rootTag = NbtFile.Read(filePath, FormatOptions.BedrockNetwork, CompressionType.AutoDetect);
                            formatType = "Bedrock Version (Network)";
                            compressionType = DetermineCompressionType(filePath);
                        }
                        catch
                        {
                            try
                            {
                                rootTag = NbtFile.Read(filePath, FormatOptions.BedrockFile, CompressionType.AutoDetect);
                                formatType = "Bedrock Version (File)";
                                compressionType = DetermineCompressionType(filePath);
                            }
                            catch
                            {
                                return info.ToString() + "Error reading NBT file: Unable to determine format or compression type.";
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
    }
}
