using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GitMC.Services;

/// <summary>
/// Default implementation that probes session.lock with exclusive open to detect in-use state (Minecraft 1.17+ semantics).
/// It debounces the transition from in-use to idle with a quiet period to avoid false positives.
/// </summary>
public sealed class SessionLockMonitorService : ISessionLockMonitorService, IDisposable
{
    private readonly ConcurrentDictionary<string, MonitorState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _quietPeriod = TimeSpan.FromSeconds(3);
    private Timer? _timer;

    public event EventHandler<SessionEndedEventArgs>? SessionEnded;
    public event EventHandler<SessionInUseChangedEventArgs>? SessionInUseChanged;

    public void StartMonitoring(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath)) return;
        _states.AddOrUpdate(savePath, _ => new MonitorState(), (_, existing) => existing);
        EnsureTimer();
    }

    public void StopMonitoring(string savePath)
    {
        _states.TryRemove(savePath, out _);
        TryStopTimer();
    }

    public bool TryGetInUse(string savePath, out bool inUse)
    {
        if (_states.TryGetValue(savePath, out var s))
        {
            inUse = s.InUse;
            return true;
        }
        inUse = false;
        return false;
    }

    private void EnsureTimer()
    {
        if (_timer != null) return;
        _timer = new Timer(async _ => await TickAsync().ConfigureAwait(false), null, _pollInterval, _pollInterval);
    }

    private void TryStopTimer()
    {
        if (_states.IsEmpty && _timer is { } t)
        {
            t.Dispose();
            _timer = null;
        }
    }

    private async Task TickAsync()
    {
        foreach (var kvp in _states.ToArray())
        {
            var savePath = kvp.Key;
            var state = kvp.Value;
            try
            {
                var inUse = await IsSaveInUseAsync(savePath).ConfigureAwait(false);
                var now = DateTimeOffset.UtcNow;

                if (inUse)
                {
                    if (!state.InUse)
                    {
                        state.InUse = true;
                        SessionInUseChanged?.Invoke(this, new SessionInUseChangedEventArgs(savePath, true, now));
                    }
                    state.LastInUse = now;
                    state.LastIdleCandidate = null;
                }
                else
                {
                    if (state.InUse)
                    {
                        // transition from in-use to idle candidate
                        state.InUse = false;
                        SessionInUseChanged?.Invoke(this, new SessionInUseChangedEventArgs(savePath, false, now));
                        state.LastIdleCandidate = now;
                    }
                    else if (state.LastIdleCandidate is { } firstIdle && now - firstIdle >= _quietPeriod)
                    {
                        // stable idle since quiet period passed – fire event once
                        state.LastIdleCandidate = null;
                        SessionEnded?.Invoke(this, new SessionEndedEventArgs(savePath, now));
                    }
                }
            }
            catch
            {
                // ignore per-path errors
            }
        }
    }

    private static async Task<bool> IsSaveInUseAsync(string savePath)
    {
        try
        {
            var sessionLock = Path.Combine(savePath, "session.lock");
            if (!File.Exists(sessionLock))
            {
                // Session not in use if lock file absent; Minecraft writes/locks it on open (1.17+)
                await Task.CompletedTask;
                return false;
            }
            if (IsFileLocked(sessionLock)) return true;
        }
        catch { }

        await Task.CompletedTask;
        return false;
    }

    private static bool IsFileLocked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _states.Clear();
    }

    private sealed class MonitorState
    {
        public bool InUse { get; set; }
        public DateTimeOffset LastInUse { get; set; }
        public DateTimeOffset? LastIdleCandidate { get; set; }
    }
}
