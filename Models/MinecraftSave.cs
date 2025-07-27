using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitMC.Models
{
    public class MinecraftSave : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _path = string.Empty;
        private string _worldType = string.Empty;
        private DateTime _lastPlayed;
        private DateTime _lastCommit;
        private long _worldSize;
        private string _gitStatus = "Not initialized";
        private int _pendingChanges;
        private bool _isGitInitialized;
        private string _gameVersion = string.Empty;
        private string _worldIcon = "🌍";

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Path
        {
            get => _path;
            set => SetProperty(ref _path, value);
        }

        public string WorldType
        {
            get => _worldType;
            set => SetProperty(ref _worldType, value);
        }

        public DateTime LastPlayed
        {
            get => _lastPlayed;
            set => SetProperty(ref _lastPlayed, value);
        }

        public DateTime LastCommit
        {
            get => _lastCommit;
            set => SetProperty(ref _lastCommit, value);
        }

        public long WorldSize
        {
            get => _worldSize;
            set => SetProperty(ref _worldSize, value);
        }

        public string WorldSizeFormatted => FormatFileSize(_worldSize);

        public string GitStatus
        {
            get => _gitStatus;
            set => SetProperty(ref _gitStatus, value);
        }

        public int PendingChanges
        {
            get => _pendingChanges;
            set => SetProperty(ref _pendingChanges, value);
        }

        public bool IsGitInitialized
        {
            get => _isGitInitialized;
            set => SetProperty(ref _isGitInitialized, value);
        }

        public string GameVersion
        {
            get => _gameVersion;
            set => SetProperty(ref _gameVersion, value);
        }

        public string WorldIcon
        {
            get => _worldIcon;
            set => SetProperty(ref _worldIcon, value);
        }

        public string LastPlayedFormatted => _lastPlayed == DateTime.MinValue ? "Never" : FormatRelativeTime(_lastPlayed);
        public string LastCommitFormatted => _lastCommit == DateTime.MinValue ? "No commits" : FormatRelativeTime(_lastCommit);

        private static string FormatFileSize(long bytes)
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

        private static string FormatRelativeTime(DateTime dateTime)
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
