using System.Collections.Immutable;
using System.Windows.Threading;

namespace Smiley;

// All coordinates are PHYSICAL desktop pixels, never WPF DIPs.
public readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    public long Width => (long)Right - Left;
    public long Height => (long)Bottom - Top;
    public bool IsValid => Width > 0 && Height > 0;
}
public enum WindowVisibility { Visible, Hidden, Minimized, Cloaked, Unavailable }
public readonly record struct WindowGeometry(nint Handle, WindowBounds Bounds, WindowVisibility Visibility, bool IsMaximized, int ZOrder)
{
    public int TopEdge => Bounds.Top;
    public int EdgeLeft => Bounds.Left;
    public int EdgeRight => Bounds.Right;
    public bool IsVisible => Visibility == WindowVisibility.Visible && Bounds.IsValid;
}
[Flags]
public enum WindowChangeKind
{
    None = 0, Appeared = 1, Disappeared = 2, Moved = 4, Resized = 8, Minimized = 16,
    Restored = 32, VisibilityChanged = 64, ZOrderChanged = 128, MaximizedChanged = 256
}
public readonly record struct WindowChange(nint Handle, WindowChangeKind Kind, WindowGeometry? Before, WindowGeometry? After);
public sealed record WindowMapSnapshot(long Version, DateTimeOffset CapturedAtUtc,
    ImmutableArray<WindowGeometry> Windows, ImmutableArray<WindowChange> Changes)
{
    public static WindowMapSnapshot Empty { get; } = new(0, DateTimeOffset.MinValue, [], []);
}

internal readonly record struct WindowObservation(WindowGeometry Geometry, uint ProcessId, uint ThreadId);
internal interface IWindowGeometrySource
{
    bool TryCapture(List<WindowObservation> destination);
}
internal interface ISingleWindowGeometrySource
{
    bool TryCaptureWindow(nint handle, out WindowObservation observation);
}

// An independent UI-dispatcher service: never driven by rendering or the behaviour clock.
public sealed class WindowTracker : IDisposable
{
    private readonly IWindowGeometrySource source;
    private readonly DispatcherTimer timer;
    private readonly List<WindowObservation> capture = new();
    private readonly List<WindowGeometry> visible = new();
    private readonly List<WindowChange> changes = new();
    private Dictionary<nint, WindowObservation> previous = new(), next = new();
    private WindowMapSnapshot current = WindowMapSnapshot.Empty;
    private bool disposed, polling;
    public TimeSpan Interval => timer.Interval;
    public bool IsRunning => timer.IsEnabled;
    public bool LastPollSucceeded { get; private set; }
    public DateTimeOffset LastPollAtUtc { get; private set; }
    public WindowMapSnapshot Current => Volatile.Read(ref current);
    public event EventHandler? SnapshotChanged;

    public WindowTracker(nint smileyHandle, TimeSpan? interval = null)
        : this(new NativeWindowGeometrySource(smileyHandle), interval) { }

