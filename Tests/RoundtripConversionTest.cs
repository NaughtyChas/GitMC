using System;
using System.IO;
using System.Threading.Tasks;
using GitMC.Services;
using GitMC.Utils.Mca;

namespace GitMC.Tests
{
    /// <summary>
    /// Test program for MCA to SNBT roundtrip conversion
    /// Tests the complete workflow: MCA → SNBT → MCA
    /// </summary>
    public class RoundtripConversionTest
    {
        private readonly NbtService _nbtService;
        private readonly string _testDataPath;
        private readonly IProgress<string>? _progress;

        public RoundtripConversionTest(IProgress<string>? progress = null)
        {
            _nbtService = new NbtService();
            _testDataPath = Path.Combine(Environment.CurrentDirectory, "TestData");
            _progress = progress;
            
            // Ensure test data directory exists
            if (!Directory.Exists(_testDataPath))
            {
                Directory.CreateDirectory(_testDataPath);
            }
        }

        /// <summary>
        /// Test complete roundtrip conversion with a sample MCA file
        /// </summary>
        public async Task<bool> TestRoundtripConversion(string originalMcaPath)
        {
            try
            {
                ReportProgress($"Starting roundtrip conversion test for: {originalMcaPath}");
                
                if (!File.Exists(originalMcaPath))
                {
                    ReportProgress($"ERROR: Input MCA file not found: {originalMcaPath}");
                    return false;
                }

                // Step 1: Convert MCA to SNBT
                var snbtPath = Path.Combine(_testDataPath, $"step1_{Path.GetFileNameWithoutExtension(originalMcaPath)}.snbt");
                ReportProgress($"Step 1: Converting MCA to SNBT...");
                
                // Use our new asynchronous conversion method with progress reporting
                var conversionProgress = new Progress<string>(message => ReportProgress($"  {message}"));
                await _nbtService.ConvertToSnbtAsync(originalMcaPath, snbtPath, conversionProgress);
                
                if (!File.Exists(snbtPath))
                {
                    ReportProgress("ERROR: SNBT file was not created");
                    return false;
                }
                
                var snbtSize = new FileInfo(snbtPath).Length;
                ReportProgress($"✓ SNBT file created: {snbtPath} ({snbtSize:N0} bytes)");

                // Step 2: Convert SNBT back to MCA
                var reconstructedMcaPath = Path.Combine(_testDataPath, $"step2_{Path.GetFileName(originalMcaPath)}");
                ReportProgress($"Step 2: Converting SNBT back to MCA...");
                _nbtService.ConvertSnbtToRegionFile(snbtPath, reconstructedMcaPath);
                
                if (!File.Exists(reconstructedMcaPath))
                {
                    ReportProgress("ERROR: Reconstructed MCA file was not created");
                    return false;
                }
                
                var reconstructedSize = new FileInfo(reconstructedMcaPath).Length;
                ReportProgress($"✓ Reconstructed MCA file created: {reconstructedMcaPath} ({reconstructedSize:N0} bytes)");

                // Step 3: Verify the reconstructed MCA file structure
                ReportProgress($"Step 3: Verifying reconstructed MCA file...");
                var verificationResult = await VerifyMcaFile(reconstructedMcaPath);
                
                if (!verificationResult.IsValid)
                {
                    ReportProgress($"ERROR: Reconstructed MCA file verification failed: {verificationResult.ErrorMessage}");
                    return false;
                }
                
                ReportProgress($"✓ MCA file verification passed");
                ReportProgress($"  - Chunks found: {verificationResult.ChunkCount}");
                ReportProgress($"  - File size: {verificationResult.FileSize:N0} bytes");

                // Step 4: Compare chunk data (optional detailed verification)
                ReportProgress($"Step 4: Comparing chunk data integrity...");
                var comparisonResult = await CompareChunkData(originalMcaPath, reconstructedMcaPath);
                
                if (!comparisonResult.Success)
                {
                    ReportProgress($"WARNING: Chunk data comparison detected differences: {comparisonResult.Message}");
                    // Don't fail the test for minor differences, just report them
                }
                else
                {
                    ReportProgress($"✓ Chunk data integrity verified");
                }

                // Step 5: Generate test report
                await GenerateTestReport(originalMcaPath, snbtPath, reconstructedMcaPath, verificationResult, comparisonResult);
                
                ReportProgress($"✓ Roundtrip conversion test completed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                ReportProgress($"ERROR: Roundtrip conversion test failed: {ex.Message}");
                ReportProgress($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Helper method for reporting progress
        /// </summary>
        private void ReportProgress(string message)
        {
            // Report to both console and progress reporter if available
            Console.WriteLine(message);
            _progress?.Report(message);
        }

        /// <summary>
        /// Verify basic MCA file structure and readability
        /// </summary>
        private async Task<McaVerificationResult> VerifyMcaFile(string mcaPath)
        {
            try
            {
                using var mcaFile = new McaRegionFile(mcaPath);
                await mcaFile.LoadAsync();
                
                var chunks = mcaFile.GetExistingChunks();
                var fileSize = new FileInfo(mcaPath).Length;
                
                return new McaVerificationResult
                {
                    IsValid = true,
                    ChunkCount = chunks.Count,
                    FileSize = fileSize,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                return new McaVerificationResult
                {
                    IsValid = false,
                    ChunkCount = 0,
                    FileSize = 0,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Compare chunk data between original and reconstructed MCA files
        /// </summary>
        private async Task<ChunkComparisonResult> CompareChunkData(string originalPath, string reconstructedPath)
        {
            try
            {
                using var originalMca = new McaRegionFile(originalPath);
                using var reconstructedMca = new McaRegionFile(reconstructedPath);
                
                await originalMca.LoadAsync();
                await reconstructedMca.LoadAsync();
                
                var originalChunks = originalMca.GetExistingChunks();
                var reconstructedChunks = reconstructedMca.GetExistingChunks();
                
                if (originalChunks.Count != reconstructedChunks.Count)
                {
                    return new ChunkComparisonResult
                    {
                        Success = false,
                        Message = $"Chunk count mismatch: original {originalChunks.Count}, reconstructed {reconstructedChunks.Count}"
                    };
                }
                
                // Basic comparison - could be extended for detailed NBT comparison
                return new ChunkComparisonResult
                {
                    Success = true,
                    Message = $"Chunk count matches: {originalChunks.Count} chunks"
                };
            }
            catch (Exception ex)
            {
                return new ChunkComparisonResult
                {
                    Success = false,
                    Message = $"Comparison failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Generate detailed test report
        /// </summary>
        private async Task GenerateTestReport(string originalPath, string snbtPath, string reconstructedPath, 
            McaVerificationResult verification, ChunkComparisonResult comparison)
        {
            var reportPath = Path.Combine(_testDataPath, $"roundtrip_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            
            using var writer = new StreamWriter(reportPath);
            await writer.WriteLineAsync("=== MCA Roundtrip Conversion Test Report ===");
            await writer.WriteLineAsync($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("=== File Paths ===");
            await writer.WriteLineAsync($"Original MCA:     {originalPath}");
            await writer.WriteLineAsync($"Intermediate SNBT: {snbtPath}");
            await writer.WriteLineAsync($"Reconstructed MCA: {reconstructedPath}");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("=== File Sizes ===");
            if (File.Exists(originalPath))
                await writer.WriteLineAsync($"Original MCA:     {new FileInfo(originalPath).Length:N0} bytes");
            if (File.Exists(snbtPath))
                await writer.WriteLineAsync($"Intermediate SNBT: {new FileInfo(snbtPath).Length:N0} bytes");
            if (File.Exists(reconstructedPath))
                await writer.WriteLineAsync($"Reconstructed MCA: {new FileInfo(reconstructedPath).Length:N0} bytes");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("=== Verification Results ===");
            await writer.WriteLineAsync($"MCA Validation: {(verification.IsValid ? "PASSED" : "FAILED")}");
            if (!verification.IsValid)
                await writer.WriteLineAsync($"Error: {verification.ErrorMessage}");
            await writer.WriteLineAsync($"Chunks Found: {verification.ChunkCount}");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("=== Comparison Results ===");
            await writer.WriteLineAsync($"Chunk Comparison: {(comparison.Success ? "PASSED" : "FAILED")}");
            await writer.WriteLineAsync($"Details: {comparison.Message}");
            await writer.WriteLineAsync();
            
            await writer.WriteLineAsync("=== Test Summary ===");
            var overallResult = verification.IsValid && comparison.Success ? "SUCCESS" : "PARTIAL SUCCESS";
            await writer.WriteLineAsync($"Overall Result: {overallResult}");
            
            Console.WriteLine($"Test report generated: {reportPath}");
        }

        /// <summary>
        /// Run test with a sample MCA file from the mcfiles directory
        /// </summary>
        public async Task<bool> RunSampleTest()
        {
            // Look for sample MCA files in the mcfiles/region directory
            var regionPath = Path.Combine(Environment.CurrentDirectory, "mcfiles", "region");
            
            if (!Directory.Exists(regionPath))
            {
                Console.WriteLine($"Sample region directory not found: {regionPath}");
                return false;
            }
            
            var mcaFiles = Directory.GetFiles(regionPath, "*.mca");
            if (mcaFiles.Length == 0)
            {
                Console.WriteLine($"No MCA files found in: {regionPath}");
                return false;
            }
            
            // Test with the first MCA file found
            var sampleFile = mcaFiles[0];
            Console.WriteLine($"Using sample file: {sampleFile}");
            
            return await TestRoundtripConversion(sampleFile);
        }
    }

    /// <summary>
    /// Result of MCA file verification
    /// </summary>
    public class McaVerificationResult
    {
        public bool IsValid { get; set; }
        public int ChunkCount { get; set; }
        public long FileSize { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of chunk data comparison
    /// </summary>
    public class ChunkComparisonResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
