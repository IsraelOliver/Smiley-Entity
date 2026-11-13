using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Smiley;

internal static class WindowTrackerChecks
{
    private static WindowObservation Observe(int id, WindowBounds? bounds = null, WindowVisibility visibility = WindowVisibility.Visible, bool maximized = false, uint pid = 10)
        => new(new((nint)id, bounds ?? new WindowBounds(-1200, -200, -800, 100), visibility, maximized, 0), pid, 20);
    private sealed class FakeSource : IWindowGeometrySource
    {
        internal WindowObservation[] Windows = [];
        internal bool Fail;
        internal int Calls;
        public bool TryCapture(List<WindowObservation> destination) { Calls++; destination.AddRange(Windows); return !Fail; }
    }

    internal static void Run(Action<bool, string> check)
    {
        var main = new WindowMetadata(1, 10, true, true, false, 0, 0, "ApplicationWindow");
        WindowMetadata[] excluded = [main with { IsValid = false }, main with { Handle = 0 }, main with { IsRoot = false },
            main with { HasOwner = true }, main with { Style = WindowClassifier.Child }, main with { ExtendedStyle = WindowClassifier.ToolWindow },
            main with { ExtendedStyle = WindowClassifier.NoActivate }, main with { ExtendedStyle = WindowClassifier.Transparent },
            main with { ClassName = "Progman" }, main with { ClassName = "WorkerW" }, main with { ClassName = "Shell_TrayWnd" },
            main with { ClassName = "Shell_SecondaryTrayWnd" }, main with { ClassName = "#32768" }, main with { ClassName = "tooltips_class32" }];
        check(WindowClassifier.IsMainWindow(main, 2, 30) && excluded.All(w => !WindowClassifier.IsMainWindow(w, 2, 30)),
            "windows: classification excludes invalid, child, owned, shell, tooltip and overlay windows");
        check(!WindowClassifier.IsMainWindow(main, 1, 30) && !WindowClassifier.IsMainWindow(main, 2, 10) &&
            WindowClassifier.IsMainWindow(main with { ExtendedStyle = 0x80000 }, 2, 30),
            "windows: excludes Smiley handle/process while retaining ordinary layered application windows");
        var source = new FakeSource { Windows = [Observe(1)] };
        using var tracker = new WindowTracker(source);
        int notifications = 0; tracker.SnapshotChanged += (_, _) => notifications++;
        tracker.PollOnce(); var initial = tracker.Current;
        check(initial.Windows.Length == 1 && initial.Changes.Single().Kind == WindowChangeKind.Appeared && initial.Windows[0].TopEdge == -200 &&
            initial.Windows[0].EdgeLeft == -1200 && initial.Windows[0].EdgeRight == -800, "windows: immutable geometric snapshot exposes negative coordinates and future top edge");
        tracker.PollOnce();
        check(ReferenceEquals(initial, tracker.Current) && notifications == 1 && tracker.LastPollSucceeded,
            "windows: unchanged polls reuse snapshot/arrays and emit no duplicate change notifications");
        source.Windows = [Observe(1, new(-1100, -100, -700, 200))]; tracker.PollOnce();
        check(tracker.Current.Changes.Single().Kind == WindowChangeKind.Moved && initial.Windows[0].Bounds.Left == -1200,
            "windows: move detected without resize and previous snapshot remains immutable");
        source.Windows = [Observe(1, new(-1100, -100, -600, 250))]; tracker.PollOnce();
        check(tracker.Current.Changes.Single().Kind == WindowChangeKind.Resized, "windows: resize distinguished from move");
        source.Windows = [Observe(1, new(-1000, -90, -400, 400), maximized: true)]; tracker.PollOnce();
        var flags = tracker.Current.Changes.Single().Kind;
        check(flags.HasFlag(WindowChangeKind.Moved) && flags.HasFlag(WindowChangeKind.Resized) && flags.HasFlag(WindowChangeKind.MaximizedChanged) && tracker.Current.Windows[0].IsMaximized,
            "windows: simultaneous geometry and maximization changes kept in one coherent delta");
        source.Windows = [Observe(1, default(WindowBounds), WindowVisibility.Minimized)]; tracker.PollOnce();
        check(tracker.Current.Windows.IsEmpty && tracker.Current.Changes.Single().Kind.HasFlag(WindowChangeKind.Minimized),
            "windows: minimized window removed from usable map while minimization event is retained");
        source.Windows = [Observe(1)]; tracker.PollOnce();
        check(tracker.Current.Windows.Length == 1 && tracker.Current.Changes.Single().Kind.HasFlag(WindowChangeKind.Restored),
            "windows: same HWND restored without false disappeared/appeared lifecycle");
        source.Windows = [Observe(1, default(WindowBounds), WindowVisibility.Minimized)]; tracker.PollOnce();
        source.Windows = [Observe(1, default(WindowBounds), WindowVisibility.Cloaked)]; tracker.PollOnce();
        check(tracker.Current.Windows.IsEmpty && tracker.Current.Changes.Single().Kind.HasFlag(WindowChangeKind.Restored),
            "windows: leaving minimization is reported even when restored window remains cloaked on another desktop");
        source.Windows = [Observe(1)]; tracker.PollOnce();
        bool visibility = true;
        foreach (var state in new[] { WindowVisibility.Hidden, WindowVisibility.Cloaked, WindowVisibility.Unavailable })
        {
            source.Windows = [Observe(1, default(WindowBounds), state)]; tracker.PollOnce();
            visibility &= tracker.Current.Windows.IsEmpty && tracker.Current.Changes.Single().After?.Visibility == state;
        }
        check(visibility, "windows: hidden, cloaked and failed-DWM samples never become platform candidates");
        source.Windows = [Observe(1, new(10, 20, 10, 200))]; tracker.PollOnce();
        check(tracker.Current.Windows.IsEmpty, "windows: empty/inverted geometric bounds rejected");
        source.Windows = [Observe(1), Observe(2, new(-1100, -100, -500, 400))]; tracker.PollOnce();
        source.Windows = [source.Windows[1], source.Windows[0]]; tracker.PollOnce();
        check(tracker.Current.Windows[0].Handle == 2 && tracker.Current.Windows[0].ZOrder == 0 && tracker.Current.Windows[1].ZOrder == 1 &&
            tracker.Current.Changes.All(c => c.Kind.HasFlag(WindowChangeKind.ZOrderChanged) || c.Kind.HasFlag(WindowChangeKind.Appeared)),
            "windows: overlapping windows retain front-to-back Z order independently of geometry");
        source.Windows = [Observe(1), Observe(1), Observe(2)]; tracker.PollOnce();
        check(tracker.Current.Windows.Length == 2 && tracker.Current.Windows[1].ZOrder == 1, "windows: duplicate handles do not corrupt ordering or duplicate rectangles");
        source.Windows = [Observe(1)]; tracker.PollOnce();
        check(tracker.Current.Changes.Any(c => c.Handle == 2 && c.Kind == WindowChangeKind.Disappeared), "windows: disappeared HWND removed from map and reported once");
        var complete = tracker.Current; source.Fail = true; source.Windows = []; tracker.PollOnce();
        check(ReferenceEquals(complete, tracker.Current) && !tracker.LastPollSucceeded, "windows: failed or partial enumeration preserves last complete snapshot");
        source.Fail = false; source.Windows = [Observe(1, pid: 99)]; tracker.PollOnce();
        check(tracker.Current.Changes.Count(c => c.Handle == 1) == 2 && tracker.Current.Changes.Any(c => c.Kind == WindowChangeKind.Appeared) && tracker.Current.Changes.Any(c => c.Kind == WindowChangeKind.Disappeared),
            "windows: recycled HWND with different process identity creates replacement events");
        bool physical = true;
        foreach (int dpi in new[] { 96, 120, 144, 192 })
        {
            var physicalBounds = new WindowBounds(-2 * dpi, -dpi, 8 * dpi, 5 * dpi);
            source.Windows = [Observe(1, physicalBounds)]; tracker.PollOnce();
            physical &= tracker.Current.Windows[0].Bounds == physicalBounds;
        }
        check(physical, "windows: physical rectangles remain unscaled across negative/mixed-DPI monitor scenarios");
        tracker.SnapshotChanged += (_, _) => tracker.PollOnce(); source.Windows = []; tracker.PollOnce();
        check(tracker.Current.Windows.IsEmpty && tracker.LastPollSucceeded, "windows: event reentrancy cannot publish nested partial snapshots");
        {
            var timedSource = new FakeSource();
            using var independent = new WindowTracker(timedSource, TimeSpan.FromMilliseconds(200));
            independent.Start(); independent.Start(); PumpFor(TimeSpan.FromMilliseconds(470));
            int calls = timedSource.Calls; independent.Stop(); PumpFor(TimeSpan.FromMilliseconds(240));
            check(calls >= 2 && timedSource.Calls == calls && !independent.IsRunning && independent.Interval.TotalMilliseconds == 200,
                "windows: independent 200ms observation runs without render/behaviour updates and stops cleanly");
        }
        tracker.Dispose();
        check(tracker.Current.Windows.IsEmpty && !tracker.IsRunning, "windows: disposing observer releases timer and current geometric state");
        bool rejected = false;
        try { using var invalid = new WindowTracker(new FakeSource(), TimeSpan.FromMilliseconds(1)); } catch (ArgumentOutOfRangeException) { rejected = true; }
        check(rejected, "windows: invalid polling interval rejected");
        NativeIntegration(check);
    }

