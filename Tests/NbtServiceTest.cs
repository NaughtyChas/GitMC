using GitMC.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GitMC.Tests
{
    internal class NbtServiceTest
    {
        public static async Task TestNbtService()
        {
            var nbtService = new NbtService();

            // This is a simple SNBT structure for testing purposes
            var testSnbt = @"{
    ""test"": ""Hello World"",
    ""number"": 42,
    ""float"": 3.14f,
    ""list"": [1, 2, 3],
    ""nested"": {
        ""key"": ""value""
    }
}";

            try
            {
                // Test SNBT validation
                Console.WriteLine($"SNBT validation result: {nbtService.IsValidSnbt(testSnbt)}");

                // Test conversion to NBT
                var tempFile = Path.GetTempFileName() + ".nbt";
                await nbtService.ConvertSnbtToNbtAsync(testSnbt, tempFile);
                Console.WriteLine($"NBT file created: {tempFile}");

                // Test file validation
                var isValid = await nbtService.IsValidNbtFileAsync(tempFile);
                Console.WriteLine($"File validation result: {isValid}");

                // Test file info
                var fileInfo = await nbtService.GetNbtFileInfoAsync(tempFile);
                Console.WriteLine($"File info:\n{fileInfo}");

                // Test conversion back to SNBT
                var convertedSnbt = await nbtService.ConvertNbtToSnbtAsync(tempFile);
                Console.WriteLine($"Converted back to SNBT:\n{convertedSnbt}");

                // Clean up
                File.Delete(tempFile);
                Console.WriteLine("Test completed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed: {ex.Message}");
            }
        }
    }
}
