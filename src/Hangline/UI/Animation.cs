using System.Windows.Media;

namespace Hangline.UI;

/// One animated number, driven once per frame. It either springs towards a
/// target, with the same parameters SwiftUI takes, or tweens over a fixed
/// time. This is what gives the photos their swing.
internal sealed class Anim
{
    public double Value { get; private set; }
    public double Target { get; private set; }
    private double velocity;
    private double stiffness = 100, damping = 20;
    private bool tweening;
    private double tweenFrom, tweenStart, tweenDuration;
    private Func<double, double> ease = t => t;
    private Action? then;
    private readonly Action<double> apply;

    public Anim(double value, Action<double> apply)
    {
        Value = Target = value;
        this.apply = apply;
        apply(value);
    }

    public bool Running { get; private set; }

    /// SwiftUI's spring(response:dampingFraction:), for a unit mass.
    public void Spring(double to, double response, double dampingFraction, Action? then = null)
    {
        var w = 2 * Math.PI / response;
        Interpolating(to, w * w, 2 * dampingFraction * w, then);
    }

    /// SwiftUI's interpolatingSpring(stiffness:damping:), for a unit mass.
    public void Interpolating(double to, double stiffness, double damping, Action? then = null)
    {
        this.stiffness = stiffness;
        this.damping = damping;
        Target = to;
        tweening = false;
        this.then = then;
        Start();
    }

    public void Tween(double to, double seconds, Func<double, double>? ease = null, Action? then = null)
    {
        if (seconds <= 0) { Set(to); then?.Invoke(); return; }
        tweenFrom = Value;
        Target = to;
        tweenDuration = seconds;
        tweenStart = Animator.Now;
        this.ease = ease ?? Ease.Out;
        tweening = true;
        velocity = 0;
        this.then = then;
        Start();
    }

    /// Jumps there, no animation.
    public void Set(double to)
    {
        Value = Target = to;
        velocity = 0;
        Stop();
        apply(to);
    }

    /// Starts from here, then animates: used to give a photo a swing.
    public void Kick(double from)
    {
        Value = from;
        apply(from);
    }

    private void Start()
    {
        if (Running) return;
        Running = true;
        Animator.Add(this);
    }

    private void Stop()
    {
        if (!Running) return;
        Running = false;
        Animator.Remove(this);
    }

    /// Advances by dt seconds. Returns false once it has come to rest.
    internal bool Step(double dt)
    {
        if (tweening)
        {
            var k = Math.Min(1, (Animator.Now - tweenStart) / tweenDuration);
            Value = tweenFrom + (Target - tweenFrom) * ease(k);
            apply(Value);
            if (k < 1) return true;
        }
        else
        {
            // Small fixed steps keep a stiff, lightly damped spring stable.
            const double h = 1.0 / 480;
            for (var t = 0.0; t < dt; t += h)
            {
                var step = Math.Min(h, dt - t);
                var a = -stiffness * (Value - Target) - damping * velocity;
                velocity += a * step;
                Value += velocity * step;
            }
            var settled = Math.Abs(velocity) < 0.002 && Math.Abs(Value - Target) < 0.002;
            if (!settled) { apply(Value); return true; }
            Value = Target;
            velocity = 0;
            apply(Value);
        }
        Running = false;
        var next = then;
        then = null;
        next?.Invoke();
        return Running;
    }
}

internal static class Ease
{
    public static double Out(double t) => 1 - (1 - t) * (1 - t);
    public static double In(double t) => t * t;
    public static double InOut(double t) => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    public static double InOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    public static double Smooth(double x, double a, double b)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}

/// Runs every live animation once per rendered frame.
internal static class Animator
{
    private static readonly List<Anim> Active = new();
    private static readonly List<Action<double>> Frames = new();
    private static bool hooked;
    private static TimeSpan last;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    public static double Now => Clock.Elapsed.TotalSeconds;

    public static void Add(Anim a)
    {
        if (!Active.Contains(a)) Active.Add(a);
        Hook();
    }

    public static void Remove(Anim a) => Active.Remove(a);

    /// A callback for every frame until it is removed, for flights.
    public static void AddFrame(Action<double> f)
    {
        Frames.Add(f);
        Hook();
    }

    public static void RemoveFrame(Action<double> f) => Frames.Remove(f);

    private static void Hook()
    {
        if (hooked) return;
        hooked = true;
        last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == last) return;
        var dt = last == TimeSpan.Zero ? 1.0 / 60 : Math.Min(1.0 / 20, (time - last).TotalSeconds);
        last = time;
        foreach (var a in Active.ToArray())
            if (!a.Step(dt)) Active.Remove(a);
        foreach (var f in Frames.ToArray()) f(dt);
        if (Active.Count == 0 && Frames.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            hooked = false;
        }
    }
}
