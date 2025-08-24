using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace GitMC.Services
{
    /// <summary>
    /// Represents a timed operation for performance measurement
    /// </summary>
    public class TimedOperation : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly LogCategory _category;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public TimedOperation(ILoggingService logger, LogCategory category, string operationName)
        {
            _logger = logger;
            _category = category;
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch.Stop();
                _logger.LogDebug(_category, "Operation '{Operation}' completed in {ElapsedMs}ms", _operationName, _stopwatch.ElapsedMilliseconds);
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// High-performance, structured logging service implementation
    /// </summary>
    public class LoggingService : ILoggingService
    {
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<LogCategory, bool> _enabledCategories;
        private readonly string? _logFilePath;
        private readonly bool _enableFileLogging;
        private readonly bool _enableConsoleLogging;
        private readonly ThreadLocal<StringBuilder> _stringBuilder;

        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

        public LoggingService(bool enableFileLogging = true, bool enableConsoleLogging = true, string? logFilePath = null)
        {
            _enableFileLogging = enableFileLogging;
            _enableConsoleLogging = enableConsoleLogging;
            
            var programRoot = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            _logFilePath = logFilePath ?? Path.Combine(programRoot, ".GitMC", "logs", $"gitmc_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            
            // Enable all categories by default
            _enabledCategories = new ConcurrentDictionary<LogCategory, bool>();
            foreach (LogCategory category in Enum.GetValues<LogCategory>())
            {
                _enabledCategories[category] = true;
            }

            // Thread-safe string builder for performance
            _stringBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(512));

            // Ensure log directory exists
            if (_enableFileLogging && _logFilePath != null)
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            // Log service initialization
            LogInfo(LogCategory.General, "LoggingService initialized. File logging: {FileLogging}, Console logging: {ConsoleLogging}, Log file: {LogFile}", 
                _enableFileLogging, _enableConsoleLogging, _logFilePath ?? "disabled");
        }

        public void LogTrace(LogCategory category, string message, params object[] args)
            => Log(LogLevel.Trace, category, message, null, args);

        public void LogTrace(LogCategory category, string message, Exception? exception = null, params object[] args)
            => Log(LogLevel.Trace, category, message, exception, args);

        public void LogDebug(LogCategory category, string message, params object[] args)
            => Log(LogLevel.Debug, category, message, null, args);

        public void LogDebug(LogCategory category, string message, Exception? exception = null, params object[] args)
            => Log(LogLevel.Debug, category, message, exception, args);

        public void LogInfo(LogCategory category, string message, params object[] args)
            => Log(LogLevel.Info, category, message, null, args);

        public void LogInfo(LogCategory category, string message, Exception? exception = null, params object[] args)
            => Log(LogLevel.Info, category, message, exception, args);

        public void LogWarning(LogCategory category, string message, params object[] args)
            => Log(LogLevel.Warning, category, message, null, args);

        public void LogWarning(LogCategory category, string message, Exception? exception = null, params object[] args)
            => Log(LogLevel.Warning, category, message, exception, args);

        public void LogError(LogCategory category, string message, params object[] args)
            => Log(LogLevel.Error, category, message, null, args);

        public void LogError(LogCategory category, string message, Exception? exception = null, params object[] args)
            => Log(LogLevel.Error, category, message, exception, args);

        public void LogCritical(LogCategory category, string message, params object[] args)
            => Log(LogLevel.Critical, category, message, null, args);

        public void LogCritical(LogCategory category, string message, Exception? exception = null, params object[] args)
            => Log(LogLevel.Critical, category, message, exception, args);

        public IDisposable BeginTimedOperation(LogCategory category, string operationName)
        {
            LogDebug(category, "Starting operation: {Operation}", operationName);
            return new TimedOperation(this, category, operationName);
        }

        public void LogMethodEntry(LogCategory category, string methodName, params object[] parameters)
        {
            if (!IsEnabled(LogLevel.Trace) || !IsCategoryEnabled(category))
                return;

            var sb = _stringBuilder.Value!;
            sb.Clear();
            sb.Append("→ ").Append(methodName).Append('(');
            
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FormatParameter(parameters[i]));
            }
            sb.Append(')');

            LogTrace(category, sb.ToString());
        }

        public void LogMethodExit(LogCategory category, string methodName, object? returnValue = null)
        {
            if (!IsEnabled(LogLevel.Trace) || !IsCategoryEnabled(category))
                return;

            var message = returnValue != null 
                ? $"← {methodName} → {FormatParameter(returnValue)}"
                : $"← {methodName}";

            LogTrace(category, message);
        }

        public void SetCategoryEnabled(LogCategory category, bool enabled)
        {
            _enabledCategories[category] = enabled;
            LogDebug(LogCategory.General, "Category {Category} {Status}", category, enabled ? "enabled" : "disabled");
        }

        public bool IsEnabled(LogLevel level) => level >= MinimumLogLevel;

        public bool IsCategoryEnabled(LogCategory category) 
            => _enabledCategories.TryGetValue(category, out var enabled) && enabled;

        private void Log(LogLevel level, LogCategory category, string message, Exception? exception, params object[] args)
        {
            // Early exit if logging is disabled for this level/category
            if (!IsEnabled(level) || !IsCategoryEnabled(category))
                return;

            try
            {
                var formattedMessage = args.Length > 0 ? FormatMessage(message, args) : message;
                var logEntry = CreateLogEntry(level, category, formattedMessage, exception);

                WriteLog(logEntry);
            }
            catch (Exception ex)
            {
                // Fallback to basic debug output if logging fails
                Debug.WriteLine($"[LOGGING_ERROR] Failed to log message: {ex.Message}");
                Debug.WriteLine($"[FALLBACK] [{level}] [{category}] {message}");
            }
        }

        private string FormatMessage(string template, object[] args)
        {
            try
            {
                // Simple replacement for structured logging placeholders
                var result = template;
                for (int i = 0; i < args.Length; i++)
                {
                    var placeholder = $"{{{i}}}";
                    if (result.Contains(placeholder))
                    {
                        result = result.Replace(placeholder, FormatParameter(args[i]));
                    }
                }

                // Handle named placeholders (simple implementation)
                var argIndex = 0;
                while (result.Contains('{') && argIndex < args.Length)
                {
                    var start = result.IndexOf('{');
                    var end = result.IndexOf('}', start);
                    if (start >= 0 && end > start)
                    {
                        result = result.Substring(0, start) + FormatParameter(args[argIndex]) + result.Substring(end + 1);
                        argIndex++;
                    }
                    else
                    {
                        break;
                    }
                }

                return result;
            }
            catch
            {
                // If formatting fails, return original template with args appended
                return $"{template} [Args: {string.Join(", ", args)}]";
            }
        }

        private string FormatParameter(object? parameter)
        {
            if (parameter == null) return "null";
            if (parameter is string s) return $"\"{s}\"";
            if (parameter is Exception ex) return $"{ex.GetType().Name}: {ex.Message}";
            if (parameter is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss.fff");
            if (parameter is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
            if (parameter is TimeSpan ts) return ts.ToString(@"hh\:mm\:ss\.fff");
            
            return parameter.ToString() ?? "null";
        }

        private string CreateLogEntry(LogLevel level, LogCategory category, string message, Exception? exception)
        {
            var sb = _stringBuilder.Value!;
            sb.Clear();

            // Timestamp
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(" [");

            // Thread ID
            sb.Append("T").Append(Thread.CurrentThread.ManagedThreadId.ToString().PadLeft(2, '0'));
            sb.Append("] ");

            // Log level
            sb.Append(GetLogLevelString(level).PadRight(5));
            sb.Append(" [");

            // Category
            sb.Append(category.ToString().PadRight(12));
            sb.Append("] ");

            // Message
            sb.Append(message);

            // Exception details
            if (exception != null)
            {
                sb.AppendLine();
                sb.Append("    Exception: ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    sb.AppendLine();
                    sb.Append("    StackTrace: ").Append(exception.StackTrace.Replace("\r\n", "\r\n    "));
                }
                if (exception.InnerException != null)
                {
                    sb.AppendLine();
                    sb.Append("    InnerException: ").Append(exception.InnerException.GetType().Name).Append(": ").Append(exception.InnerException.Message);
                }
            }

            return sb.ToString();
        }

        private static string GetLogLevelString(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "UNKN"
        };

        private void WriteLog(string logEntry)
        {
            lock (_lock)
            {
                // Write to debug output
                if (_enableConsoleLogging)
                {
                    Debug.WriteLine(logEntry);
                }

                // Write to file
                if (_enableFileLogging && !string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[LOGGING_ERROR] Failed to write to log file: {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            LogInfo(LogCategory.General, "LoggingService disposing");
            _stringBuilder?.Dispose();
        }
    }
}
