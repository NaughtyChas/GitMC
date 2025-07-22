using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitMC.Services
{
    public interface INbtService
    {
        /// <summary>
        /// Translate NBT file to SNBT format
        /// </summary>
        /// <param name="filePath">NBT file path</param>
        /// <returns>SNBT string</returns>
        Task<string> ConvertNbtToSnbtAsync(string filePath);

        /// <summary>
        /// Convert NBT file to SNBT file
        /// </summary>
        /// <param name="inputPath">Input NBT file path</param>
        /// <param name="outputPath">Output SNBT file path</param>
        void ConvertToSnbt(string inputPath, string outputPath);

        /// <summary>
        /// Convert SNBT file back to NBT file
        /// </summary>
        /// <param name="inputPath">Input SNBT file path</param>
        /// <param name="outputPath">Output NBT file path</param>
        void ConvertFromSnbt(string inputPath, string outputPath);

        /// <summary>
        /// Translate SNBT string to NBT file
        /// </summary>
        /// <param name="snbtContent">SNBT string content</param>
        /// <param name="outputPath">Output NBT file path</param>
        Task ConvertSnbtToNbtAsync(string snbtContent, string outputPath);

        /// <summary>
        /// Validate if the file is a valid NBT file
        /// </summary>
        /// <param name="filePath">File path</param>
        /// <returns>Whether it is a valid NBT file</returns>
        Task<bool> IsValidNbtFileAsync(string filePath);

        /// <summary>
        /// Get basic information about the NBT file
        /// </summary>
        /// <param name="filePath">NBT file path</param>
        /// <returns>File information string</returns>
        Task<string> GetNbtFileInfoAsync(string filePath);

        /// <summary>
        /// Validate if the SNBT string is valid
        /// </summary>
        /// <param name="snbtContent">SNBT string content</param>
        /// <returns>Whether it is a valid SNBT</returns>
        bool IsValidSnbt(string snbtContent);

        // Anvil region file methods
        /// <summary>
        /// Get information about an Anvil region file (.mca)
        /// </summary>
        /// <param name="mcaFilePath">Path to .mca file</param>
        /// <returns>Region information</returns>
        Task<AnvilRegionInfo> GetRegionInfoAsync(string mcaFilePath);

        /// <summary>
        /// List all chunks in an Anvil region file
        /// </summary>
        /// <param name="mcaFilePath">Path to .mca file</param>
        /// <returns>List of chunk information</returns>
        Task<List<AnvilChunkInfo>> ListChunksInRegionAsync(string mcaFilePath);

        /// <summary>
        /// Extract and convert chunk data to SNBT format
        /// </summary>
        /// <param name="mcaFilePath">Path to .mca file</param>
        /// <param name="chunkX">Chunk X coordinate</param>
        /// <param name="chunkZ">Chunk Z coordinate</param>
        /// <returns>Chunk data in SNBT format</returns>
        Task<string> ExtractChunkDataAsync(string mcaFilePath, int chunkX, int chunkZ);

        /// <summary>
        /// Validate if the file is a valid Anvil file (.mca or .mcc)
        /// </summary>
        /// <param name="filePath">File path</param>
        /// <returns>Whether it is a valid Anvil file</returns>
        Task<bool> IsValidAnvilFileAsync(string filePath);
    }

    public class AnvilRegionInfo
    {
        public int RegionX { get; set; }
        public int RegionZ { get; set; }
        public required string FilePath { get; set; }
        public long FileSize { get; set; }
        public int TotalChunks { get; set; }
        public int ValidChunks { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class AnvilChunkInfo
    {
        public int ChunkX { get; set; }
        public int ChunkZ { get; set; }
        public int LocalX { get; set; }
        public int LocalZ { get; set; }
        public uint SectorOffset { get; set; }
        public uint SectorCount { get; set; }
        public uint Timestamp { get; set; }
        public DateTime LastModified { get; set; }
        public long DataSize { get; set; }
        public AnvilCompressionType CompressionType { get; set; }
        public bool IsValid { get; set; }
        public bool IsOversized { get; set; }
    }

    public enum AnvilCompressionType
    {
        GZip = 1,
        Zlib = 2,
        Uncompressed = 3,
        LZ4 = 4,
        Custom = 127
    }
}
