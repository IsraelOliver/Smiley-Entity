using Smiley;

internal static class ThrowChecks
{
    internal static void Run(Action<bool, string> check, SpriteManager sprites)
    {
        var area = new WorkArea(-2000, 0, 2000, 1000);
        SmileyController Create(double y = 200)
        {
            var c = new SmileyController(new SmileyOptions { IdleMin = 1000, IdleMax = 1000 });
            c.BeginDrag(); c.MoveTo(0, y, area, 210, 190); return c;
        }
        var tracker = new DragVelocityTracker();
        tracker.Reset(0, 0, 0); tracker.Add(0.05, 40, -20);
        var velocity = tracker.Release(0.10, 80, -40);
        check(Math.Abs(velocity.X - 800) < 1e-7 && Math.Abs(velocity.Y + 400) < 1e-7, "throw: recent drag estimates physical velocity in both axes");
        tracker.Reset(0, 0, 0); tracker.Add(0.04, 100, 0); velocity = tracker.Release(0.25, 100, 0);
        check(velocity == (0, 0), "throw: holding still before release removes stale impulse");
        tracker.Reset(0, 0, 0); velocity = tracker.Release(0.001, 50, 0);
        check(velocity == (0, 0), "throw: tiny time intervals cannot create spurious impulse");
        tracker.Reset(0, 0, 0); tracker.Add(0.1, 100, 0); tracker.Add(0.2, 110, 0); tracker.Add(0.25, 120, 0); velocity = tracker.Release(0.3, 130, 0);
        check(Math.Abs(velocity.X - 200) < 1e-7, "throw: uses recent motion instead of entire drag");
        var c = Create(); c.EndDrag(500, -300); c.Tick(0.1, area, 210, 190);
        check(Math.Abs(c.X - 50) < 1e-7 && Math.Abs(c.Y - 175.5) < 1e-7 && c.PhysicalState == PhysicalState.Rising, "throw: release follows ballistic arc with horizontal momentum");
        check(c.DisplayPose == Pose.Startled, "throw: ascent retains existing jump pose");
        c.Tick(0.3, area, 210, 190);
        check(c.DisplayPose == Pose.Falling && c.PhysicalState == PhysicalState.Falling, "throw: descent uses supplied falling PNG");
        check(sprites[Pose.Falling].IsFrozen, "throw: falling sprite decoded and cached");
        c.React(); double x = c.X; c.Tick(0.1, area, 210, 190);
        check(c.X > x && c.State == SmileyState.Reacting, "throw: reacting preserves airborne horizontal momentum");
        c.SetPaused(true); x = c.X; double y = c.Y, vx = c.VelocityX, vy = c.VelocityY;
        c.Tick(10, area, 210, 190);
        check(c.X == x && c.Y == y && c.VelocityX == vx && c.VelocityY == vy, "throw: global pause freezes full trajectory");
        c.SetPaused(false); c.Tick(5, area, 210, 190);
        check(c.IsGrounded && c.Y == 810 && c.VelocityX == 0 && c.VelocityY == 0, "throw: landing absorbs momentum and restores ground behaviour");
        c = Create(); c.EndDrag(100, 0); c.Tick(0.1, area, 210, 190);
        check(c.X == 0 && Math.Abs(c.Y - 205.5) < 1e-7, "throw: gentle drag retains ordinary vertical drop");
        c = Create(); c.EndDrag(100000, -100000);
        check(Math.Abs(Math.Sqrt(c.VelocityX * c.VelocityX + c.VelocityY * c.VelocityY) - 1100) < 1e-7, "throw: impulse capped to configurable maximum magnitude");
        c = Create(); c.MoveTo(1780, 200, area, 210, 190); c.EndDrag(1000, 0); c.Tick(0.2, area, 210, 190);
        check(c.X == 1790 && c.VelocityX == 0 && c.VelocityY > 0 && c.Y > 200, "throw: wall absorbs horizontal velocity while gravity continues");
        c = Create(); c.EndDrag(-500, 0); c.Tick(0.1, area, 210, 190);
        check(c.X == -50 && c.Direction == -1, "throw: left fling preserves negative screen coordinates and facing");
        var a = Create(); var b = Create(); a.EndDrag(500, -300); b.EndDrag(500, -300);
        a.Tick(0.7, area, 210, 190); for (int i = 0; i < 70; i++) b.Tick(0.01, area, 210, 190);
        check(Math.Abs(a.X - b.X) < 1e-7 && Math.Abs(a.Y - b.Y) < 1e-7, "throw: trajectory independent of update rate");
        c = Create(); c.EndDrag(1000, -600, 2, 2); c.Tick(0.1, area, 210, 190, 2);
        check(Math.Abs(c.X - 100) < 1e-7 && Math.Abs(c.Y - 151) < 1e-7, "throw: physical mouse velocity converted correctly at 200 percent DPI");
        c = Create(); c.EndDrag(double.NaN, 300); c.Tick(0.1, area, 210, 190);
        check(c.X == 0 && double.IsFinite(c.Y), "throw: invalid mouse velocity falls back to safe drop");
        c = Create(810); c.EndDrag(300, -300);
        check(!c.IsGrounded && c.PhysicalState == PhysicalState.Rising, "throw: upward release from floor can lift off");
        c = Create(810); c.EndDrag(300, 200); c.Tick(0.1, area, 210, 190);
        check(c.IsGrounded && c.X == 0 && c.Y == 810, "throw: downward release into floor is absorbed");
    }
}