    internal WindowTracker(IWindowGeometrySource source, TimeSpan? interval = null)
    {
        this.source = source;
        var period = interval ?? TimeSpan.FromMilliseconds(200);
        if (period < TimeSpan.FromMilliseconds(50) || period > TimeSpan.FromSeconds(10))
            throw new ArgumentOutOfRangeException(nameof(interval));
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = period };
        timer.Tick += OnTimer;
    }

    public void Start()
    {
        timer.Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (timer.IsEnabled) return;
        PollOnce(); timer.Start();
    }
    public void Stop() { timer.Dispatcher.VerifyAccess(); timer.Stop(); }
    // Targeted read only: never enumerates windows or changes the 200 ms snapshot.
    internal bool TryRefreshWindow(nint handle, out WindowGeometry geometry)
    {
        timer.Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        geometry = default;
        if (!previous.TryGetValue(handle, out var known) || source is not ISingleWindowGeometrySource single ||
            !single.TryCaptureWindow(handle, out var read) || read.Geometry.Handle != handle ||
            read.ProcessId != known.ProcessId || read.ThreadId != known.ThreadId) return false;
        geometry = read.Geometry with { ZOrder = known.Geometry.ZOrder };
        return true;
    }
    private void OnTimer(object? sender, EventArgs e) => PollOnce();

    public void PollOnce()
    {
        timer.Dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this);
        if (polling) return;
        polling = true;
        try
        {
            LastPollAtUtc = DateTimeOffset.UtcNow;
            capture.Clear();
            LastPollSucceeded = source.TryCapture(capture);
            if (!LastPollSucceeded) return; // Never replace a complete snapshot with a failed/partial poll.
            next.Clear(); visible.Clear(); changes.Clear();
            foreach (var observation in capture)
            {
                var geometry = observation.Geometry;
                if (geometry.Handle == 0 || next.ContainsKey(geometry.Handle)) continue;
                if (geometry.Visibility == WindowVisibility.Visible && !geometry.Bounds.IsValid)
                    geometry = geometry with { Visibility = WindowVisibility.Unavailable };
                geometry = geometry with { ZOrder = geometry.IsVisible ? visible.Count : -1 };
                var normalized = observation with { Geometry = geometry };
                next.Add(geometry.Handle, normalized);
                if (geometry.IsVisible) visible.Add(geometry);
                if (!previous.TryGetValue(geometry.Handle, out var old))
                    changes.Add(new(geometry.Handle, WindowChangeKind.Appeared, null, geometry));
                else if (old.ProcessId != normalized.ProcessId || old.ThreadId != normalized.ThreadId)
                {
                    // A recycled HWND with a different native identity is a replacement.
                    changes.Add(new(geometry.Handle, WindowChangeKind.Disappeared, old.Geometry, null));
                    changes.Add(new(geometry.Handle, WindowChangeKind.Appeared, null, geometry));
                }
                else
                {
                    var kind = Compare(old.Geometry, geometry);
                    if (kind != WindowChangeKind.None) changes.Add(new(geometry.Handle, kind, old.Geometry, geometry));
                }
            }
            foreach (var old in previous)
                if (!next.ContainsKey(old.Key)) changes.Add(new(old.Key, WindowChangeKind.Disappeared, old.Value.Geometry, null));
            (previous, next) = (next, previous);
            next.Clear();
            if (changes.Count == 0) return; // Reuse both snapshot and immutable arrays when nothing changed.
            var snapshot = new WindowMapSnapshot(current.Version + 1, LastPollAtUtc, visible.ToImmutableArray(), changes.ToImmutableArray());
            Volatile.Write(ref current, snapshot);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { polling = false; }
    }

    private static WindowChangeKind Compare(WindowGeometry before, WindowGeometry after)
    {
        var kind = WindowChangeKind.None;
        if (before.Visibility != after.Visibility)
        {
            kind |= WindowChangeKind.VisibilityChanged;
            if (after.Visibility == WindowVisibility.Minimized) kind |= WindowChangeKind.Minimized;
            if (before.Visibility == WindowVisibility.Minimized && after.Visibility != WindowVisibility.Minimized) kind |= WindowChangeKind.Restored;
        }
        if (before.IsVisible && after.IsVisible)
        {
            if (before.Bounds.Left != after.Bounds.Left || before.Bounds.Top != after.Bounds.Top) kind |= WindowChangeKind.Moved;
            if (before.Bounds.Width != after.Bounds.Width || before.Bounds.Height != after.Bounds.Height) kind |= WindowChangeKind.Resized;
            if (before.ZOrder != after.ZOrder) kind |= WindowChangeKind.ZOrderChanged;
        }
        if (before.IsMaximized != after.IsMaximized) kind |= WindowChangeKind.MaximizedChanged;
        return kind;
    }

    public void Dispose()
    {
        timer.Dispatcher.VerifyAccess();
        if (disposed) return;
        timer.Stop(); timer.Tick -= OnTimer;
        previous.Clear(); next.Clear(); capture.Clear(); visible.Clear(); changes.Clear();
        if (source is IDisposable resource) resource.Dispose();
        SnapshotChanged = null; Volatile.Write(ref current, WindowMapSnapshot.Empty);
        disposed = true;
    }
}
