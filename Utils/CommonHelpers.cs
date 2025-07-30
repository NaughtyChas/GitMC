using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitMC.Utils
{
    /// <summary>
    /// Common utility methods shared across the application
    /// </summary>
    public static class CommonHelpers
    {
        /// <summary>
        /// Formats file size from bytes to human-readable format
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Formats relative time from DateTime to human-readable format
        /// </summary>
        public static string FormatRelativeTime(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;
            return timeSpan.TotalDays switch
            {
                < 1 when timeSpan.TotalHours < 1 => $"{(int)timeSpan.TotalMinutes}m ago",
                < 1 => $"{(int)timeSpan.TotalHours}h ago",
                < 7 => $"{(int)timeSpan.TotalDays}d ago",
                < 30 => $"{(int)(timeSpan.TotalDays / 7)}w ago",
                _ => dateTime.ToString("MMM dd, yyyy")
            };
        }

        /// <summary>
        /// Calculates folder size recursively
        /// </summary>
        public static long CalculateFolderSize(DirectoryInfo directoryInfo)
        {
            try
            {
                return directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(file => file.Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Generates a welcome message based on time of day
        /// </summary>
        public static string GetWelcomeMessage()
        {
            var hour = DateTime.Now.Hour;
            return hour switch
            {
                < 12 => "Good morning, Crafter! ☀️",
                < 18 => "Good afternoon, Miner! ⛏️",
                _ => "Good evening, Builder! 🌙"
            };
        }

        /// <summary>
        /// Determines world icon based on world type
        /// </summary>
        public static string GetWorldIcon(string worldType)
        {
            return worldType.ToLower() switch
            {
                "creative" => "🎨",
                "hardcore" => "💀",
                "spectator" => "👻",
                "adventure" => "🗺️",
                _ => "🌍"
            };
        }
    }

    /// <summary>
    /// Base class for ViewModels implementing INotifyPropertyChanged
    /// </summary>
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, newValue))
                return false;

            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
