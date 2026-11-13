using Smiley;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class BehaviorChecks
{
    private static readonly WorkArea Area = new(-2000, 0, 2000, 1000);
    private const double Width = 210, Height = 190, Ground = 810;
    private static (SmileyController C, SmileyBehaviorController B) Create(SmileyBehaviorOptions? options = null, Random? random = null, double x = 0)
    {
        var c = new SmileyController(random: new Random(1234)); c.MoveTo(x, Ground, Area, Width, Height);
        return (c, new(c, options, random ?? new Random(42)));
    }
    private static SmileyBehaviorOptions Forced(BehaviorAction action) => new()
    {
        IdleMin = 0.3, IdleMax = 0.3, WanderMin = 0.4, WanderMax = 0.4, RunMin = 0.2, RunMax = 0.2,
        CuriousMin = 0.2, CuriousMax = 0.2,
        IdleWeight = action == BehaviorAction.Idle ? 1 : 0, WanderWeight = action == BehaviorAction.Wander ? 1 : 0,
        RunWeight = action == BehaviorAction.Run ? 1 : 0, HopWeight = action == BehaviorAction.Hop ? 1 : 0
    };
    private static SmileyBehaviorOptions Encounters() => new()
    {
        IdleMin = 100, IdleMax = 100, RunMin = 0.4, RunMax = 0.4, CuriousMin = 0.2, CuriousMax = 0.2,
        CursorCooldown = 0.5
    };
    private static void Tick((SmileyController C, SmileyBehaviorController B) s, double seconds, CursorPosition? cursor = null)
        => s.B.Tick(seconds, Area, Width, Height, cursor: cursor);
    private static void EnterFromLeft((SmileyController C, SmileyBehaviorController B) s)
    {
        Tick(s, 0.1, new(s.C.X - 200, s.C.Y + 95));
        Tick(s, 0.1, new(s.C.X - 80, s.C.Y + 95));
    }
    internal static void Run(Action<bool, string> check)
    {
        var counted = new CountingRandom(); var idle = Create(random: counted);
        int calls = counted.Calls;
        for (int i = 0; i < 120; i++) Tick(idle, 1.0 / 60);
        check(counted.Calls == calls && idle.B.DecisionCount == 0 && idle.C.X == 0, "behavior: initial rest and no random decision per render frame");
        var distribution = Create(); int[] counts = new int[4];
        for (int i = 0; i < 10000; i++) counts[(int)distribution.B.SelectAction()]++;
        check(Math.Abs(counts[0] / 10000.0 - 0.55) < 0.02 && Math.Abs(counts[1] / 10000.0 - 0.25) < 0.02 &&
            Math.Abs(counts[2] / 10000.0 - 0.10) < 0.02 && Math.Abs(counts[3] / 10000.0 - 0.10) < 0.02,
            "behavior: seeded 10000-decision distribution matches 55/25/10/10 weights");
        var seededA = Create(); var seededB = Create(); bool reproduced = true;
        for (int i = 0; i < 200; i++)
        {
            Tick(seededA, 0.5); Tick(seededB, 0.5);
            reproduced &= seededA.B.CurrentAction == seededB.B.CurrentAction && seededA.B.DecisionCount == seededB.B.DecisionCount && seededA.C.X == seededB.C.X && seededA.C.Y == seededB.C.Y;
        }
        check(reproduced, "behavior: full action/physics trace reproduced with same seeds");
        var coarse = Create(); var fine = Create();
        Tick(coarse, 90); for (int i = 0; i < 5400; i++) Tick(fine, 1.0 / 60);
        check(coarse.B.DecisionCount == fine.B.DecisionCount && coarse.B.CurrentAction == fine.B.CurrentAction &&
            Math.Abs(coarse.C.X - fine.C.X) < 1e-6 && Math.Abs(coarse.C.Y - fine.C.Y) < 1e-6 && coarse.C.AnimationFrame == fine.C.AnimationFrame,
            "behavior: 90-second simulation independent of render update rate");
        var predominance = Create(); int resting = 0;
        for (int i = 0; i < 10000; i++) { Tick(predominance, 0.1); if (predominance.B.CurrentAction == BehaviorAction.Idle) resting++; }
        check(resting > 7500, "behavior: rest occupies more than 75 percent of seeded long simulation");

        var wander = Create(Forced(BehaviorAction.Wander)); Tick(wander, 0.3);
        check(wander.B.CurrentAction == BehaviorAction.Wander && wander.C.State == SmileyState.Walking && wander.C.IsMoving, "behavior: wander requests existing walking controller");
        Tick(wander, 0.4); double x = wander.C.X; int decisions = wander.B.DecisionCount; Tick(wander, 0.2);
        check(wander.C.X == x && wander.B.CurrentAction == BehaviorAction.Idle && wander.B.DecisionCount == decisions, "behavior: completed movement is followed by mandatory rest");
        var running = Create(Forced(BehaviorAction.Run)); Tick(running, 0.4);
        check(running.C.State == SmileyState.Running && Math.Abs(Math.Abs(running.C.X) - 14) < 1e-7, "behavior: spontaneous run uses existing speed and finite duration");
        var hop = Create(Forced(BehaviorAction.Hop)); Tick(hop, 0.3);
        check(hop.B.CurrentAction == BehaviorAction.Hop && hop.C.PhysicalState == PhysicalState.Rising && !hop.C.TryJump(), "behavior: automatic hop reuses ballistic jump without double jump");
        Tick(hop, 1.4);
        check(hop.C.IsGrounded && hop.B.CurrentAction == BehaviorAction.Idle, "behavior: hop cooldown prevents repeated rapid automatic jumps");
        var shallowC = new SmileyController(); var shallowArea = new WorkArea(-2000, 800, 2000, 1000);
        shallowC.MoveTo(0, Ground, shallowArea, Width, Height);
        var shallowB = new SmileyBehaviorController(shallowC, Forced(BehaviorAction.Hop)); shallowB.Tick(0.3, shallowArea, Width, Height);
        check(shallowC.IsGrounded && shallowB.CurrentAction == BehaviorAction.Idle, "behavior: automatic hop declined when there is insufficient headroom");

        var curious = Create(Forced(BehaviorAction.Wander)); Tick(curious, 0.3);
        int facing = curious.C.Direction;
        var close = new CursorPosition(curious.C.X + Width + 80, Ground + 95);
        Tick(curious, 0.1, close); x = curious.C.X;
        check(curious.B.CurrentAction == BehaviorAction.Curious && curious.C.Pose == Pose.Sceptical && !curious.C.IsMoving,
            "behavior: close cursor interrupts wander with existing sceptical pose");
        Tick(curious, 0.2, close);
        check(curious.C.IsMoving && curious.C.Direction == facing && curious.C.X == x, "behavior: curiosity resumes interrupted movement without repositioning or reversing");
        var flee = Create(Encounters(), new AlwaysZeroRandom()); EnterFromLeft(flee);
        check(flee.B.CurrentAction == BehaviorAction.Flee && flee.C.Direction == 1 && flee.C.State == SmileyState.Running && flee.B.Cursor is { Side: -1, Approaching: true },
            "behavior: eligible approaching cursor on left can trigger short escape to right");
        Tick(flee, 0.4);
        Tick(flee, 0.1, new(flee.C.X - 200, Ground + 95)); Tick(flee, 0.1, new(flee.C.X - 80, Ground + 95));
        check(flee.B.CurrentAction == BehaviorAction.Curious, "behavior: eight-second flee cooldown blocks a second eligible escape");
        var right = Create(Encounters(), new AlwaysZeroRandom());
        Tick(right, 0.1, new(Width + 200, Ground + 95)); Tick(right, 0.1, new(Width + 80, Ground + 95));
        check(right.B.CurrentAction == BehaviorAction.Flee && right.C.Direction == -1, "behavior: right-side approach chooses escape to left");
        var edge = Create(Encounters(), new AlwaysZeroRandom(), Area.Right - Width - 5); EnterFromLeft(edge); Tick(edge, 0.2);
        check(edge.C.X <= Area.Right - Width && edge.B.CurrentAction != BehaviorAction.Flee, "behavior: escape declined when moving away would hit monitor boundary");
        var clickZone = Create(Encounters(), new AlwaysZeroRandom());
        Tick(clickZone, 0.1, new(-200, Ground + 95)); Tick(clickZone, 0.1, new(-5, Ground + 95));
        check(clickZone.B.CurrentAction == BehaviorAction.Curious && clickZone.B.Cursor is { InsideClickZone: true }, "behavior: close click/drag zone never triggers fleeing");
        var cooldown = Create(Encounters());
        Tick(cooldown, 0.1, new(-100, Ground + 95)); Tick(cooldown, 0.2, new(-100, Ground + 95));
        Tick(cooldown, 0.1, new(-200, Ground + 95)); Tick(cooldown, 0.1, new(-100, Ground + 95));
        check(cooldown.B.CurrentAction == BehaviorAction.Idle, "behavior: cursor reaction cooldown suppresses repeated curiosity");
        Tick(cooldown, 1, new(-100, Ground + 95));
        check(cooldown.B.CurrentAction == BehaviorAction.Idle, "behavior: stationary nearby cursor does not retrigger encounters continuously");
        Tick(cooldown, 0.1, new(-200, Ground + 95)); Tick(cooldown, 0.1, new(-110, Ground + 95)); Tick(cooldown, 0.1, new(-119, Ground + 95));
        check(cooldown.B.Cursor is { Side: -1, Approaching: false }, "behavior: retreating cursor is distinguished from approaching cursor");

        var manual = Create(Forced(BehaviorAction.Wander)); Tick(manual, 0.3);
        manual.C.BeginDrag(); manual.B.NotifyManualInteraction(); x = manual.C.X; Tick(manual, 2, close);
        check(manual.C.State == SmileyState.Dragging && manual.C.X == x, "behavior: direct drag interrupts autonomy and suspends decisions");
        manual.C.MoveTo(0, 200, Area, Width, Height); manual.C.EndDrag(500, -300); manual.B.NotifyManualInteraction();
        Tick(manual, 0.2, close);
        check(Math.Abs(manual.C.X - 100) < 1e-7 && Math.Abs(manual.C.Y - 162) < 1e-7 && manual.C.VelocityX == 500,
            "behavior: autonomy does not interfere with manual airborne throw");
        manual.C.React(); manual.B.NotifyManualInteraction(); Tick(manual, 0.1, close);
        check(manual.C.State == SmileyState.Reacting && manual.C.VelocityX == 500, "behavior: direct click overrides cursor reactions while gravity and throw continue");
        var quiet = Create(Forced(BehaviorAction.Wander)); quiet.B.NotifyManualInteraction(); Tick(quiet, 1.9);
        check(quiet.B.DecisionCount == 0 && !quiet.C.IsMoving, "behavior: manual interaction grants at least two seconds of quiet");
        var pauseA = Create(Forced(BehaviorAction.Wander)); var pauseB = Create(Forced(BehaviorAction.Wander));
        Tick(pauseA, 0.4); pauseA.C.SetPaused(true); Tick(pauseA, 100); pauseA.C.SetPaused(false); Tick(pauseA, 1);
        Tick(pauseB, 1.4);
        check(Math.Abs(pauseA.C.X - pauseB.C.X) < 1e-7 && pauseA.B.CurrentAction == pauseB.B.CurrentAction && pauseA.B.DecisionCount == pauseB.B.DecisionCount,
            "behavior: global pause freezes both decision timeline and movement");
        var off = Create(Forced(BehaviorAction.Wander)); Tick(off, 0.3); off.B.SetEnabled(false); Tick(off, 1); x = off.C.X; Tick(off, 100);
        check(off.C.IsGrounded && off.C.State == SmileyState.Idle && off.C.X == x && off.B.DecisionCount == 1, "behavior: disabling autonomy lets finite action finish then stays quiet");
        off.C.TryJump(); Tick(off, 0.2);
        check(!off.C.IsGrounded && off.C.PhysicalState == PhysicalState.Rising, "behavior: manual jump and physics remain active with autonomy disabled");
        Tick(off, 2); off.C.React(); Tick(off, 0.1);
        check(off.C.State == SmileyState.Reacting, "behavior: click reaction remains available with autonomy disabled");
        bool preferences = true;
        foreach (var pref in new[] { MovementPreference.Walking, MovementPreference.Running })
        {
            var selected = Create(Forced(BehaviorAction.Hop)); selected.B.SetPreference(pref); Tick(selected, 0.3);
            preferences &= selected.C.IsGrounded && selected.C.State == (pref == MovementPreference.Walking ? SmileyState.Walking : SmileyState.Running);
        }
        check(preferences, "behavior: manual modes preserve walk/run preference and do not add spontaneous hops");
        var scaledC = new SmileyController(); var scaledArea = new WorkArea(-4000, -200, 0, 2000);
        scaledC.MoveTo(-2000, 1620, scaledArea, 420, 380);
        var scaledB = new SmileyBehaviorController(scaledC, Encounters()); scaledB.Tick(0.1, scaledArea, 420, 380, 2, 2, new(-2200, 1810));
        check(scaledB.Cursor is { DistanceDips: 100, Side: -1 } && scaledB.CurrentAction == BehaviorAction.Curious,
            "behavior: cursor distance uses DIPs on negative-coordinate 200-percent-DPI monitor");
        var sampling = Create(); int polls = 0;
        for (int i = 0; i < 60; i++) { if (sampling.B.NeedsCursorSample(1.0 / 60)) polls++; Tick(sampling, 1.0 / 60); }
        sampling.B.SetEnabled(false);
        check(polls == 10 && !sampling.B.NeedsCursorSample(1), "behavior: perception requests only ten cursor polls per second and none when disabled");
        bool invalid = false; try { _ = Create(new SmileyBehaviorOptions { FleeProbability = 0.5 }); } catch (ArgumentOutOfRangeException) { invalid = true; }
        check(invalid, "behavior: excessive flee probability rejected");
        var window = new MainWindow(startPaused: true);
        try
        {
            window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            bool automatic = window.AutoItem.IsChecked && !window.WalkItem.IsChecked && !window.RunItem.IsChecked;
            window.RunItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            bool runningSelected = window.RunItem.IsChecked && !window.AutoItem.IsChecked && !window.WalkItem.IsChecked;
            window.WalkItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            bool walkingSelected = window.WalkItem.IsChecked && !window.AutoItem.IsChecked && !window.RunItem.IsChecked;
            window.AutoItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(automatic && runningSelected && walkingSelected && window.AutoItem.IsChecked && !window.RunItem.IsChecked && !window.WalkItem.IsChecked,
                "behavior UI: Automatic, Walk and Run menu selections are mutually exclusive");
            window.AutonomyItem.IsChecked = false;
            window.AutonomyItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.PauseItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.Character.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            var neutral = window.Character.Source;
            bool offButJumpEnabled = !window.AutonomyItem.IsChecked && window.JumpItem.IsEnabled;
            window.JumpItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(offButJumpEnabled && !ReferenceEquals(neutral, window.Character.Source), "behavior UI: autonomy switch does not disable manual Pular command");
            window.Character.ContextMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        }
        finally { window.Close(); }
    }
    private sealed class CountingRandom : Random
    {
        public int Calls { get; private set; }
        public override double NextDouble() { Calls++; return base.NextDouble(); }
    }
    private sealed class AlwaysZeroRandom : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int maxValue) => 0;
    }
}
