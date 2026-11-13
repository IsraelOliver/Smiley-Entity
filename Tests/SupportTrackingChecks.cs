using System.Windows;
using System.Windows.Interop;
using Smiley;

internal static class SupportTrackingChecks
{
    private sealed class Source : IWindowGeometrySource, ISingleWindowGeometrySource
    {
        internal WindowObservation Value = new(new(1, new(100, 500, 700, 700), WindowVisibility.Visible, false, 0), 10, 20);
        internal int FullCalls, SingleCalls;
        internal bool Fail;
        public bool TryCapture(List<WindowObservation> destination) { FullCalls++; destination.Add(Value); return true; }
        public bool TryCaptureWindow(nint handle, out WindowObservation observation)
        { SingleCalls++; observation = Value; return !Fail && handle == Value.Geometry.Handle; }
    }

    internal static void Run(Action<bool, string> check)
    {
        var source = new Source(); using var tracker = new WindowTracker(source);
        tracker.PollOnce(); var snapshot = tracker.Current;
        var provider = new WindowPlatformProvider(); var map = provider.Build(snapshot);
        var area = new WorkArea(-2000, -1000, 3000, 2000);
        var body = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
        body.SetPlatforms(map); body.MoveTo(150, 100, area, 210, 190); body.Tick(1, area, 210, 190);
        bool follows = body.ActiveSupport?.Handle == 1;
        for (int frame = 1; frame <= 60; frame++)
        {
            source.Value = source.Value with { Geometry = source.Value.Geometry with { Bounds = new(100 + frame * 2, 500 + frame, 700 + frame * 2, 700 + frame) } };
            follows &= tracker.TryRefreshWindow(1, out var geometry);
            body.SetPlatforms(provider.RefreshSupport(map, geometry, 24));
            body.ReconcilePlatforms(); body.Tick(1.0 / 60, area, 210, 190);
            follows &= body.X == 150 + frame * 2 && body.FeetY == 500 + frame;
        }
        check(follows && source.FullCalls == 1 && source.SingleCalls == 60 && tracker.Interval.TotalMilliseconds == 200 &&
            ReferenceEquals(snapshot, tracker.Current) && snapshot.Windows[0].Bounds.Left == 100,
            "support refresh: 60 targeted samples carry body without full enumeration or modifying immutable 200ms snapshot");
        tracker.TryRefreshWindow(1, out var current);
        var liveMap = provider.RefreshSupport(map, current, 24);
        check(ReferenceEquals(liveMap, provider.RefreshSupport(map, current, 24)),
            "support refresh: stationary targeted geometry reuses immutable overlay");
        tracker.PollOnce(); var updated = provider.Build(tracker.Current);
        body.SetPlatforms(provider.RefreshSupport(updated, current, 24)); body.ReconcilePlatforms();
        check(body.X == 270 && body.FeetY == 560 && body.ActiveSupport is not null,
            "support refresh: next full snapshot preserves position without rollback or applying movement twice");
        source.Value = source.Value with { Geometry = current with { Visibility = WindowVisibility.Minimized, Bounds = default } };
        tracker.TryRefreshWindow(1, out var minimized);
        body.SetPlatforms(provider.RefreshSupport(updated, minimized, 24)); body.ReconcilePlatforms();
        check(body.ActiveSupport is null && !body.IsGrounded && body.X == 270 && body.FeetY == 560,
            "support refresh: targeted minimization removes support without position jump");
        source.Value = source.Value with { ProcessId = 99 };
        check(!tracker.TryRefreshWindow(1, out _) && !tracker.TryRefreshWindow(999, out _) && source.FullCalls == 2,
            "support refresh: recycled identity and unknown HWND are rejected without enumeration");
        source.Fail = true;
        check(!tracker.TryRefreshWindow(1, out _) && !provider.RefreshSupport(WindowPlatformMap.Unavailable, current, 24).IsAvailable,
            "support refresh: failed targeted read and expired general map cannot invent valid support");

        var fixture = new Window { Width = 400, Height = 180, Left = 200, Top = 450, ShowActivated = false };
        try
        {
            fixture.Show(); var handle = new WindowInteropHelper(fixture).Handle;
            using var native = new WindowTracker(new NativeWindowGeometrySource(0, 0)); native.PollOnce();
            var before = native.Current;
            NativeMethods.GetWindowRect(handle, out var rect);
            NativeMethods.SetWindowPos(handle, 0, rect.Left + 73, rect.Top + 27, 0, 0, 0x0015);
            bool read = native.TryRefreshWindow(handle, out var moved);
            var original = before.Windows.Single(w => w.Handle == handle);
            check(read && moved.Bounds.Left == original.Bounds.Left + 73 && moved.Bounds.Top == original.Bounds.Top + 27 &&
                ReferenceEquals(before, native.Current), "support refresh native: DWM reads moved WPF support before next general poll");
            fixture.Close();
            check(!native.TryRefreshWindow(handle, out _), "support refresh native: closed support HWND rejected immediately");
        }
        finally { if (fixture.IsLoaded) fixture.Close(); }
    }
}