    private static void NativeIntegration(Action<bool, string> check)
    {
        var fixture = new Window { Width = 360, Height = 240, Left = 100, Top = 100, ShowActivated = false, ShowInTaskbar = true };
        Window? doomed = null;
        try
        {
            fixture.Show(); Flush();
            nint handle = new WindowInteropHelper(fixture).Handle;
            using var native = new NativeWindowGeometrySource(0, 0);
            using var tracker = new WindowTracker(native);
            tracker.PollOnce();
            check(tracker.LastPollSucceeded && tracker.Current.Windows.Any(w => w.Handle == handle && w.IsVisible), "windows native: EnumWindows/DWM detects real WPF application fixture");
            fixture.Left += 40; fixture.Width += 50; Flush(); tracker.PollOnce();
            check(tracker.Current.Changes.Any(c => c.Handle == handle && c.Kind.HasFlag(WindowChangeKind.Moved) && c.Kind.HasFlag(WindowChangeKind.Resized)),
                "windows native: moving/resizing real HWND produces geometric delta");
            fixture.WindowState = WindowState.Minimized; Flush(); tracker.PollOnce();
            check(!tracker.Current.Windows.Any(w => w.Handle == handle) && tracker.Current.Changes.Any(c => c.Handle == handle && c.Kind.HasFlag(WindowChangeKind.Minimized)),
                "windows native: IsIconic excludes minimized fixture");
            fixture.WindowState = WindowState.Normal; Flush(); tracker.PollOnce();
            check(tracker.Current.Windows.Any(w => w.Handle == handle) && tracker.Current.Changes.Any(c => c.Handle == handle && c.Kind.HasFlag(WindowChangeKind.Restored)),
                "windows native: restoring fixture returns same HWND to map");
            fixture.WindowState = WindowState.Maximized; Flush(); tracker.PollOnce();
            check(tracker.Current.Windows.Any(w => w.Handle == handle && w.IsMaximized), "windows native: IsZoomed identifies maximized fixture");
            fixture.Hide(); Flush(); tracker.PollOnce();
            check(!tracker.Current.Windows.Any(w => w.Handle == handle), "windows native: hidden fixture does not remain a usable rectangle");
            fixture.WindowState = WindowState.Normal; fixture.Show(); Flush();
            using var ownProcess = new NativeWindowGeometrySource(handle);
            var observations = new List<WindowObservation>();
            check(ownProcess.TryCapture(observations) && observations.All(o => o.ProcessId != (uint)Environment.ProcessId), "windows native: every Smiley-process window including diagnostic is excluded");
            doomed = new Window { Width = 260, Height = 200, ShowActivated = false, ShowInTaskbar = true };
            doomed.Show(); Flush(); nint doomedHandle = new WindowInteropHelper(doomed).Handle;
            using var race = new NativeWindowGeometrySource(0, 0, () => doomed.Close());
            observations.Clear();
            check(race.TryCapture(observations) && observations.All(o => o.Geometry.Handle != doomedHandle), "windows native: fixture destroyed after DWM read is excluded by final handle validation");
            fixture.Close(); tracker.PollOnce();
            check(tracker.Current.Changes.Any(c => c.Handle == handle && c.Kind == WindowChangeKind.Disappeared), "windows native: closing fixture reports disappearance");
        }
        finally { if (doomed?.IsLoaded == true) doomed.Close(); if (fixture.IsLoaded) fixture.Close(); }
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); DwmFlush(); }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private static void PumpFor(TimeSpan interval)
    {
        var frame = new DispatcherFrame(); var stop = new DispatcherTimer { Interval = interval };
        stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; };
        stop.Start(); Dispatcher.PushFrame(frame);
    }
}
