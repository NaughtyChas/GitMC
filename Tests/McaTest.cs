using System;
using System.IO;
using System.Threading.Tasks;
using GitMC.Utils.Mca;
using GitMC.Utils;

namespace GitMC.Tests
{
    /// <summary>
    /// MCA Chunk parser test
    /// </summary>
    public class McaTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("MCA Region File Parser Test");
            Console.WriteLine("===========================");

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: McaTest <mca-file-path>");
                Console.WriteLine("Example: McaTest r.0.0.mca");
                return;
            }

            var mcaPath = args[0];
            
            if (!File.Exists(mcaPath))
            {
                Console.WriteLine($"File not found: {mcaPath}");
                return;
            }

            try
            {
                await TestMcaRegionFile(mcaPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static async Task TestMcaRegionFile(string mcaPath)
        {
            Console.WriteLine($"Testing MCA file: {mcaPath}");
            
            using var regionFile = new McaRegionFile(mcaPath);

            // 1. Load file
            Console.WriteLine("Loading region file...");
            await regionFile.LoadAsync();
            Console.WriteLine($"✓ Region loaded successfully");
            Console.WriteLine($"  Region coordinates: {regionFile.RegionCoordinates}");
            Console.WriteLine($"  File path: {regionFile.FilePath}");

            // 2. Validate file
            Console.WriteLine("\nValidating region file...");
            var validation = await regionFile.ValidateAsync();
            Console.WriteLine($"✓ Validation complete");
            Console.WriteLine($"  Is valid: {validation.IsValid}");
            Console.WriteLine($"  Errors: {validation.Errors.Count}");
            Console.WriteLine($"  Warnings: {validation.Warnings.Count}");

            if (validation.Errors.Count > 0)
            {
                Console.WriteLine("  Errors:");
                foreach (var error in validation.Errors)
                {
                    Console.WriteLine($"    - {error}");
                }
            }

            if (validation.Warnings.Count > 0)
            {
                Console.WriteLine("  Warnings:");
                foreach (var warning in validation.Warnings)
                {
                    Console.WriteLine($"    - {warning}");
                }
            }

            // 3. List existing chunks
            Console.WriteLine("\nListing existing chunks...");
            var existingChunks = regionFile.GetExistingChunks();
            Console.WriteLine($"✓ Found {existingChunks.Count} chunks");

            if (existingChunks.Count > 0)
            {
                Console.WriteLine("  First 10 chunks:");
                for (int i = 0; i < Math.Min(10, existingChunks.Count); i++)
                {
                    var chunk = existingChunks[i];
                    Console.WriteLine($"    [{i}] Chunk ({chunk.X}, {chunk.Z})");
                }

                // 4. Try to read first chunk data
                Console.WriteLine("\nReading first chunk data...");
                var firstChunk = existingChunks[0];
                var chunkData = await regionFile.GetChunkAsync(firstChunk);
                
                if (chunkData != null)
                {
                    Console.WriteLine($"✓ Successfully read chunk ({firstChunk.X}, {firstChunk.Z})");
                    Console.WriteLine($"  Compression type: {chunkData.CompressionType}");
                    Console.WriteLine($"  Is external: {chunkData.IsExternal}");
                    Console.WriteLine($"  Data length: {chunkData.DataLength} bytes");
                    Console.WriteLine($"  Timestamp: {chunkData.Timestamp}");
                    
                    if (chunkData.NbtData != null)
                    {
                        Console.WriteLine($"  NBT root tag: {chunkData.NbtData.Name ?? "unnamed"}");
                        Console.WriteLine($"  NBT child count: {chunkData.NbtData.Count}");
                        
                        // List first most NBT tags
                        var count = 0;
                        foreach (var tag in chunkData.NbtData)
                        {
                            if (count >= 5) break;
                            Console.WriteLine($"    - {tag.Name}: {tag.TagType}");
                            count++;
                        }
                        if (chunkData.NbtData.Count > 5)
                        {
                            Console.WriteLine($"    ... and {chunkData.NbtData.Count - 5} more tags");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"✗ Failed to read chunk ({firstChunk.X}, {firstChunk.Z})");
                }
            }

            Console.WriteLine("\n✓ MCA test completed successfully!");
        }
    }
}
