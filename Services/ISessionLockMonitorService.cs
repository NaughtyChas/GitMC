using System;

namespace GitMC.Services;

/// <summary>
/// Monitors Minecraft world session.lock usage state and raises events when a session ends.
/// A world is considered "in use" when session.lock cannot be opened exclusively.
/// When the state transitions from in-use to not-in-use and remains stable for a quiet period,
/// the monitor raises <see cref="SessionEnded"/>.
/// </summary>
public interface ISessionLockMonitorService
{
    /// <summary>
    /// Raised when a monitored world's session appears to have ended (lock released) and remained stable during a quiet period.
    /// </summary>
    event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <summary>
    /// Raised whenever the in-use state changes for a monitored save (true when a lock is detected, false when no lock).
    /// Useful for UI to show/hide warnings while the game is running.
    /// </summary>
    event EventHandler<SessionInUseChangedEventArgs>? SessionInUseChanged;

    /// <summary>
    /// Start monitoring a save directory.
    /// Safe to call multiple times for the same path.
    /// </summary>
    /// <param name="savePath">The path to the Minecraft save directory.</param>
    void StartMonitoring(string savePath);

    /// <summary>
    /// Stop monitoring a save directory.
    /// </summary>
    /// <param name="savePath">The path to the Minecraft save directory.</param>
    void StopMonitoring(string savePath);

    /// <summary>
    /// Try get the latest observed in-use state for a monitored save.
    /// Returns false when the path is not being monitored.
    /// </summary>
    bool TryGetInUse(string savePath, out bool inUse);
}

public sealed class SessionEndedEventArgs : EventArgs
{
    public SessionEndedEventArgs(string savePath, DateTimeOffset detectedAt)
    {
        SavePath = savePath;
        DetectedAt = detectedAt;
    }

    public string SavePath { get; }
    public DateTimeOffset DetectedAt { get; }
}

public sealed class SessionInUseChangedEventArgs : EventArgs
{
    public SessionInUseChangedEventArgs(string savePath, bool inUse, DateTimeOffset detectedAt)
    {
        SavePath = savePath;
        InUse = inUse;
        DetectedAt = detectedAt;
    }

    public string SavePath { get; }
    public bool InUse { get; }
    public DateTimeOffset DetectedAt { get; }
}
