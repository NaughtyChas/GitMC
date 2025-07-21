using System;
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
    }
}
