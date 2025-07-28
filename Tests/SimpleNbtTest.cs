using System.Text;
using fNbt;
using GitMC.Services;

namespace GitMC.Tests
{
    public class SimpleNbtTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine(@"=== NBT to SNBT Conversion Test ===");
            Console.WriteLine();

            try
            {
                // Create a simple NBT structure
                var root = new NbtCompound("TestRoot");
                root.Add(new NbtString("message", "Hello World!"));
                root.Add(new NbtInt("number", 42));
                root.Add(new NbtFloat("pi", 3.14159f));
                root.Add(new NbtByte("flag", 1));

                // Create a list
                var list = new NbtList("items", NbtTagType.String);
                list.Add(new NbtString("apple"));
                list.Add(new NbtString("banana"));
                list.Add(new NbtString("cherry"));
                root.Add(list);

                // Create arrays
                root.Add(new NbtByteArray("bytes", new byte[] { 1, 2, 3, 4, 5 }));
                root.Add(new NbtIntArray("ints", new[] { 10, 20, 30 }));

                Console.WriteLine(@"1. Created NBT structure:");
                Console.WriteLine($@"   Root: {root.Name} ({root.TagType})");
                Console.WriteLine($@"   Children: {root.Count}");
                Console.WriteLine();

                // Save as NBT file
                var nbtFile = new NbtFile(root);
                var tempNbtPath = Path.GetTempFileName() + ".nbt";
                nbtFile.SaveToFile(tempNbtPath, NbtCompression.GZip);
                Console.WriteLine($@"2. Saved NBT file: {tempNbtPath}");
                Console.WriteLine();

                // Convert to SNBT using our implementation
                var nbtService = new NbtService();
                var snbtContent = await nbtService.ConvertNbtToSnbtAsync(tempNbtPath);
                
                Console.WriteLine(@"3. Converted to SNBT:");
                Console.WriteLine(snbtContent);
                Console.WriteLine();

                // Save SNBT file
                var tempSnbtPath = Path.GetTempFileName() + ".snbt";
                File.WriteAllText(tempSnbtPath, snbtContent, Encoding.UTF8);
                Console.WriteLine($@"4. Saved SNBT file: {tempSnbtPath}");
                Console.WriteLine();

                // Convert back to NBT
                var finalNbtPath = Path.GetTempFileName() + ".nbt";
                await nbtService.ConvertSnbtToNbtAsync(snbtContent, finalNbtPath);
                Console.WriteLine($@"5. Converted back to NBT: {finalNbtPath}");
                Console.WriteLine();

                // Verify the round-trip
                var finalNbtFile = new NbtFile();
                finalNbtFile.LoadFromFile(finalNbtPath);
                var finalRoot = finalNbtFile.RootTag;

                Console.WriteLine(@"6. Round-trip verification:");
                Console.WriteLine($@"   Original tags: {root.Count}");
                Console.WriteLine($@"   Final tags: {finalRoot?.Count ?? 0}");
                
                if (finalRoot != null)
                {
                    var messageTag = finalRoot.Get<NbtString>("message");
                    var numberTag = finalRoot.Get<NbtInt>("number");
                    var piTag = finalRoot.Get<NbtFloat>("pi");
                    
                    Console.WriteLine($@"   Message: '{messageTag?.Value}' (expected: 'Hello World!')");
                    Console.WriteLine($@"   Number: {numberTag?.Value} (expected: 42)");
                    Console.WriteLine($@"   Pi: {piTag?.Value} (expected: 3.14159)");
                    
                    if (messageTag?.Value == "Hello World!" && 
                        numberTag?.Value == 42 && 
                        Math.Abs(piTag?.Value - 3.14159f ?? float.MaxValue) < 0.0001f)
                    {
                        Console.WriteLine();
                        Console.WriteLine(@"✅ SUCCESS: Round-trip conversion works correctly!");
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine(@"❌ ERROR: Round-trip conversion failed!");
                    }
                }
                else
                {
                    Console.WriteLine(@"❌ ERROR: Could not load final NBT file!");
                }

                // Clean up
                File.Delete(tempNbtPath);
                File.Delete(tempSnbtPath);
                File.Delete(finalNbtPath);
                Console.WriteLine();
                Console.WriteLine(@"7. Cleaned up temporary files.");

            }
            catch (Exception ex)
            {
                Console.WriteLine($@"❌ ERROR: {ex.Message}");
                Console.WriteLine($@"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine();
            Console.WriteLine(@"Test completed. Press any key to exit...");
            Console.ReadKey();
        }
    }
}
