using System;
using System.Runtime.CompilerServices;
using GitMC.Services;

namespace GitMC.Extensions
{
    /// <summary>
    /// Extension methods for convenient logging
    /// </summary>
    public static class LoggingExtensions
    {
        /// <summary>
        /// Logs method entry with automatic method name detection
        /// </summary>
        public static void LogEntry(this ILoggingService logger, LogCategory category, [CallerMemberName] string methodName = "", params object[] parameters)
        {
            logger.LogMethodEntry(category, methodName, parameters);
        }

        /// <summary>
        /// Logs method exit with automatic method name detection
        /// </summary>
        public static void LogExit(this ILoggingService logger, LogCategory category, object? returnValue = null, [CallerMemberName] string methodName = "")
        {
            logger.LogMethodExit(category, methodName, returnValue);
        }

        /// <summary>
        /// Logs an operation with timing
        /// </summary>
        public static IDisposable LogOperation(this ILoggingService logger, LogCategory category, [CallerMemberName] string operationName = "")
        {
            return logger.BeginTimedOperation(category, operationName);
        }

        /// <summary>
        /// Convenient method to log with context (class name + method name)
        /// </summary>
        public static void LogWithContext(this ILoggingService logger, LogLevel level, LogCategory category, string message, [CallerMemberName] string methodName = "", [CallerFilePath] string filePath = "", params object[] args)
        {
            var className = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var contextMessage = $"[{className}.{methodName}] {message}";

            switch (level)
            {
                case LogLevel.Trace:
                    logger.LogTrace(category, contextMessage, args);
                    break;
                case LogLevel.Debug:
                    logger.LogDebug(category, contextMessage, args);
                    break;
                case LogLevel.Info:
                    logger.LogInfo(category, contextMessage, args);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning(category, contextMessage, args);
                    break;
                case LogLevel.Error:
                    logger.LogError(category, contextMessage, args);
                    break;
                case LogLevel.Critical:
                    logger.LogCritical(category, contextMessage, args);
                    break;
            }
        }

        /// <summary>
        /// Convenient debug logging with context
        /// </summary>
        public static void LogDebugContext(this ILoggingService logger, LogCategory category, string message, [CallerMemberName] string methodName = "", [CallerFilePath] string filePath = "", params object[] args)
        {
            logger.LogWithContext(LogLevel.Debug, category, message, methodName, filePath, args);
        }

        /// <summary>
        /// Convenient info logging with context
        /// </summary>
        public static void LogInfoContext(this ILoggingService logger, LogCategory category, string message, [CallerMemberName] string methodName = "", [CallerFilePath] string filePath = "", params object[] args)
        {
            logger.LogWithContext(LogLevel.Info, category, message, methodName, filePath, args);
        }

        /// <summary>
        /// Convenient error logging with context
        /// </summary>
        public static void LogErrorContext(this ILoggingService logger, LogCategory category, string message, Exception? exception = null, [CallerMemberName] string methodName = "", [CallerFilePath] string filePath = "", params object[] args)
        {
            var className = System.IO.Path.GetFileNameWithoutExtension(filePath);
            var contextMessage = $"[{className}.{methodName}] {message}";
            logger.LogError(category, contextMessage, exception, args);
        }
    }
}
