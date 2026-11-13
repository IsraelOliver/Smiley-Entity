using System.Collections.Immutable;

namespace Smiley;

// SourceIndex refers directly into Source.Windows; original window geometry is not
// copied into every exposed fragment. Endpoints and heights are physical pixels.
public readonly record struct WindowPlatform(nint Handle, int SourceIndex, long SnapshotVersion, int Left, int Right, int Y)
{
    public long Width => (long)Right - Left;
    public bool IsAvailable => Width > 0;
}
public sealed record WindowPlatformMap(WindowMapSnapshot Source, ImmutableArray<WindowPlatform> Platforms, bool IsAvailable)
{
    public static WindowPlatformMap Unavailable { get; } = new(WindowMapSnapshot.Empty, [], false);
    public WindowGeometry GetSource(WindowPlatform platform) => Source.Windows[platform.SourceIndex];
}

public sealed class WindowPlatformProvider
{
    private readonly List<(int Left, int Right)> blockers = new();
    private readonly List<WindowPlatform> segments = new();
    private WindowPlatformMap cached = WindowPlatformMap.Unavailable;
    private DateTimeOffset lastGoodPoll;
    private double lastGoodTime, cachedMinimum;
    private bool hasGoodPoll;
    private WindowPlatformMap? supportBase, supportMap;
    private WindowGeometry supportGeometry;
    private double supportMinimum;
    public double MaximumStaleSeconds { get; }
    public WindowPlatformMap Current { get; private set; } = WindowPlatformMap.Unavailable;

    public WindowPlatformProvider(double maximumStaleSeconds = 1)
    {
        if (!double.IsFinite(maximumStaleSeconds) || maximumStaleSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(maximumStaleSeconds));
        MaximumStaleSeconds = maximumStaleSeconds;
    }

    // elapsedSeconds is monotonic (the existing Stopwatch), not wall-clock age.
    // LastPollAtUtc identifies NEW successful heartbeat polls, even if geometry is unchanged.
    public WindowPlatformMap Update(WindowMapSnapshot snapshot, bool pollSucceeded, DateTimeOffset pollAtUtc,
        double elapsedSeconds, double minimumSegmentWidth = 24)
    {
        if (!double.IsFinite(elapsedSeconds) || !double.IsFinite(minimumSegmentWidth) || minimumSegmentWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (pollSucceeded && pollAtUtc != DateTimeOffset.MinValue &&
            (!hasGoodPoll || pollAtUtc != lastGoodPoll || !ReferenceEquals(snapshot, cached.Source)))
        {
            lastGoodPoll = pollAtUtc; lastGoodTime = elapsedSeconds; hasGoodPoll = true;
            if (!ReferenceEquals(snapshot, cached.Source) || minimumSegmentWidth != cachedMinimum)
            { cached = Build(snapshot, minimumSegmentWidth); cachedMinimum = minimumSegmentWidth; }
        }
        else if (hasGoodPoll && minimumSegmentWidth != cachedMinimum)
        { cached = Build(cached.Source, minimumSegmentWidth); cachedMinimum = minimumSegmentWidth; }
        Current = hasGoodPoll && elapsedSeconds - lastGoodTime <= MaximumStaleSeconds ? cached : WindowPlatformMap.Unavailable;
        return Current;
    }

    public WindowPlatformMap Build(WindowMapSnapshot snapshot, double minimumSegmentWidth = 24)
    {
        if (!double.IsFinite(minimumSegmentWidth) || minimumSegmentWidth <= 0) throw new ArgumentOutOfRangeException(nameof(minimumSegmentWidth));
        segments.Clear();
        for (int index = 0; index < snapshot.Windows.Length; index++)
            AppendSegments(snapshot, index, minimumSegmentWidth);
        return new(snapshot, segments.ToImmutableArray(), true);
    }

    // Refresh ONLY the support's exposed edge; other geometry and Z remain at 200 ms.
    // Immutable overlays are reused while stationary and never published by WindowTracker.
    internal WindowPlatformMap RefreshSupport(WindowPlatformMap map, WindowGeometry geometry, double minimumSegmentWidth)
    {
        if (!map.IsAvailable) return map;
        if (ReferenceEquals(map, supportBase) && geometry == supportGeometry && minimumSegmentWidth == supportMinimum)
            return supportMap!;
        int index = -1;
        for (int i = 0; i < map.Source.Windows.Length; i++)
            if (map.Source.Windows[i].Handle == geometry.Handle) { index = i; break; }
        if (index < 0) return map;
        if (geometry == map.Source.Windows[index]) return map;
        var snapshot = map.Source with { Windows = map.Source.Windows.SetItem(index, geometry) };
        segments.Clear();
        foreach (var segment in map.Platforms)
            if (segment.Handle != geometry.Handle) segments.Add(segment);
        AppendSegments(snapshot, index, minimumSegmentWidth);
        supportBase = map; supportGeometry = geometry; supportMinimum = minimumSegmentWidth;
        return supportMap = new(snapshot, segments.ToImmutableArray(), true);
    }

    private void AppendSegments(WindowMapSnapshot snapshot, int index, double minimumSegmentWidth)
    {
        var window = snapshot.Windows[index];
        if (!window.IsVisible) return;
        blockers.Clear();
        foreach (var higher in snapshot.Windows)
        {
            if (!higher.IsVisible || higher.ZOrder >= window.ZOrder || higher.Bounds.Top > window.TopEdge || higher.Bounds.Bottom <= window.TopEdge) continue;
            int left = Math.Max(window.EdgeLeft, higher.Bounds.Left), right = Math.Min(window.EdgeRight, higher.Bounds.Right);
            if (left < right) blockers.Add((left, right));
        }
        blockers.Sort(static (a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right));
        int cursor = window.EdgeLeft;
        foreach (var blocked in blockers)
        {
            if ((long)blocked.Left - cursor >= minimumSegmentWidth)
                segments.Add(new(window.Handle, index, snapshot.Version, cursor, blocked.Left, window.TopEdge));
            cursor = Math.Max(cursor, blocked.Right);
        }
        if ((long)window.EdgeRight - cursor >= minimumSegmentWidth)
            segments.Add(new(window.Handle, index, snapshot.Version, cursor, window.EdgeRight, window.TopEdge));
    }
}

public readonly record struct WindowSupport(WindowPlatform Segment, WindowBounds SourceBounds, double RelativeX)
{
    public nint Handle => Segment.Handle;
    public int SurfaceY => Segment.Y;
}
