using System.Windows.Media;
using System.Windows.Threading;
using ZeiLand.Services;

namespace ZeiLand.Animation;

/// <summary>
/// Moves the compact view's level bars with what is actually playing: reads the output peak level
/// about 30 times a second, and only while the bars are on screen. Automatic gain makes quiet and loud
/// tracks equally lively.
/// </summary>
internal sealed class LevelMeter
{
    private const double Floor = 0.18;          // bar height in silence
    private const double CeilingDecay = 0.992;  // the auto-gain forgets a loud peak in ~4 s
    private const double MinCeiling = 0.02;     // anything quieter is silence, not quiet music
    private const double Attack = 0.65;         // share of the way to a higher target covered per frame
    private const double Release = 0.25;        // ... and to a lower one: bars jump up, fall back slowly

    private readonly ScaleTransform[] _bars;
    private readonly double[] _rest;
    private readonly VolumeService _volume;
    private readonly DispatcherTimer _timer;
    private readonly float[] _peaks = new float[2];
    private readonly double[] _levels;
    private readonly Random _random = new();
    private double _ceiling;
    private double _previousMid;

    public LevelMeter(ScaleTransform[] bars, double[] rest, VolumeService volume)
    {
        _bars = bars;
        _rest = rest;
        _volume = volume;
        _levels = new double[bars.Length];
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Step();
        Stop();
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _ceiling = MinCeiling;
        _volume.ReadPeaks(_peaks); // Drop the peak gathered while the bars were hidden.
        _timer.Start();
    }

    /// <summary>Stops reading and leaves the bars at their resting shape.</summary>
    public void Stop()
    {
        _timer.Stop();
        for (int i = 0; i < _bars.Length; i++)
        {
            _levels[i] = _rest[i];
            _bars[i].ScaleY = _rest[i];
        }
    }

    private void Step()
    {
        if (!_volume.ReadPeaks(_peaks)) _peaks[0] = _peaks[1] = 0;
        double left = _peaks[0], right = _peaks[1], mid = (left + right) / 2;
        _ceiling = Math.Max(Math.Max(left, right), Math.Max(_ceiling * CeilingDecay, MinCeiling));

        // Four bars from two channels: left, centre, right, and the centre one frame ago (a slight ripple).
        MoveBar(0, left);
        MoveBar(1, mid);
        MoveBar(2, right);
        MoveBar(3, _previousMid);
        _previousMid = mid;
    }

    private void MoveBar(int bar, double peak)
    {
        double loudness = Math.Clamp(peak / _ceiling, 0, 1);
        // Squaring stretches the loud end so beats stand out from the sustained level; the random factor
        // keeps bars fed by near-identical signals (most music is close to mono) from moving in lockstep.
        double target = Floor + (1 - Floor) * loudness * loudness * (0.7 + 0.3 * _random.NextDouble());
        double current = _levels[bar];
        double next = current + (target - current) * (target > current ? Attack : Release);
        if (Math.Abs(next - current) < 0.002) return; // Settled (e.g. silence): nothing to redraw.
        _levels[bar] = next;
        _bars[bar].ScaleY = next;
    }
}
