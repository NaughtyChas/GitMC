using System;

namespace GitMC.Services
{
    /// <summary>
    /// Defines log levels for the application
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Critical = 5
    }

    /// <summary>
    /// Defines log categories for better organization
    /// </summary>
    public enum LogCategory
    {
        General,
        Git,
        NBT,
        Translation,
        UI,
        FileSystem,
        GitHub,
        Session,
        Configuration,
        Manifest,
        Operations
    }

    /// <summary>
    /// Logging service interface for structured application logging
    /// </summary>
    public interface ILoggingService
    {
        /// <summary>
        /// Gets or sets the minimum log level to output
        /// </summary>
        LogLevel MinimumLogLevel { get; set; }

        /// <summary>
        /// Logs a trace message (most verbose)
        /// </summary>
        void LogTrace(LogCategory category, string message, params object[] args);
        void LogTrace(LogCategory category, string message, Exception? exception = null, params object[] args);

        /// <summary>
        /// Logs a debug message
        /// </summary>
        void LogDebug(LogCategory category, string message, params object[] args);
        void LogDebug(LogCategory category, string message, Exception? exception = null, params object[] args);

        /// <summary>
        /// Logs an informational message
        /// </summary>
        void LogInfo(LogCategory category, string message, params object[] args);
        void LogInfo(LogCategory category, string message, Exception? exception = null, params object[] args);

        /// <summary>
        /// Logs a warning message
        /// </summary>
        void LogWarning(LogCategory category, string message, params object[] args);
        void LogWarning(LogCategory category, string message, Exception? exception = null, params object[] args);

        /// <summary>
        /// Logs an error message
        /// </summary>
        void LogError(LogCategory category, string message, params object[] args);
        void LogError(LogCategory category, string message, Exception? exception = null, params object[] args);

        /// <summary>
        /// Logs a critical error message
        /// </summary>
        void LogCritical(LogCategory category, string message, params object[] args);
        void LogCritical(LogCategory category, string message, Exception? exception = null, params object[] args);

        /// <summary>
        /// Starts a timed operation for performance tracking
        /// </summary>
        IDisposable BeginTimedOperation(LogCategory category, string operationName);

        /// <summary>
        /// Logs method entry (for detailed tracing)
        /// </summary>
        void LogMethodEntry(LogCategory category, string methodName, params object[] parameters);

        /// <summary>
        /// Logs method exit (for detailed tracing)
        /// </summary>
        void LogMethodExit(LogCategory category, string methodName, object? returnValue = null);

        /// <summary>
        /// Enables or disables logging for a specific category
        /// </summary>
        void SetCategoryEnabled(LogCategory category, bool enabled);

        /// <summary>
        /// Checks if a log level would be written
        /// </summary>
        bool IsEnabled(LogLevel level);

        /// <summary>
        /// Checks if a category is enabled
        /// </summary>
        bool IsCategoryEnabled(LogCategory category);
    }
}
