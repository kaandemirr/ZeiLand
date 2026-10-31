using System.Windows.Media;

namespace ZeiLand.Animation;

/// <summary>
/// Damped spring: the "Apple feel" comes from animating with physics instead of fixed-duration easing,
/// so retargeting mid-flight keeps the current velocity.
/// </summary>
internal sealed class Spring(double stiffness, double damping)
{
    public double Value;
    public double Velocity;
    public double Target;

    public bool IsSettled => Math.Abs(Value - Target) < 0.05 && Math.Abs(Velocity) < 0.5;

    public void Step(double dt)
    {
        double acceleration = -stiffness * (Value - Target) - damping * Velocity;
        Velocity += acceleration * dt;
        Value += Velocity * dt;
    }

    public void Snap()
    {
        Value = Target;
        Velocity = 0;
    }
}

/// <summary>
/// Drives a group of springs from the WPF render loop. It is subscribed to
/// <see cref="CompositionTarget.Rendering"/> only while something is moving,
/// so a resting island renders nothing and costs no CPU.
/// </summary>
internal sealed class SpringAnimator
{
    private const double SubStep = 1.0 / 240;
    private const double MaxFrame = 1.0 / 30;

    private readonly Spring[] _springs;
    private readonly double[] _values;
    private readonly Action<double[]> _apply;
    private TimeSpan _lastFrame;
    private bool _running;

    public SpringAnimator(int count, Action<double[]> apply, double stiffness, double damping)
    {
        _springs = new Spring[count];
        for (int i = 0; i < count; i++) _springs[i] = new Spring(stiffness, damping);
        _values = new double[count];
        _apply = apply;
    }

    public void JumpTo(params double[] targets)
    {
        for (int i = 0; i < _springs.Length; i++)
        {
            _springs[i].Target = targets[i];
            _springs[i].Snap();
        }
        Publish();
    }

    public void AnimateTo(params double[] targets)
    {
        for (int i = 0; i < _springs.Length; i++) _springs[i].Target = targets[i];
        if (_running) return;

        _running = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return; // Rendering can fire more than once per frame.

        double dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : Math.Min((now - _lastFrame).TotalSeconds, MaxFrame);
        _lastFrame = now;

        // Fixed sub-steps keep the integration stable regardless of frame rate.
        for (double remaining = dt; remaining > 1e-9; remaining -= SubStep)
        {
            double h = Math.Min(SubStep, remaining);
            foreach (var spring in _springs) spring.Step(h);
        }

        bool settled = true;
        foreach (var spring in _springs) settled &= spring.IsSettled;
        if (settled)
        {
            foreach (var spring in _springs) spring.Snap();
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }

        Publish();
    }

    private void Publish()
    {
        for (int i = 0; i < _springs.Length; i++) _values[i] = _springs[i].Value;
        _apply(_values);
    }
}
