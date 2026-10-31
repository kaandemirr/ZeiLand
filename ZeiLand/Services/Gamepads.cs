using System.Windows.Threading;
using static ZeiLand.Native.NativeMethods;

namespace ZeiLand.Services;

/// <summary>
/// Notices game-controller use, which Windows doesn't count as input, so the AOD doesn't come up over a game
/// played with a controller. While <see cref="Watch"/>ed, samples Xbox-compatible controllers every two
/// seconds, and only while one is plugged in; otherwise there's nothing to do.
/// </summary>
internal sealed class Gamepads
{
    private const int Slots = 4;
    private const int StickRest = 7849; // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
    private const int TriggerRest = 30; // XINPUT_GAMEPAD_TRIGGER_THRESHOLD
    private const int StickMove = 3000; // more than a resting stick's jitter

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
    private readonly XINPUT_GAMEPAD?[] _last = new XINPUT_GAMEPAD?[Slots];
    private bool _sampled;
    private bool _available = true;

    public Gamepads() => _timer.Tick += (_, _) =>
    {
        Sample();
        if (_last.All(pad => pad is null)) _timer.Stop(); // unplugged
    };

    /// <summary>When a controller was last used (<see cref="Environment.TickCount64"/>), or null.</summary>
    public long? LastUsed { get; private set; }

    /// <summary>Samples now, and keeps sampling while a controller is plugged in.</summary>
    public void Watch()
    {
        Sample();
        if (_last.Any(pad => pad is not null)) _timer.Start();
        else _timer.Stop();
    }

    public void Stop() => _timer.Stop();

    /// <summary>True if a controller was used since the last sample (one plugged in since counts).</summary>
    public bool Sample()
    {
        if (!_available) return false;
        bool used = false;
        try
        {
            for (int slot = 0; slot < Slots; slot++)
            {
                XINPUT_GAMEPAD? pad = XInputGetState(slot, out var state) == 0 ? state.Gamepad : null;
                if (pad is { } now && (!Resting(now) || (_last[slot] is { } before ? Moved(before, now) : _sampled))) used = true;
                _last[slot] = pad;
            }
        }
        catch (DllNotFoundException)
        {
            _available = false; // no XInput on this system
        }
        _sampled = true;
        if (used) LastUsed = Environment.TickCount64;
        return used;
    }

    private static bool Resting(XINPUT_GAMEPAD pad) => pad.wButtons == 0
        && pad.bLeftTrigger <= TriggerRest && pad.bRightTrigger <= TriggerRest
        && Math.Abs((int)pad.sThumbLX) <= StickRest && Math.Abs((int)pad.sThumbLY) <= StickRest
        && Math.Abs((int)pad.sThumbRX) <= StickRest && Math.Abs((int)pad.sThumbRY) <= StickRest;

    private static bool Moved(XINPUT_GAMEPAD a, XINPUT_GAMEPAD b) => a.wButtons != b.wButtons
        || Math.Abs(a.bLeftTrigger - b.bLeftTrigger) > TriggerRest || Math.Abs(a.bRightTrigger - b.bRightTrigger) > TriggerRest
        || Math.Abs(a.sThumbLX - b.sThumbLX) > StickMove || Math.Abs(a.sThumbLY - b.sThumbLY) > StickMove
        || Math.Abs(a.sThumbRX - b.sThumbRX) > StickMove || Math.Abs(a.sThumbRY - b.sThumbRY) > StickMove;
}
