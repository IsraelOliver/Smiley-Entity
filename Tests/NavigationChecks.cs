using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Smiley;

internal static class NavigationChecks
{
    private static long version;
    private sealed record Scene(SmileyController C, SmileyBehaviorController B, WindowPlatformMap Map, WorkArea Area, double Scale);
    private static WindowGeometry W(int id, int left, int top, int right, int bottom, int z = 0)
        => new(id, new(left, top, right, bottom), WindowVisibility.Visible, false, z);
    private static WindowPlatformMap Map(params WindowGeometry[] windows)
        => new WindowPlatformProvider().Build(new(++version, DateTimeOffset.UnixEpoch, windows.ToImmutableArray(), []));
    private static SmileyBehaviorOptions Options(double chance = 1, bool wander = false) => new()
    {
        IdleMin = 0.1, IdleMax = 0.1, IdleWeight = wander ? 0 : 1, WanderWeight = wander ? 1 : 0,
        RunWeight = 0, HopWeight = 0, NavigationProbability = chance
    };
    private static Scene Create(bool enabled = true, double scale = 1, int dx = 0, int dy = 0, SmileyBehaviorOptions? options = null, int seed = 42)
    {
        WindowGeometry Geometry(int id, int left, int top, int right, int bottom)
            => W(id, dx + (int)(left * scale), dy + (int)(top * scale), dx + (int)(right * scale), dy + (int)(bottom * scale));
        var map = Map(Geometry(1, 300, 700, 700, 950), Geometry(2, 740, 650, 1140, 950));
        var area = new WorkArea(dx, dy, dx + 1600 * scale, dy + 1100 * scale);
        var body = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 }, new Random(seed));
        body.SetPlatforms(map); body.MoveTo(dx + 350 * scale, dy + 500 * scale, area, 210 * scale, 190 * scale);
        body.Tick(1, area, 210 * scale, 190 * scale, scale);
        var behavior = new SmileyBehaviorController(body, options ?? Options(), new Random(seed));
        behavior.SetNavigationMap(map, true); if (enabled) behavior.SetExploreWindows(true);
        return new(body, behavior, map, area, scale);
    }
    private static void Tick(Scene s, double seconds, CursorPosition? cursor = null)
        => s.B.Tick(seconds, s.Area, 210 * s.Scale, 190 * s.Scale, s.Scale, s.Scale, cursor);
    private static void Run(Scene s, double seconds, double step = 1.0 / 60)
    { while (seconds > 1e-9) { double dt = Math.Min(seconds, step); Tick(s, dt); seconds -= dt; } }
    private static void SetMap(Scene s, WindowPlatformMap map, bool fresh = true)
    { s.C.SetPlatforms(map); s.B.SetNavigationMap(map, fresh); }
    private static bool WaitForFlight(Scene s)
    {
        for (int i = 0; i < 100 && s.B.NavigationStage != NavigationStage.Flying; i++) Tick(s, 0.1);
        return s.B.NavigationStage == NavigationStage.Flying;
    }

    internal static void Run(Action<bool, string> check)
    {
        var planner = new SmileyNavigationPlanner(); var s = Create();
        planner.TrySafeRange(s.C, s.Area, 210, 1, out var left, out var right);
        planner.TryLocalDestination(s.C, s.Area, 210, 1, 0, out var edgeLeft);
        planner.TryLocalDestination(s.C, s.Area, 210, 1, 1, out var edgeRight);
        check(left == 215 && right == 575 && edgeLeft == left && edgeRight == right,
            "navigation: same-platform destinations reserve full foot band plus eight-DIP safety margins");
        bool reachable = planner.TryChoose(s.Map, true, s.C, s.Area, 210, 1, 1, 140, null, out var plan, out _);
        double endY = s.C.FeetY - 460 * plan.FlightSeconds + 550 * plan.FlightSeconds * plan.FlightSeconds;
        check(reachable && plan.Target.Handle == 2 && plan.DepartureX == 575 && plan.LandingX == 655 &&
            Math.Abs(endY - 650) < 1e-7 && Math.Abs(plan.DepartureX + plan.HorizontalVelocity * plan.FlightSeconds - plan.LandingX) < 1e-7 &&
            plan.HorizontalVelocity <= 140 && -460 + 1100 * plan.FlightSeconds > 0,
            "navigation: descending ballistic root and bounded horizontal velocity predict a nearby safe landing");
        var high = Map(s.Map.Source.Windows[0], W(2, 740, 590, 1140, 900));
        check(!planner.TryChoose(high, true, s.C, s.Area, 210, 1, 1, 140, null, out _, out var highReason) && highReason.Contains("alto"),
            "navigation: rise beyond jump apex plus safety margin is rejected");
        var far = Map(s.Map.Source.Windows[0], W(2, 1250, 650, 1590, 900));
        check(!planner.TryChoose(far, true, s.C, s.Area, 210, 1, 1, 140, null, out _, out var farReason) && farReason.Contains("horizontal"),
            "navigation: impossible horizontal gap is rejected instead of increasing launch speed");
        var narrow = Map(s.Map.Source.Windows[0], W(2, 740, 650, 770, 900));
        check(!planner.TryChoose(narrow, true, s.C, s.Area, 210, 1, 1, 140, null, out _, out _),
            "navigation: collision-capable narrow ledge is rejected when safe landing margins do not fit");
        check(!planner.TryChoose(s.Map, false, s.C, s.Area, 210, 1, 1, 140, null, out _, out _) &&
            !planner.TryChoose(WindowPlatformMap.Unavailable, true, s.C, s.Area, 210, 1, 1, 140, null, out _, out _),
            "navigation: unhealthy or unavailable geometry never produces a new plan");
        var lowerOverlap = Map(W(1, 300, 700, 700, 740), W(2, 100, 750, 500, 950, 1));
        check(!planner.TryChoose(lowerOverlap, true, s.C, s.Area, 210, 1, 1, 140, null, out _, out _, 2),
            "navigation: plan rejects descent intercepted by original platform before chosen lower surface");
        var outside = Map(s.Map.Source.Windows[0], W(2, 1700, 650, 2100, 900));
        check(!planner.TryChoose(outside, true, s.C, s.Area, 210, 1, 1, 140, null, out _, out _),
            "navigation: destinations outside current monitor work area are never deliberate routes");
        check(!planner.TryChoose(s.Map, true, s.C, s.Area, 210, 1, 1, 65, null, out _, out _),
            "navigation: walking preference cannot borrow running speed to bridge an unreachable gap");
        var nearer = Map(s.Map.Source.Windows[0], s.Map.Source.Windows[1], W(3, 200, 650, 280, 900));
        check(planner.TryChoose(nearer, true, s.C, s.Area, 210, 1, 1, 140, null, out var nearest, out _) && nearest.Target.Handle == 3,
            "navigation: candidate scoring favors nearby reachable surfaces");

        Tick(s, 0.1);
        check(s.B.NavigationStage == NavigationStage.Approaching && s.C.IsMoving && s.C.ActiveSupport?.Handle == 1 && s.C.X == 350,
            "navigation: chooses destination then walks to departure instead of teleporting or launching immediately");
        check(WaitForFlight(s) && Math.Abs(s.C.X - plan.DepartureX) < 1e-6 && s.C.ActiveSupport is null && s.C.VelocityY < 0,
            "navigation: directed jump starts only after exact finite approach reaches launch point");
        Run(s, 1.2);
        check(s.C.IsGrounded && s.C.ActiveSupport?.Handle == 2 && Math.Abs(s.C.X - plan.LandingX) < 1e-6 && s.C.FeetY == 650 &&
            s.B.NavigationStatus.Status.Contains("confirmada"), "navigation: actual continuous physics lands on chosen destination, beyond prediction alone");
        double landedX = s.C.X; Run(s, 3);
        check(s.C.IsGrounded && s.C.X == landedX && s.B.NavigationPlan is null,
            "navigation: mandatory rest and global navigation cooldown prevent immediate second jump");

        var directed = Create();
        check(!directed.C.TryDirectedJump(141) && directed.C.IsGrounded && directed.C.TryDirectedJump(120) &&
            directed.C.VelocityX == 120 && directed.C.VelocityY == -460 && !directed.C.TryDirectedJump(0),
            "navigation: directed launch respects run-speed cap, existing vertical impulse and no double jump");
        var manual = Create(); check(manual.C.TryJump() && manual.C.VelocityX == 0,
            "navigation: manual jump remains vertical");

        foreach (var visibility in new[] { WindowVisibility.Minimized, WindowVisibility.Hidden })
        {
            var cancelled = Create(); Tick(cancelled, 0.1);
            SetMap(cancelled, Map(cancelled.Map.Source.Windows[0], cancelled.Map.Source.Windows[1] with { Visibility = visibility })); Tick(cancelled, 0.1);
            check(cancelled.B.NavigationPlan is null && cancelled.C.IsGrounded && !cancelled.C.IsMoving,
                $"navigation: {visibility} destination cancels approach safely before jump");
        }
        var closed = Create(); Tick(closed, 0.1); SetMap(closed, Map(closed.Map.Source.Windows[0])); Tick(closed, 0.1);
        check(closed.B.NavigationPlan is null && closed.C.IsGrounded && !closed.C.IsMoving,
            "navigation: closed destination cancels pre-launch movement");
        var moving = Create(); Tick(moving, 0.1); var oldPlan = moving.B.NavigationPlan!.Value;
        SetMap(moving, Map(moving.Map.Source.Windows[0], W(2, 730, 660, 1130, 900))); Tick(moving, 0.1);
        check(moving.B.NavigationPlan is { Target.Y: 660, LandingX: 645 } && moving.B.NavigationPlan != oldPlan,
            "navigation: changed destination geometry replans approach on the decision timeline");
        var irrelevant = Create(); Tick(irrelevant, 0.1); var unchangedPlan = irrelevant.B.NavigationPlan;
        SetMap(irrelevant, Map(irrelevant.Map.Source.Windows[0], irrelevant.Map.Source.Windows[1], W(9, 1400, 1000, 1500, 1100))); Tick(irrelevant, 0.1);
        check(irrelevant.B.NavigationPlan == unchangedPlan && irrelevant.C.IsMoving,
            "navigation: unrelated snapshot change preserves plan without recalculating its target");
        var covered = Create(); Tick(covered, 0.1);
        SetMap(covered, Map(covered.Map.Source.Windows[0] with { ZOrder = 2 }, covered.Map.Source.Windows[1] with { ZOrder = 1 }, W(3, 730, 600, 1200, 900))); Tick(covered, 0.1);
        check(covered.B.NavigationPlan is null && covered.C.IsGrounded && !covered.C.IsMoving,
            "navigation: foreground occlusion removes planned destination and stops approach");
        var resized = Create(); Tick(resized, 0.1); SetMap(resized, Map(resized.Map.Source.Windows[0], W(2, 740, 650, 770, 900))); Tick(resized, 0.1);
        check(resized.B.NavigationPlan is null && !resized.C.IsMoving,
            "navigation: destination resized below safe foot margin cancels launch");
        var movedOrigin = Create(); Tick(movedOrigin, 0.1);
        SetMap(movedOrigin, Map(W(1, 310, 710, 710, 960), movedOrigin.Map.Source.Windows[1])); Tick(movedOrigin, 0.1);
        check(movedOrigin.B.NavigationPlan is { OriginY: 710, OriginBounds.Left: 310 } && movedOrigin.C.ActiveSupport?.Handle == 1,
            "navigation: moving source reconciles relative support then replans departure");
        var lostOrigin = Create(); Tick(lostOrigin, 0.1); SetMap(lostOrigin, Map(lostOrigin.Map.Source.Windows[1])); Tick(lostOrigin, 0.1);
        check(lostOrigin.B.NavigationPlan is null && !lostOrigin.C.IsGrounded && lostOrigin.C.VelocityY > 0,
            "navigation: loss of launch support cancels approach and leaves ordinary falling physics authoritative");
        var movingFlight = Create(); WaitForFlight(movingFlight);
        SetMap(movingFlight, Map(movingFlight.Map.Source.Windows[0], W(2, 1250, 650, 1590, 900))); Run(movingFlight, 3);
        check(movingFlight.C.IsGrounded && movingFlight.C.ActiveSupport?.Handle != 2 && movingFlight.C.X < 1000,
            "navigation: target moving out of flight path causes a real miss, never a teleport to planned HWND");
        var failed = Create(); check(WaitForFlight(failed), "navigation: flight fixture begins a real autonomous jump");
        double flightX = failed.C.X, flightVx = failed.C.VelocityX;
        SetMap(failed, Map(failed.Map.Source.Windows[0])); Tick(failed, 0.1);
        check(!failed.C.IsGrounded && Math.Abs(failed.C.X - flightX - flightVx * 0.1) < 1e-6 && failed.C.VelocityX == flightVx,
            "navigation: target disappearing midair preserves ballistic motion without steering or teleporting");
        Run(failed, 3);
        check(failed.C.IsGrounded && failed.C.ActiveSupport?.Handle != 2 && failed.B.NavigationStatus.Status.Contains("cooldown"),
            "navigation: missed destination is detected from real landing and receives retry cooldown");
        SetMap(failed, failed.Map); failed.C.BeginDrag(); failed.C.MoveTo(350, 510, failed.Area, 210, 190); failed.C.EndDrag();
        Run(failed, 10);
        check(failed.B.NavigationPlan is null && failed.C.ActiveSupport?.Handle == 1,
            "navigation: reappearing failed target is not retried during thirty-second per-window cooldown");
        bool retried = false;
        for (int i = 0; i < 250; i++) { Tick(failed, 0.1); retried |= failed.B.NavigationPlan?.Target.Handle == 2; }
        check(retried, "navigation: target becomes eligible again after bounded failure cooldown expires");

        var pause = Create(); Tick(pause, 0.1); double px = pause.C.X; var pausedPlan = pause.B.NavigationPlan;
        pause.C.SetPaused(true); Tick(pause, 100);
        check(pause.C.X == px && pause.B.NavigationPlan == pausedPlan, "navigation: global pause freezes approach, plan and simulation time");
        SetMap(pause, Map(pause.Map.Source.Windows[0])); pause.C.SetPaused(false); Tick(pause, 0.1);
        check(pause.B.NavigationPlan is null && pause.C.IsGrounded, "navigation: latest destination removal is reconciled on resume without a time jump");
        var drag = Create(); Tick(drag, 0.1); drag.B.NotifyManualInteraction(); drag.C.BeginDrag();
        check(drag.B.NavigationPlan is null && drag.C.State == SmileyState.Dragging, "navigation: manual drag immediately overrides planned approach");
        drag.C.MoveTo(400, 300, drag.Area, 210, 190); drag.C.EndDrag(400, -300); drag.B.NotifyManualInteraction();
        check(drag.C.VelocityX == 400 && drag.C.VelocityY == -300 && drag.B.NavigationPlan is null,
            "navigation: cancelling intent never overwrites manual throw impulse");
        var click = Create(); Tick(click, 0.1); click.B.NotifyManualInteraction(); click.C.React(); Run(click, 1.9);
        check(click.B.NavigationPlan is null && !click.C.IsMoving, "navigation: direct click cancels plan and preserves manual quiet interval");
        var curious = Create(); Tick(curious, 0.1);
        Tick(curious, 0.1, new(curious.C.X + 100, curious.C.Y + 80));
        check(curious.B.NavigationPlan is null && curious.B.CurrentAction == BehaviorAction.Curious,
            "navigation: grounded cursor reaction preempts approach");
        var airborneCursor = Create(); WaitForFlight(airborneCursor);
        Tick(airborneCursor, 0.1, new(airborneCursor.C.X - 40, airborneCursor.C.Y));
        check(airborneCursor.B.NavigationStage == NavigationStage.Flying && airborneCursor.B.CurrentAction == BehaviorAction.Explore,
            "navigation: cursor does not trigger flee during an already launched planned flight");
        var off = Create(false); Run(off, 10);
        check(!off.B.ExploreWindows && off.B.NavigationPlan is null && off.C.ActiveSupport?.Handle == 1,
            "navigation: window exploration is off by default and old decisions remain available");
        var disabled = Create(); disabled.B.SetEnabled(false); Run(disabled, 5);
        check(disabled.B.NavigationPlan is null && disabled.C.TryJump(), "navigation: disabled autonomy blocks routes while manual jump remains available");
        var toggle = Create(); WaitForFlight(toggle); double toggleVx = toggle.C.VelocityX; toggle.B.SetExploreWindows(false);
        check(toggle.B.NavigationPlan is null && !toggle.C.IsGrounded && toggle.C.VelocityX == toggleVx,
            "navigation: disabling exploration during flight cancels intention without cancelling physical flight");
        var unhealthy = Create(); Tick(unhealthy, 0.1); unhealthy.B.SetNavigationMap(unhealthy.Map, false); Tick(unhealthy, 0.1);
        check(unhealthy.B.NavigationPlan is null && !unhealthy.C.IsMoving,
            "navigation: tracker failure immediately stops planning despite physical support grace period");

        var local = Create(options: Options(0, true)); SetMap(local, Map(local.Map.Source.Windows[0])); bool safe = true, walked = false;
        for (int i = 0; i < 1200; i++)
        {
            Tick(local, 0.05); walked |= local.C.IsMoving;
            safe &= local.C.IsGrounded && local.C.ActiveSupport?.Handle == 1 && local.C.X >= 215 - 1e-6 && local.C.X <= 575 + 1e-6;
        }
        check(safe && walked, "navigation: repeated seeded local wandering remains inside safe foot margins and stops at finite destinations");
        var ground = Create(); var groundMap = Map(W(2, 500, 1030, 900, 1100)); SetMap(ground, groundMap);
        ground.C.MoveTo(500, 910, ground.Area, 210, 190); Run(ground, 2);
        check(ground.C.ActiveSupport?.Handle == 2 && ground.C.FeetY == 1030,
            "navigation: floor is a valid launch origin for a nearby reachable window");
        var splitMap = Map(W(1, 100, 700, 700, 900, 1), W(2, 400, 650, 440, 800));
        var splitBody = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
        splitBody.SetPlatforms(splitMap); splitBody.MoveTo(200, 500, s.Area, 210, 190); splitBody.Tick(1, s.Area, 210, 190);
        bool splitReachable = planner.TryChoose(splitMap, true, splitBody, s.Area, 210, 1, 1, 140, null, out var splitPlan, out _, 1);
        check(splitReachable && splitPlan.Target.Left == 440 && splitPlan.Target.Handle == 1,
            "navigation: another exposed segment of the same HWND can be a distinct reachable destination");
        var lowCeilingMap = Map(W(1, 300, 250, 700, 450), W(2, 740, 200, 1140, 400));
        var lowBody = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
        lowBody.SetPlatforms(lowCeilingMap); lowBody.MoveTo(350, 50, s.Area, 210, 190); lowBody.Tick(1, s.Area, 210, 190);
        check(!planner.TryChoose(lowCeilingMap, true, lowBody, s.Area, 210, 1, 1, 140, null, out _, out var headroomReason) && headroomReason.Contains("acima"),
            "navigation: insufficient monitor headroom rejects autonomous jump arc");
        var custom = new SmileyController(new SmileyOptions { Gravity = 900, JumpSpeed = 380, Speed = 50, RunSpeed = 110, IdleMin = 10000, IdleMax = 10000 });
        var customMap = Map(W(1, 300, 700, 700, 950), W(2, 720, 650, 1120, 950));
        custom.SetPlatforms(customMap); custom.MoveTo(350, 500, s.Area, 210, 190); custom.Tick(1, s.Area, 210, 190);
        bool configured = planner.TryChoose(customMap, true, custom, s.Area, 210, 1, 1, custom.MotionLimits.RunSpeed, null, out var customPlan, out _);
        custom.TryMove(MovementMode.Walking, 1, (customPlan.DepartureX - custom.X) / 50); custom.Tick(4.5, s.Area, 210, 190);
        custom.TryDirectedJump(customPlan.HorizontalVelocity); custom.Tick(1, s.Area, 210, 190);
        check(configured && Math.Abs(customPlan.FlightSeconds - (380 + Math.Sqrt(380 * 380 - 90000)) / 900) < 1e-8 &&
            custom.ActiveSupport?.Handle == 2 && custom.FeetY == 650,
            "navigation: planner uses configured gravity, jump and movement limits rather than duplicated defaults");
        bool dpi = true;
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var scaled = Create(scale: scale, dx: -3200, dy: -1800); Run(scaled, 6);
            dpi &= scaled.C.ActiveSupport?.Handle == 2 && scaled.C.FeetY == scaled.Map.Source.Windows.Single(w => w.Handle == 2).TopEdge &&
                Math.Abs((scaled.C.X + 3200) / scale - 655) < 1e-6;
        }
        check(dpi, "navigation: actual landing agrees across negative desktop coordinates and 100/125/150/200 percent DPI");
        var coarse = Create(options: Options(0.6), seed: 23); var fine = Create(options: Options(0.6), seed: 23);
        Run(coarse, 35, 0.37); Run(fine, 35, 1.0 / 144);
        check(Math.Abs(coarse.C.X - fine.C.X) < 1e-6 && Math.Abs(coarse.C.Y - fine.C.Y) < 1e-6 &&
            coarse.B.DecisionCount == fine.B.DecisionCount && coarse.B.CurrentAction == fine.B.CurrentAction,
            "navigation: fixed seed yields equivalent exploration across coarse updates and 144Hz rendering");
        NativeIntegration(check);
    }

    private static void NativeIntegration(Action<bool, string> check)
    {
        var source = new Window { Width = 400, Height = 230, Left = 300, Top = 700, Topmost = true, ShowActivated = false };
        var target = new Window { Width = 400, Height = 230, Left = 740, Top = 650, Topmost = true, ShowActivated = false };
        try
        {
            source.Show(); target.Show(); nint origin = new WindowInteropHelper(source).Handle, destination = new WindowInteropHelper(target).Handle;
            using var tracker = new WindowTracker(new NativeWindowGeometrySource(0, 0)); tracker.PollOnce();
            var snapshot = tracker.Current with { Windows = tracker.Current.Windows.Where(w => w.Handle == origin || w.Handle == destination).ToImmutableArray() };
            var map = new WindowPlatformProvider().Build(snapshot);
            var geometry = snapshot.Windows.Single(w => w.Handle == origin);
            var body = new SmileyController(new SmileyOptions { IdleMin = 10000, IdleMax = 10000 });
            var area = new WorkArea(0, 0, 1600, 1100);
            body.SetPlatforms(map); body.MoveTo(350, geometry.TopEdge - 200, area, 210, 190); body.Tick(1, area, 210, 190);
            var behavior = new SmileyBehaviorController(body, Options(), new Random(42)); behavior.SetNavigationMap(map, true); behavior.SetExploreWindows(true);
            behavior.Tick(6, area, 210, 190);
            check(body.IsGrounded && body.ActiveSupport?.Handle == destination && body.FeetY == snapshot.Windows.Single(w => w.Handle == destination).TopEdge,
                "navigation native: real DWM geometry from two WPF windows produces an actual autonomous destination landing");
            var window = new MainWindow(startPaused: true);
            try
            {
                window.Show(); bool initiallyOff = !window.ExploreItem.IsChecked;
                window.ExploreItem.IsChecked = true; window.ExploreItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                bool on = window.ExploreItem.Header.ToString() == "Explorar janelas: ativado";
                window.ExploreItem.IsChecked = false; window.ExploreItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                check(initiallyOff && on && window.ExploreItem.Header.ToString() == "Explorar janelas: desativado",
                    "navigation UI: real menu starts disabled and toggles exploration on and off through bound handlers");
            }
            finally { window.Close(); }
        }
        finally { source.Close(); target.Close(); }
    }
}
