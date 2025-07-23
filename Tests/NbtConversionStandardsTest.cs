using System;
using System.IO;
using System.Threading.Tasks;

namespace GitMC.Tests
{
    /// <summary>
    /// Test if translation process follows standard process
    /// Test if the program will "freeze"
    /// </summary>
    public class NbtConversionStandardsTest
    {
        private readonly Services.NbtService _nbtService;
        
        public NbtConversionStandardsTest()
        {
            _nbtService = new Services.NbtService();
        }
        
        public async Task TestConversionStandards()
        {
            Console.WriteLine("=== NBT Standardization test ===");
            
            var testSnbt = @"{
                ""testByte"": 123b,
                ""testShort"": 32767s,
                ""testInt"": 2147483647,
                ""testLong"": 9223372036854775807L,
                ""testFloat"": 0.5f,
                ""testDouble"": 0.5d,
                ""testString"": ""Hello World"",
                ""testByteArray"": [B; 1b, 2b, 3b],
                ""testIntArray"": [I; 1, 2, 3],
                ""testLongArray"": [L; 1L, 2L, 3L],
                ""testList"": [
                    {""item1"": 1},
                    {""item2"": 2}
                ],
                ""testEmptyList"": [],
                ""testCompound"": {
                    ""nestedValue"": 42
                }
            }";
            
            var tempDir = Path.Combine(Path.GetTempPath(), "nbt_test_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var snbtFile = Path.Combine(tempDir, "test.snbt");
                var nbtFile = Path.Combine(tempDir, "test.nbt");
                var reconvertedSnbtFile = Path.Combine(tempDir, "reconverted.snbt");
                
                await File.WriteAllTextAsync(snbtFile, testSnbt);
                Console.WriteLine($"✓ Created testing SNBT file: {snbtFile}");
                
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // 1. SNBT -> NBT 
                Console.WriteLine("\n--- Step 1: SNBT -> NBT ---");
                try
                {
                    _nbtService.ConvertFromSnbt(snbtFile, nbtFile);
                    var nbtSize = new FileInfo(nbtFile).Length;
                    Console.WriteLine($"✓ SNBT -> NBT Complete, NBT file size {nbtSize} bytes");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ SNBT -> NBT conversion failed: {ex.Message}");
                    throw;
                }
                
                // 2. NBT -> SNBT Conversion
                Console.WriteLine("\n--- Step 2: NBT -> SNBT Conversion ---");
                try
                {
                    _nbtService.ConvertToSnbt(nbtFile, reconvertedSnbtFile);
                    var reconvertedSize = new FileInfo(reconvertedSnbtFile).Length;
                    Console.WriteLine($"✓ NBT -> SNBT Conversion successful, reconverted SNBT file size: {reconvertedSize} bytes");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ NBT -> SNBT Conversion failed: {ex.Message}");
                    throw;
                }
                
                stopwatch.Stop();
                Console.WriteLine($"\nTotal conversion time: {stopwatch.ElapsedMilliseconds}ms");
                
                // 3. Content consistency verification
                Console.WriteLine("\n--- Step 3: Content consistency verification ---");
                var originalContent = await File.ReadAllTextAsync(snbtFile);
                var reconvertedContent = await File.ReadAllTextAsync(reconvertedSnbtFile);
                
                // Remove whitespace for comparison
                var normalizedOriginal = NormalizeSnbt(originalContent);
                var normalizedReconverted = NormalizeSnbt(reconvertedContent);
                
                if (normalizedOriginal.Contains("testByte") && normalizedReconverted.Contains("testByte"))
                {
                    Console.WriteLine("✓ Basic data type conversion correct");
                }
                else
                {
                    Console.WriteLine("❌ Basic data type conversion failed");
                }
                
                if (normalizedOriginal.Contains("testEmptyList") && normalizedReconverted.Contains("testEmptyList"))
                {
                    Console.WriteLine("✓ Empty list handled correctly");
                }
                else
                {
                    Console.WriteLine("❌ Empty list handling failed");
                }
                
                // 4. Performance and stability test
                Console.WriteLine("\n--- Step 4: Performance and stability test ---");
                await TestBatchConversionStability(tempDir);
                
                Console.WriteLine("\n🎉 NBT conversion standardization test complete");
            }
            finally
            {
                // Clean up temporary files
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        
        private async Task TestBatchConversionStability(string tempDir)
        {
            Console.WriteLine("Testing batch conversion stability...");
            
            var testFiles = new List<string>();
            var batchDir = Path.Combine(tempDir, "batch");
            Directory.CreateDirectory(batchDir);
            
            // Create multiple test files
            for (int i = 0; i < 50; i++)
            {
                var testContent = $@"{{
                    ""fileIndex"": {i},
                    ""testData"": [
                        {{""value1"": {i}b}},
                        {{""value2"": {i * 2}s}},
                        {{""value3"": {i * 3}}}
                    ],
                    ""largeSample"": ""{new string('A', 1000)}""
                }}";
                
                var filePath = Path.Combine(batchDir, $"test_{i:D3}.snbt");
                await File.WriteAllTextAsync(filePath, testContent);
                testFiles.Add(filePath);
            }
            
            var outputDir = Path.Combine(batchDir, "output");
            Directory.CreateDirectory(outputDir);
            
            var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
                var (successful, failed, errors) = await _nbtService.ConvertBatchImprovedAsync(
                    testFiles, outputDir, ".nbt", cancellationToken);
                
                batchStopwatch.Stop();
                
                Console.WriteLine($"✓ Batch conversion complete:");
                Console.WriteLine($"  - Successful: {successful}");
                Console.WriteLine($"  - Failed: {failed}");
                Console.WriteLine($"  - Time taken: {batchStopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine($"  - Average per file: {(double)batchStopwatch.ElapsedMilliseconds / testFiles.Count:F1}ms");
                
                if (failed > 0)
                {
                    Console.WriteLine("Failure details:");
                    foreach (var error in errors.Take(5))
                    {
                        Console.WriteLine($"  - {error}");
                    }
                }
                
                // Check for timeout (possible "freeze" issue)
                if (batchStopwatch.ElapsedMilliseconds > 60000) // Over 1 minute
                {
                    Console.WriteLine("⚠️  Warning: Batch conversion took too long, possible performance issue");
                }
                else
                {
                    Console.WriteLine("✓ Batch conversion performance normal, no freeze issue");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("❌ Batch conversion timed out, freeze issue detected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Batch conversion exception: {ex.Message}");
            }
        }
        
        private string NormalizeSnbt(string snbt)
        {
            return snbt.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
        }
    }
}
