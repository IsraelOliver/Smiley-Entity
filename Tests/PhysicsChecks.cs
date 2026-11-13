using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Smiley;

internal static class PhysicsChecks
{
    public static void Run(Action<bool, string> check, SpriteManager sprites, string root)
    {
        var area = new WorkArea(-1000, 20, 2000, 1000);
        var options = new SmileyOptions { IdleMin = 1000, IdleMax = 1000 };
        const double width = 210, height = 190, ground = 810;
        SmileyController Create(double y = ground)
        {
            var c = new SmileyController(options, new Random(17));
            c.MoveTo(0, y, area, width, height); return c;
        }
        var c = Create();
        check(c.IsGrounded && c.VelocityY == 0 && c.CanJump, "physics: initial grounded contact and zero vertical speed");
        check(c.TryJump() && c.VelocityY == -460 && c.PhysicalState == PhysicalState.Rising, "physics: jump begins with upward 460 DIPs/s");
        double x = c.X;
        check(!c.TryJump(), "physics: double jump rejected during ascent");
        c.Tick(0.1, area, width, height);
        check(Math.Abs(c.VelocityY + 350) < 1e-7 && Math.Abs(c.Y - (ground - 40.5)) < 1e-7, "physics: gravity accelerates by 110 DIPs/s in 100ms");
        check(c.X == x && c.VelocityX == 0 && !c.IsMoving && c.AnimationFrame == 0, "physics: vertical jump suspends horizontal animation and travel");
        double toApex = 460.0 / 1100;
        c.Tick(toApex - 0.1, area, width, height);
        double maximumHeight = 460.0 * 460 / (2 * 1100);
        check(Math.Abs((ground - c.Y) - maximumHeight) < 1e-7 && Math.Abs(c.VelocityY) < 1e-7, "physics: apex height is approximately 96.18 DIPs");
        c.Tick(0.01, area, width, height);
        check(c.PhysicalState == PhysicalState.Falling && c.VelocityY > 0, "physics: ascent transitions to descent");
        check(!c.TryJump(), "physics: double jump rejected during descent");
        c.Tick(5, area, width, height);
        check(c.Y == ground && c.IsGrounded && c.VelocityY == 0 && c.PhysicalState == PhysicalState.Grounded, "physics: long interval lands exactly without crossing floor");
        check(c.TryJump(), "physics: jump available again after landing");
        c = Create(300); check(!c.IsGrounded && c.PhysicalState == PhysicalState.Falling, "physics: elevated placement starts airborne");
        c.Tick(0.2, area, width, height);
        check(Math.Abs(c.Y - 322) < 1e-7 && Math.Abs(c.VelocityY - 220) < 1e-7, "physics: fall from rest uses constant acceleration");
        double y = c.Y, velocity = c.VelocityY;
        c.SetPaused(true); c.Tick(10, area, width, height);
        check(c.Y == y && c.VelocityY == velocity && !c.CanJump, "physics: pause freezes position and velocity during fall");
        c.SetPaused(false); c.Tick(0.1, area, width, height);
        check(Math.Abs(c.Y - (y + velocity * 0.1 + 5.5)) < 1e-7 && Math.Abs(c.VelocityY - (velocity + 110)) < 1e-7, "physics: resume preserves accumulated falling velocity");
        c.React(); y = c.Y; c.Tick(0.1, area, width, height);
        check(c.State == SmileyState.Reacting && c.Y > y && c.DisplayPose is Pose.Startled or Pose.Frowning, "physics: visual reaction does not suspend gravity");
        c.Tick(5, area, width, height);
        check(c.IsGrounded && c.State == SmileyState.Idle && c.Pose == Pose.Neutral, "physics: landing and reaction complete independently");

        c = Create(); c.TryJump(); c.Tick(0.1, area, width, height);
        c.BeginDrag(); y = c.Y; c.Tick(10, area, width, height);
        check(c.Y == y && c.VelocityY == 0 && c.PhysicalState == PhysicalState.Suspended && !c.CanJump, "physics: capture drag suspends gravity and clears momentum");
        c.MoveTo(123, 200, area, width, height);
        c.EndDrag();
        check(c.X == 123 && c.Y == 200 && c.VelocityY == 0 && !c.IsGrounded, "physics: drop retains pointer position and starts with zero velocity");
        c.Tick(0.1, area, width, height);
        check(c.X == 123 && Math.Abs(c.Y - 205.5) < 1e-7, "physics: gravity resumes on next tick without mouse throw");
        c.Tick(100, area, width, height);
        check(c.Y == ground && c.VelocityY == 0, "physics: drop lands even after very long update");
        c.BeginDrag(); c.MoveTo(100, ground, area, width, height); c.EndDrag(); c.Tick(0.1, area, width, height);
        check(c.IsGrounded && c.Y == ground, "physics: releasing directly on floor stays grounded");

        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            double w = width * scale, h = height * scale;
            var scaledArea = new WorkArea(-1000 * scale, 20 * scale, 2000 * scale, 1000 * scale);
            double floor = scaledArea.Bottom - h;
            var a = new SmileyController(options); a.MoveTo(0, floor, scaledArea, w, h); a.TryJump(scale);
            a.Tick(toApex, scaledArea, w, h, scale);
            check(Math.Abs((floor - a.Y) / scale - maximumHeight) < 1e-7, $"physics: same DIP jump height at {scale * 100}% DPI");
            a.Tick(2, scaledArea, w, h, scale);
            check(a.IsGrounded && a.Y + h == scaledArea.Bottom, $"physics: physical floor contact at {scale * 100}% DPI");
            var b = new SmileyController(options); b.MoveTo(0, 200 * scale, scaledArea, w, h);
            b.Tick(0.2, scaledArea, w, h, scale);
            check(Math.Abs((b.Y / scale) - 222) < 1e-7 && Math.Abs(b.VelocityY / scale - 220) < 1e-7, $"physics: DIP acceleration converted exactly at {scale * 100}% DPI");
        }
        foreach (double dt in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 0.137 })
        {
            var a = Create(); var b = Create(); a.TryJump(); b.TryJump();
            const double total = 0.7;
            a.Tick(total, area, width, height);
            double elapsed = 0;
            while (elapsed < total - 1e-10) { double step = Math.Min(dt, total - elapsed); b.Tick(step, area, width, height); elapsed += step; }
            check(Math.Abs(a.Y - b.Y) < 1e-7 && Math.Abs(a.VelocityY - b.VelocityY) < 1e-7, $"physics: frame-rate independent ballistic integration dt={dt:F6}");
        }
        foreach (var mode in Enum.GetValues<MovementMode>())
        {
            var movementOptions = new SmileyOptions { IdleMin = 1, IdleMax = 1, WalkMin = 100, WalkMax = 100 };
            var a = new SmileyController(movementOptions, new Random(22));
            a.MoveTo(0, ground, area, width, height); a.SetMovementMode(mode); a.Tick(1, area, width, height);
            a.Tick(0.125, area, width, height); x = a.X; int facing = a.Direction;
            a.TryJump();
            check(!a.IsMoving && a.DisplayPose == Pose.Startled && a.Direction == facing, $"physics: {mode} jump keeps facing and uses existing airborne pose");
            a.Tick(2 * toApex, area, width, height);
            check(a.IsGrounded && a.IsMoving && a.AnimationFrame == 0 && a.X == x, $"physics: {mode} resumes ground state at exact landing");
            a.Tick(0.25, area, width, height);
            check(a.AnimationFrame == (mode == MovementMode.Running ? 2 : 1) && a.X != x, $"physics: {mode} eight-frame cycle resumes after landing");
            var b = new SmileyController(movementOptions, new Random(22));
            b.MoveTo(0, ground, area, width, height); b.SetMovementMode(mode); b.Tick(1.125, area, width, height); b.TryJump();
            b.Tick(2 * toApex + 0.25, area, width, height);
            check(Math.Abs(a.X - b.X) < 1e-7 && a.AnimationFrame == b.AnimationFrame && a.Y == b.Y,
                $"physics: {mode} long tick consumes post-landing time consistently");
        }
        c = Create(); c.MoveTo(99999, 20, area, width, height); c.Tick(0.1, area, width, height);
        check(c.X == area.Right - width && c.Y >= area.Top, "physics: airborne horizontal limit on right");
        c.MoveTo(-99999, 20, area, width, height); c.Tick(0.1, area, width, height);
        check(c.X == area.Left, "physics: airborne horizontal limit on negative-coordinate monitor");
        c = Create(); c.UpdateEnvironment(new(-1000, 20, 2000, 900), width, height);
        check(c.Y == 710 && c.IsGrounded, "physics: grounded contact follows taskbar/work-area resize");
        c = Create(200); c.Tick(0.1, area, width, height); velocity = c.VelocityY;
        c.UpdateEnvironment(new(-1000, 20, 2000, 950), width, height);
        check(c.VelocityY == velocity && !c.IsGrounded && c.Y == 205.5, "physics: display change preserves airborne momentum");
        c = Create(); c.MoveTo(0, 900, area, width, height, footOffset: 170);
        check(c.Y == 830 && c.IsGrounded, "physics: explicit foot reference overrides file/window bottom");
        c = new SmileyController(new SmileyOptions { Gravity = 800, JumpSpeed = 400, IdleMin = 1000, IdleMax = 1000 });
        c.MoveTo(0, ground, area, width, height); c.TryJump(); c.Tick(0.5, area, width, height);
        check(Math.Abs(c.Y - (ground - 100)) < 1e-7, "physics: configurable gravity and jump impulse");
        foreach (var invalid in new[] { new SmileyOptions { Gravity = 0 }, new SmileyOptions { JumpSpeed = double.NaN } })
        {
            bool rejected = false; try { _ = new SmileyController(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "physics: non-positive/non-finite physical settings rejected");
        }
        foreach (var mode in Enum.GetValues<MovementMode>())
        for (int frame = 0; frame < SmileyController.FramesPerCycle; frame++)
        {
            var bitmap = sprites.GetMovementFrame(mode, frame);
            check(Math.Abs(SpriteManager.FindFootLine(bitmap) / 4.0 - SpriteManager.GroundLine) <= 0.5,
                $"physics: {mode} frame {frame + 1} visible feet aligned to common ground within 0.5 DIP");
        }
        using var hashes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mark-iib-image-hashes.json")));
        check(hashes.RootElement.EnumerateArray().All(item =>
            ImageIntegrity.MatchesHistorical(Path.Combine(root, item.GetProperty("Path").GetString()!), item.GetProperty("Hash").GetString()!)),
            "physics: historical PNG hashes preserved except explicitly retired unused Caminhada_2");
        var window = new MainWindow(startPaused: true);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var menu = window.Character.ContextMenu;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            check(!window.JumpItem.IsEnabled, "physics UI: Pular disabled during global pause");
            window.PauseItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            check(window.JumpItem.IsEnabled, "physics UI: Pular enabled after resume on floor");
            var neutral = window.Character.Source;
            window.JumpItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(!ReferenceEquals(neutral, window.Character.Source), "physics UI: actual Pular menu handler selects airborne pose");
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            check(!window.JumpItem.IsEnabled, "physics UI: actual menu rejects second airborne jump");
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        }
        finally { window.Close(); }
    }
}
