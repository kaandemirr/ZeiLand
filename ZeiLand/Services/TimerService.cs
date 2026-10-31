using System.Diagnostics;
using System.Windows.Threading;

namespace ZeiLand.Services;

internal enum TimerMode
{
    None,
    Countdown,
    Stopwatch,
}

/// <summary>
/// One countdown or stopwatch at a time, measured on the monotonic clock. Nothing ticks here: a single
/// one-shot timer fires when a countdown ends, and the island redraws the time only while it's on screen.
/// </summary>
internal sealed class TimerService
{
    private readonly DispatcherTimer _finish = new();
    private long _resumedAt;
    private TimeSpan _beforeResume;

    public TimerService()
    {
        _finish.Tick += (_, _) =>
        {
            _finish.Stop();
            if (Mode != TimerMode.Countdown || Remaining > TimeSpan.FromMilliseconds(50))
            {
                Schedule(); // Woke up early (e.g. after sleep); wait for the rest.
                return;
            }
            Mode = TimerMode.None;
            Finished?.Invoke(); // First, so the island goes straight to "time's up" without a detour.
            Changed?.Invoke();
        };
    }

    /// <summary>Started, paused, resumed, extended or stopped.</summary>
    public event Action? Changed;

    /// <summary>A countdown reached zero.</summary>
    public event Action? Finished;

    public TimerMode Mode { get; private set; }
    public bool IsActive => Mode != TimerMode.None;
    public bool IsPaused { get; private set; }

    /// <summary>Countdown length (grows with <see cref="Add"/>).</summary>
    public TimeSpan Duration { get; private set; }

    public TimeSpan Elapsed => _beforeResume + (IsPaused ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_resumedAt));

    public TimeSpan Remaining => Duration - Elapsed is var left && left > TimeSpan.Zero ? left : TimeSpan.Zero;

    /// <summary>
    /// What the island shows, in whole seconds: time left for a countdown (rounded up, so it starts at its
    /// full length and shows 0:00 only as it ends), time so far for a stopwatch.
    /// </summary>
    public TimeSpan Display => Mode == TimerMode.Countdown
        ? TimeSpan.FromSeconds(Math.Ceiling(Remaining.TotalSeconds))
        : TimeSpan.FromSeconds(Math.Floor(Elapsed.TotalSeconds));

    /// <summary>How long until <see cref="Display"/> changes, so the island redraws once per second, on time.</summary>
    public TimeSpan UntilNextChange
    {
        get
        {
            double seconds = Mode == TimerMode.Countdown ? Remaining.TotalSeconds : Elapsed.TotalSeconds;
            double fraction = seconds - Math.Floor(seconds);
            double wait = Mode == TimerMode.Countdown ? fraction : 1 - fraction;
            return TimeSpan.FromSeconds(wait + 0.02); // just past the boundary
        }
    }

    public void StartCountdown(TimeSpan duration) => Start(TimerMode.Countdown, duration);

    public void StartStopwatch() => Start(TimerMode.Stopwatch, TimeSpan.Zero);

    public void TogglePause()
    {
        if (!IsActive) return;
        if (IsPaused)
        {
            _resumedAt = Stopwatch.GetTimestamp();
            IsPaused = false;
        }
        else
        {
            _beforeResume = Elapsed;
            IsPaused = true;
        }
        Schedule();
        Changed?.Invoke();
    }

    public void Add(TimeSpan time)
    {
        if (Mode != TimerMode.Countdown) return;
        Duration += time;
        Schedule();
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (!IsActive) return;
        Mode = TimerMode.None;
        _finish.Stop();
        Changed?.Invoke();
    }

    private void Start(TimerMode mode, TimeSpan duration)
    {
        Mode = mode;
        Duration = duration;
        IsPaused = false;
        _beforeResume = TimeSpan.Zero;
        _resumedAt = Stopwatch.GetTimestamp();
        Schedule();
        Changed?.Invoke();
    }

    private void Schedule()
    {
        _finish.Stop();
        if (Mode != TimerMode.Countdown || IsPaused) return;
        _finish.Interval = Remaining > TimeSpan.FromMilliseconds(1) ? Remaining : TimeSpan.FromMilliseconds(1);
        _finish.Start();
    }

    /// <summary>"4:05", "1:02:03".</summary>
    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
