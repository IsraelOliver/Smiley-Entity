namespace Smiley;

// A short history avoids basing a throw on one noisy event or the entire drag.
// Positions are the clamped character position, in physical screen pixels.
internal sealed class DragVelocityTracker
{
    internal const double SampleWindow = 0.12;
    private readonly Queue<(double Time, double X, double Y)> samples = new();
    private double lastMotion = double.NegativeInfinity;

    internal void Reset(double time, double x, double y)
    {
        samples.Clear(); lastMotion = double.NegativeInfinity; Add(time, x, y);
    }

    internal void Add(double time, double x, double y)
    {
        if (samples.Count > 0)
        {
            var last = samples.Last();
            if (time <= last.Time) return;
            if (x != last.X || y != last.Y) lastMotion = time;
        }
        samples.Enqueue((time, x, y));
        while (samples.Count > 1 && (samples.Peek().Time < time - SampleWindow || samples.Count > 128)) samples.Dequeue();
    }

    internal (double X, double Y) Release(double time, double x, double y)
    {
        Add(time, x, y);
        if (samples.Count < 2 || time - lastMotion >= SampleWindow) return (0, 0);
        var first = samples.Peek();
        double elapsed = time - first.Time;
        return elapsed < 0.012 ? (0, 0) : ((x - first.X) / elapsed, (y - first.Y) / elapsed);
    }
}
