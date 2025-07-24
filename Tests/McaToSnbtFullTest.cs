using System;
using System.IO;
using GitMC.Services;

namespace GitMC.Tests
{
    public class McaToSnbtFullTest
    {
        public static void RunTest()
        {
            Console.WriteLine("MCA -> SNBT Full Test...");
            
            string[] testFiles = {
                @"G:\Projects\GitMC\mcfiles\region\r.0.-1.mca",
                @"G:\Projects\GitMC\mcfiles\entities\r.-3.0.mca", 
                @"G:\Projects\GitMC\mcfiles\poi\r.-3.0.mca"
            };

            var nbtService = new NbtService();

            foreach (string filePath in testFiles)
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"Where the file: {filePath}?");
                    continue;
                }

                Console.WriteLine($"\nTesting file: {Path.GetFileName(filePath)}");
                string outputPath = Path.ChangeExtension(filePath, ".full.snbt");
                
                try
                {
                    // MCA -> SNBT
                    nbtService.ConvertToSnbt(filePath, outputPath);
                    Console.WriteLine($"  ✓ Success: {outputPath}");
                    
                    // Check output file
                    if (File.Exists(outputPath))
                    {
                        var fileInfo = new FileInfo(outputPath);
                        Console.WriteLine($"  Output file size: {fileInfo.Length:N0} bytes");
                        
                        // Read / Analyze
                        var content = File.ReadAllText(outputPath);
                        
                        // Check if it contains data
                        if (content.Contains("block_states") && content.Contains("biomes"))
                        {
                            Console.WriteLine($"  ✓ Include biomes and block states!");
                        }
                        else if (content.Contains("// SNBT for chunk"))
                        {
                            Console.WriteLine($"  ✓ Include chunk comments!");
                        }
                        else if (content.Contains("MCAParsingError"))
                        {
                            Console.WriteLine($"  ⚠️ MCA parsing error");
                        }
                        else
                        {
                            Console.WriteLine($"  ❓ What file did you generated?");
                        }
                        
                        // Calculate chunk count
                        int chunkCount = 0;
                        string[] lines = content.Split('\n');
                        foreach (string line in lines)
                        {
                            if (line.Trim().StartsWith("// SNBT for chunk"))
                            {
                                chunkCount++;
                            }
                        }
                        
                        if (chunkCount > 0)
                        {
                            Console.WriteLine($"  Chunks: {chunkCount}");
                        }
                        
                        // Display comment
                        var preview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                        Console.WriteLine($"  Preview: {preview.Replace('\n', ' ').Replace('\r', ' ')}");
                        
                        // Test reverse
                        string reconstructedPath = Path.ChangeExtension(filePath, ".reconstructed.mca");
                        try
                        {
                            nbtService.ConvertFromSnbt(outputPath, reconstructedPath);
                            Console.WriteLine($"  ✓ Reverse testing complete!: {reconstructedPath}");
                            
                            if (File.Exists(reconstructedPath + ".reconstruction.snbt"))
                            {
                                Console.WriteLine($"  ✓ Generated reconstruction file");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ✗ Reverse conversion fail!: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Conversion failed: {ex.Message}");
                    Console.WriteLine($"    Details: {ex}");
                }
            }
            
            Console.WriteLine("\nTest complete！");
        }
    }
}
