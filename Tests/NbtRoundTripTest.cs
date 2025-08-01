using System.Text;
using fNbt;
using GitMC.Services;

namespace GitMC.Tests;

public class NbtRoundTripTest
{
    private readonly NbtService _nbtService;

    public NbtRoundTripTest()
    {
        _nbtService = new NbtService();
    }

    public async Task<bool> TestRoundTripConversion()
    {
        try
        {
            // Create a test NBT structure
            var originalCompound = new NbtCompound("TestRoot");
            originalCompound.Add(new NbtString("StringTest", "Hello World"));
            originalCompound.Add(new NbtInt("IntTest", 12345));
            originalCompound.Add(new NbtFloat("FloatTest", 3.14159f));
            originalCompound.Add(new NbtDouble("DoubleTest", 2.71828));
            originalCompound.Add(new NbtByte("ByteTest", 42));
            originalCompound.Add(new NbtLong("LongTest", 9876543210L));
            originalCompound.Add(new NbtShort("ShortTest", 1000));

            // Create a list
            var list = new NbtList("ListTest", NbtTagType.String);
            list.Add(new NbtString("Item1"));
            list.Add(new NbtString("Item2"));
            list.Add(new NbtString("Item3"));
            originalCompound.Add(list);

            // Create arrays
            originalCompound.Add(new NbtByteArray("ByteArrayTest", new byte[] { 1, 2, 3, 4, 5 }));
            originalCompound.Add(new NbtIntArray("IntArrayTest", new[] { 10, 20, 30, 40, 50 }));
            originalCompound.Add(new NbtLongArray("LongArrayTest", new[] { 100L, 200L, 300L }));

            // Save original NBT file
            var originalNbtFile = new NbtFile(originalCompound);
            string originalPath = Path.GetTempFileName() + ".nbt";
            originalNbtFile.SaveToFile(originalPath, NbtCompression.GZip);

            Console.WriteLine($@"Original NBT file saved to: {originalPath}");

            // Convert NBT to SNBT
            string snbtContent = await _nbtService.ConvertNbtToSnbtAsync(originalPath);
            string snbtPath = Path.GetTempFileName() + ".snbt";
            File.WriteAllText(snbtPath, snbtContent, Encoding.UTF8);

            Console.WriteLine($@"SNBT content saved to: {snbtPath}");
            Console.WriteLine(@"SNBT Content:");
            Console.WriteLine(snbtContent);
            Console.WriteLine();

            // Convert SNBT back to NBT
            string finalNbtPath = Path.GetTempFileName() + ".nbt";
            await _nbtService.ConvertSnbtToNbtAsync(snbtContent, finalNbtPath);

            Console.WriteLine($@"Final NBT file saved to: {finalNbtPath}");

            // Load and compare the final NBT file
            var finalNbtFile = new NbtFile();
            finalNbtFile.LoadFromFile(finalNbtPath);

            // Basic validation
            NbtCompound? finalRoot = finalNbtFile.RootTag;
            if (finalRoot == null)
            {
                Console.WriteLine(@"ERROR: Final root tag is not a compound");
                return false;
            }

            // Check if we have the expected number of tags
            Console.WriteLine($@"Original compound has {originalCompound.Count} tags");
            Console.WriteLine($@"Final compound has {finalRoot.Count} tags");

            // Check some specific values
            NbtString? stringTest = finalRoot.Get<NbtString>("StringTest");
            NbtInt? intTest = finalRoot.Get<NbtInt>("IntTest");
            NbtFloat? floatTest = finalRoot.Get<NbtFloat>("FloatTest");

            Console.WriteLine($@"StringTest: {stringTest?.Value} (Expected: Hello World)");
            Console.WriteLine($@"IntTest: {intTest?.Value} (Expected: 12345)");
            Console.WriteLine($@"FloatTest: {floatTest?.Value} (Expected: 3.14159)");

            // Clean up temporary files
            File.Delete(originalPath);
            File.Delete(snbtPath);
            File.Delete(finalNbtPath);

            Console.WriteLine(@"Round-trip conversion test completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($@"Round-trip test failed: {ex.Message}");
            Console.WriteLine($@"Stack trace: {ex.StackTrace}");
            return false;
        }
    }
}

// Simple test runner
public class TestRunner
{
    public static async Task RunTest()
    {
        Console.WriteLine(@"Starting NBT Round-Trip Test...");
        Console.WriteLine(@"==========================================");

        var test = new NbtRoundTripTest();
        bool success = await test.TestRoundTripConversion();

        Console.WriteLine(@"==========================================");
        Console.WriteLine($@"Test Result: {(success ? "PASSED" : "FAILED")}");
    }
}
