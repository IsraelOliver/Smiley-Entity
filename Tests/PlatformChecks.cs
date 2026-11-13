using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Smiley;

internal static class PlatformChecks
{
    private static long version;
    private static readonly WorkArea Area = new(-2000, 0, 3000, 1400);
    private const double Width = 210, Height = 190;
    private static WindowGeometry Window(int id, int left = 100, int top = 500, int right = 700, int bottom = 650, int z = 0)
        => new(id, new(left, top, right, bottom), WindowVisibility.Visible, false, z);
    private static WindowMapSnapshot Snapshot(params WindowGeometry[] windows) => new(++version, DateTimeOffset.UnixEpoch, windows.ToImmutableArray(), []);
    private static WindowPlatformMap Map(params WindowGeometry[] windows) => new WindowPlatformProvider().Build(Snapshot(windows));
    private static SmileyController Create(WindowPlatformMap map, double x = 100, double y = 100)
    {
        var c = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
        c.SetPlatforms(map); c.MoveTo(x, y, Area, Width, Height); return c;
    }
    private static SmileyController Land(WindowPlatformMap? map = null)
    {
        var c = Create(map ?? Map(Window(1))); c.Tick(2, Area, Width, Height); return c;
    }

    internal static void Run(Action<bool, string> check)
    {
        var provider = new WindowPlatformProvider(); var original = Snapshot(Window(1));
        var flat = provider.Build(original);
        check(flat.Platforms is [{ Handle: 1, Left: 100, Right: 700, Y: 500 }] && ReferenceEquals(flat.Source, original),
            "platforms: visible top edge becomes source-indexed physical segment without copying window map");
        var split = Map(Window(1, z: 1), Window(2, 300, 400, 450, 600));
        var exposed = split.Platforms.Where(p => p.Handle == 1).ToArray();
        check(exposed.Length == 2 && exposed[0].Left == 100 && exposed[0].Right == 300 && exposed[1].Left == 450 && exposed[1].Right == 700,
            "platforms: foreground rectangle cuts exactly the covered part of background top edge");
        var merged = Map(Window(1, 100, 500, 1000, 900, 4), Window(2, 250, 400, 400, 700, 0), Window(3, 350, 400, 600, 700, 1),
            Window(4, 600, 400, 650, 700, 2), Window(5, 250, 400, 400, 700, 3));
        exposed = merged.Platforms.Where(p => p.Handle == 1).ToArray();
        check(exposed.Length == 2 && exposed[0].Right == 250 && exposed[1].Left == 650 && exposed[1].Right == 1000,
            "platforms: overlapping, touching and duplicate blockers produce merged gaps without duplicate segments");
        var hidden = Map(Window(1, z: 1), Window(2, 0, 400, 1000, 900));
        var narrow = Map(Window(1, 100, 500, 123, 900));
        check(!hidden.Platforms.Any(p => p.Handle == 1) && narrow.Platforms.IsEmpty, "platforms: fully occluded and too-short surfaces are unavailable");
        var unrelated = Map(Window(1, z: 2), Window(2, 200, 600, 600, 900, 0), Window(3, 200, 300, 600, 500, 1));
        check(unrelated.Platforms.Single(p => p.Handle == 1).Width == 600, "platforms: horizontal overlap alone and exclusive bottom contact do not occlude a top edge");
        var reverse = provider.Build(Snapshot(merged.Source.Windows.Reverse().ToArray()));
        check(reverse.Platforms.Where(p => p.Handle == 1).Select(p => (p.Left, p.Right)).SequenceEqual(exposed.Select(p => (p.Left, p.Right))),
            "platforms: exposure depends on Z values rather than input enumeration order");

        var c = Land();
        check(c.IsGrounded && c.ActiveSupport?.Handle == 1 && c.FeetY == 500 && c.VelocityY == 0 && c.Y == 310,
            "platforms: downward fall lands with visual foot reference aligned exactly and one active support");
        var first = Land(Map(Window(1), Window(2, 100, 800, 700, 1000, 1)));
        check(first.ActiveSupport?.Handle == 1 && first.FeetY == 500, "platforms: swept descent selects first upper platform before lower surface or desktop floor");
        var upwardMap = Map(Window(1, top: 600, bottom: 800));
        c = Create(upwardMap, y: 500); c.BeginDrag(); c.MoveTo(100, 500, Area, Width, Height); c.EndDrag(0, -650);
        c.Tick(0.25, Area, Width, Height);
        bool passedUp = !c.IsGrounded && c.ActiveSupport is null && c.FeetY < 600 && c.VelocityY < 0;
        c.Tick(1, Area, Width, Height);
        check(passedUp && c.ActiveSupport?.Handle == 1 && c.FeetY == 600, "platforms: upward traversal is free and the same plane catches the later descending crossing");
        c = Create(Map(Window(1, 200, 500, 280, 650)), x: 0); c.BeginDrag(); c.MoveTo(0, 100, Area, Width, Height); c.EndDrag(700, 800);
        double hitTime = (Math.Sqrt(800 * 800 + 2 * 1100 * 210) - 800) / 1100;
        c.Tick(1, Area, Width, Height);
        check(c.ActiveSupport?.Handle == 1 && c.FeetY == 500 && Math.Abs(c.X - 700 * hitTime) < 1e-7,
            "platforms: fast diagonal throw hits short segment at crossing time even when final free-flight position would miss it");
        c = Create(Map(Window(1, 500, 500, 700, 900)), x: 0); c.Tick(2, Area, Width, Height);
        check(c.IsGrounded && c.ActiveSupport is null && c.FeetY == Area.Bottom, "platforms: silhouette/arm width is not enough when foot interval misses surface");
        c = Land(); c.TryMove(MovementMode.Walking, 1, 10); c.Tick(0.5, Area, Width, Height);
        bool walking = c.ActiveSupport?.Handle == 1 && c.FeetY == 500 && Math.Abs(c.X - 132.5) < 1e-7 && c.AnimationFrame == 2;
        c.SetMovementMode(MovementMode.Running); c.Tick(0.25, Area, Width, Height);
        check(walking && c.ActiveSupport?.Handle == 1 && c.FeetY == 500 && Math.Abs(c.X - 167.5) < 1e-7 && c.AnimationFrame == 2,
            "platforms: existing walk/run speeds and eight-frame cycles work on support with fixed feet height");
        c = Land(Map(Window(1, 100, 500, 300, 650))); c.TryMove(MovementMode.Running, 1, 10); c.Tick(0.8, Area, Width, Height);
        check(!c.IsGrounded && c.ActiveSupport is null && c.X > 199 && c.FeetY > 500 && c.VelocityY > 0,
            "platforms: walking beyond sufficient foot overlap falls during same update without recapturing zero-time edge");
        c = Land(); bool jumped = c.TryJump(); c.Tick(0.1, Area, Width, Height);
        bool detached = c.ActiveSupport is null && c.VelocityY < 0;
        c.Tick(1, Area, Width, Height);
        check(jumped && detached && c.ActiveSupport?.Handle == 1 && c.FeetY == 500, "platforms: manual jump releases support and can land on it again without double jump");

        c = Land(); c.TryMove(MovementMode.Walking, 1, 5); c.Tick(0.5, Area, Width, Height);
        double relative = c.ActiveSupport!.Value.RelativeX;
        c.SetPlatforms(Map(Window(1, 160, 540, 760, 690))); c.ReconcilePlatforms();
        check(Math.Abs(c.X - (160 + relative)) < 1e-7 && c.FeetY == 540 && c.VelocityY == 0,
            "platforms: moving support preserves updated horizontal offset and follows new top without artificial momentum");
        c = Land(); double oldX = c.X, oldY = c.Y;
        c.SetPlatforms(Map(Window(1, 1000, 900, 1010, 1000))); c.ReconcilePlatforms();
        check(c.ActiveSupport is null && !c.IsGrounded && c.X == oldX && c.Y == oldY,
            "platforms: moved/resized source with invalid support drops at current position without teleport");
        bool removed = true;
        foreach (var visibility in new[] { WindowVisibility.Minimized, WindowVisibility.Hidden, WindowVisibility.Unavailable })
        {
            c = Land(); c.SetPlatforms(Map(Window(1) with { Visibility = visibility })); c.ReconcilePlatforms();
            removed &= c.ActiveSupport is null && !c.IsGrounded && c.X == 100 && c.Y == 310;
        }
        c = Land(); c.SetPlatforms(Map()); c.ReconcilePlatforms(); c.Tick(0.1, Area, Width, Height);
        check(removed && c.ActiveSupport is null && c.FeetY > 500 && c.DisplayPose == Pose.Falling,
            "platforms: minimized/hidden/unavailable/closed windows release current support and reuse falling pose");
        c = Land(); c.SetPlatforms(Map(Window(1, z: 1), Window(2, 100, 450, 300, 700))); c.ReconcilePlatforms();
        check(c.ActiveSupport is null && c.X == 100 && c.Y == 310, "platforms: new foreground occlusion invalidates foot region without snapping to covering window");
        c = Create(Map(), y: 310); c.SetPlatforms(Map(Window(1))); c.Tick(0.1, Area, Width, Height);
        check(c.ActiveSupport is null && c.FeetY > 500, "platforms: window appearing exactly under stationary unsupported feet cannot arbitrarily capture body");
        c = Create(Map(), y: 500); c.SetPlatforms(Map(Window(1, top: 400))); c.Tick(0.1, Area, Width, Height);
        check(c.ActiveSupport is null && c.Y > 500, "platforms: rising window passing through descending body cannot lift or capture it from below");
        c = Land(); var recycled = Snapshot(Window(1)) with { Changes = [new(1, WindowChangeKind.Disappeared, null, null), new(1, WindowChangeKind.Appeared, null, Window(1))] };
        c.SetPlatforms(provider.Build(recycled)); c.ReconcilePlatforms();
        check(c.ActiveSupport is null && !c.IsGrounded, "platforms: recycled HWND replacement cannot inherit old support");

        c = Land(); c.BeginDrag(); bool dragDetached = c.ActiveSupport is null;
        c.MoveTo(150, 310, Area, Width, Height); c.EndDrag();
        bool exactDrop = c.ActiveSupport?.Handle == 1 && c.IsGrounded && c.FeetY == 500;
        c.BeginDrag(); c.MoveTo(150, 310, Area, Width, Height); c.EndDrag(400, -400);
        check(dragDetached && exactDrop && c.ActiveSupport is null && !c.IsGrounded && c.VelocityY < 0 && c.VelocityX > 0,
            "platforms: drag detaches; quiet valid release attaches; upward throwing from same contact lifts off");
        c = Land(); c.SetPaused(true); c.SetPlatforms(Map(Window(1, 150, 540, 750, 690))); c.ReconcilePlatforms(); c.Tick(20, Area, Width, Height);
        bool frozen = c.X == 100 && c.Y == 310;
        c.SetPaused(false); c.Tick(0.016, Area, Width, Height);
        check(frozen && c.X == 150 && c.FeetY == 540 && c.VelocityY == 0, "platforms: paused support remains frozen and reconciles latest geometry once on resume");
        c = Land(); c.SetPaused(true); c.SetPlatforms(Map()); c.ReconcilePlatforms(); bool held = c.IsGrounded;
        c.SetPaused(false); c.Tick(0.1, Area, Width, Height);
        check(held && !c.IsGrounded && c.Y > 310 && c.Y < 320, "platforms: source closed during pause starts ordinary short fall on resume without elapsed-time jump");

        var timed = new WindowPlatformProvider(); var time = DateTimeOffset.UnixEpoch.AddSeconds(1);
        var stable = timed.Update(original, true, time, 0);
        c = Land(stable); var grace = timed.Update(original, false, time.AddSeconds(0.2), 0.5);
        c.SetPlatforms(grace); c.ReconcilePlatforms(); bool tolerated = c.ActiveSupport is not null && ReferenceEquals(stable, grace);
        var expired = timed.Update(original, false, time.AddSeconds(0.4), 1.01); c.SetPlatforms(expired); c.ReconcilePlatforms(); c.Tick(0.1, Area, Width, Height);
        var recovered = timed.Update(original, true, time.AddSeconds(0.6), 1.1); c.SetPlatforms(recovered); c.Tick(0.1, Area, Width, Height);
        check(tolerated && !expired.IsAvailable && recovered.IsAvailable && c.ActiveSupport is null && c.Y > 310,
            "platforms: transient tracker failure has one-second grace then drops; recovery never snaps body upward");
        check(!timed.Update(original, true, time.AddSeconds(0.6), 10).IsAvailable,
            "platforms: unchanged stale heartbeat cannot sustain support indefinitely even if old success flag remains true");
        var heartbeat = timed.Update(original, true, time.AddSeconds(2), 11);
        var heartbeat2 = timed.Update(original, true, time.AddSeconds(2.2), 11.2);
        check(ReferenceEquals(heartbeat, heartbeat2), "platforms: healthy heartbeat with identical geometry reuses platform snapshot");

        bool dpi = true;
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var world = new WorkArea(-2000 * scale, 0, 3000 * scale, 1400 * scale);
            var geometry = Window(1, (int)(100 * scale), (int)(500 * scale), (int)(700 * scale), (int)(900 * scale));
            var map = new WindowPlatformProvider().Build(Snapshot(geometry), 24 * scale);
            var body = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
            body.SetPlatforms(map); body.MoveTo(100 * scale, 100 * scale, world, Width * scale, Height * scale);
            body.Tick(2, world, Width * scale, Height * scale, scale);
            dpi &= body.ActiveSupport?.Handle == 1 && Math.Abs(body.FeetY / scale - 500) < 1e-7;
        }
        check(dpi, "platforms: support width, collisions and visual foot line agree at 100/125/150/200 percent DPI");
        var negativeArea = new WorkArea(-1920, -1080, 0, 0);
        c = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
        c.SetPlatforms(Map(Window(1, -1100, -600, -100, -200))); c.MoveTo(-1000, -1000, negativeArea, Width, Height); c.Tick(2, negativeArea, Width, Height);
        check(c.ActiveSupport?.Handle == 1 && c.FeetY == -600 && c.X == -1000, "platforms: negative horizontal and vertical monitor coordinates remain physical and supported");
        var maximized = Map(Window(1, -2000, 0, 3000, 1400) with { IsMaximized = true });
        c = Create(maximized); c.Tick(2, Area, Width, Height);
        check(maximized.Platforms.Length == 1 && c.IsGrounded && c.ActiveSupport is null && c.FeetY == Area.Bottom,
            "platforms: maximized top edge is geometric data but cannot park body outside monitor headroom");
        var coarse = Create(Map(Window(1, 100, 500, 300, 650))); var fine = Create(coarseMap());
        coarse.Tick(2, Area, Width, Height); fine.Tick(2, Area, Width, Height);
        coarse.TryMove(MovementMode.Running, 1, 10); fine.TryMove(MovementMode.Running, 1, 10);
        coarse.Tick(2, Area, Width, Height); for (int i = 0; i < 120; i++) fine.Tick(1.0 / 60, Area, Width, Height);
        check(Math.Abs(coarse.X - fine.X) < 1e-6 && Math.Abs(coarse.Y - fine.Y) < 1e-6 && coarse.IsGrounded == fine.IsGrounded,
            "platforms: landing, walk-off and fall consume event time independently of render rate");
        static WindowPlatformMap coarseMap() => Map(Window(1, 100, 500, 300, 650));
        var actor = Create(Map(Window(1))); var autonomy = new SmileyBehaviorController(actor, random: new Random(1));
        autonomy.SetEnabled(false); autonomy.Tick(2, Area, Width, Height); bool offSupport = actor.ActiveSupport?.Handle == 1;
        actor.TryMove(MovementMode.Walking, 1, 0.5); autonomy.Tick(0.2, Area, Width, Height);
        check(offSupport && actor.ActiveSupport?.Handle == 1 && actor.IsMoving, "platforms: unchanged behaviour layer can idle and execute finite locomotion on window support");
        NativeIntegration(check);
    }

    private static void NativeIntegration(Action<bool, string> check)
    {
        var fixture = new Window { Width = 600, Height = 240, Left = 200, Top = 450, ShowInTaskbar = true, ShowActivated = false, Topmost = true };
        try
        {
            fixture.Show(); Flush(); nint handle = new WindowInteropHelper(fixture).Handle;
            using var tracker = new WindowTracker(new NativeWindowGeometrySource(0, 0)); tracker.PollOnce();
            var geometry = tracker.Current.Windows.Single(w => w.Handle == handle);
            var provider = new WindowPlatformProvider(); var map = provider.Build(tracker.Current);
            var world = new WorkArea(-10000, -10000, 10000, 10000);
            var body = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
            body.SetPlatforms(map); body.MoveTo(geometry.Bounds.Left + 50, geometry.TopEdge - Height - 1, world, Width, Height); body.Tick(0.2, world, Width, Height);
            check(body.ActiveSupport?.Handle == handle && body.FeetY == geometry.TopEdge, "platforms native: real DWM fixture becomes exposed landing surface");
            double relative = body.ActiveSupport!.Value.RelativeX;
            fixture.Left += 60; fixture.Top += 20; Flush(); tracker.PollOnce(); body.SetPlatforms(provider.Build(tracker.Current)); body.ReconcilePlatforms();
            var moved = tracker.Current.Windows.Single(w => w.Handle == handle);
            check(body.ActiveSupport?.Handle == handle && body.X == moved.Bounds.Left + relative && body.FeetY == moved.TopEdge,
                "platforms native: moving real WPF source carries existing physical support");
            fixture.WindowState = WindowState.Minimized; Flush(); tracker.PollOnce(); body.SetPlatforms(provider.Build(tracker.Current)); body.ReconcilePlatforms();
            bool minimized = body.ActiveSupport is null && !body.IsGrounded;
            fixture.WindowState = WindowState.Normal; Flush(); tracker.PollOnce(); geometry = tracker.Current.Windows.Single(w => w.Handle == handle);
            body.SetPlatforms(provider.Build(tracker.Current)); body.MoveTo(geometry.Bounds.Left + 50, geometry.TopEdge - Height - 1, world, Width, Height); body.Tick(0.2, world, Width, Height);
            fixture.Close(); tracker.PollOnce(); body.SetPlatforms(provider.Build(tracker.Current)); body.ReconcilePlatforms();
            check(minimized && body.ActiveSupport is null && !body.IsGrounded, "platforms native: minimization and closure of actual source release its support");
        }
        finally { if (fixture.IsLoaded) fixture.Close(); }
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); DwmFlush(); }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
