using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Smiley;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAIL: {name}");
        Console.WriteLine($"PASS: {name}"); passed++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--platform-fixture")) return PlatformFixture.Run();
        try
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
            var bounds = new WorkArea(-1000, 20, 1000, 900);
            var options = new SmileyOptions { IdleMin = 1, IdleMax = 1, WalkMin = 3, WalkMax = 3 };
            SmileyController Create() { var c = new SmileyController(options, new Random(42)); c.MoveTo(0, 800, bounds, 100, 100); return c; }
            var c = Create();
            Check(c.State == SmileyState.Idle, "initial idle");
            c.Tick(1, bounds, 100, 100);
            Check(c.State == SmileyState.Walking && c.Pose == Pose.Running, "idle transitions to walking");
            double x = c.X; c.Tick(0.5, bounds, 100, 100);
            Check(Math.Abs(Math.Abs(c.X - x) - 32.5) < 0.001, "time-based speed");
            var a = Create(); var b = Create();
            a.Tick(2.5, bounds, 100, 100);
            for (int i = 0; i < 250; i++) b.Tick(0.01, bounds, 100, 100);
            Check(Math.Abs(a.X - b.X) < 0.001 && a.State == b.State, "equivalent movement across frame rates and transition");
            c.SetPaused(true); x = c.X; c.Tick(100, bounds, 100, 100);
            Check(c.X == x && c.State == SmileyState.Walking, "pause freezes and preserves autonomy state");
            c.React(); Check(c.State == SmileyState.Reacting, "click reaction while paused");
            c.Tick(2, bounds, 100, 100); Check(c.State == SmileyState.Reacting && c.Paused, "global pause also freezes reaction timer");
            c.SetPaused(false); c.Tick(2, bounds, 100, 100); c.SetPaused(true);
            Check(c.State == SmileyState.Idle, "reaction expires after global resume");
            c.BeginDrag(); c.Tick(100, bounds, 100, 100);
            Check(c.State == SmileyState.Dragging && c.X == x, "drag suspends autonomy");
            c.MoveTo(99999, -99999, bounds, 100, 100);
            Check(c.X == 900 && c.Y == 20, "drag clamps both axes including negative screen coordinates");
            c.EndDrag(); Check(c.X == 900 && c.Y == 20 && c.State == SmileyState.Idle, "release retains position");
            c.SetPaused(false); c.Tick(100, bounds, 100, 100);
            Check(c.X >= -1000 && c.X <= 900 && c.Y == 800 && c.IsGrounded, "long simulation lands and remains inside work area");
            c.MoveTo(0, 0, new(10, 10, 30, 30), 100, 100);
            Check(c.X == 10 && c.Y == -70 && c.IsGrounded, "work area smaller than sprite keeps feet on floor without adding ceiling collision");
            var warnings = new List<string>();
            foreach (string path in Directory.GetFiles(Path.Combine(root, "SmileyImagens"), "*.png"))
            {
                var image = SpriteManager.Load(path, warnings);
                Check(image.IsFrozen && image.PixelWidth > 0, $"decode and alpha audit: {Path.GetFileName(path)}");
            }
            Check(warnings.Count == 0, "all current source PNGs have fully transparent pixels");
            var sprites = new SpriteManager();
            Check(sprites.Warnings.Count == 0, "runtime sprite loader reports no opaque backgrounds");
            Check(Enum.GetValues<Pose>().All(p => sprites[p].IsFrozen), "all five poses resolve from output directory");
            TestAnimation(sprites);
            TestSpriteSheets(root);
            PhysicsChecks.Run(Check, sprites, root);
            ThrowChecks.Run(Check, sprites);
            BehaviorChecks.Run(Check);
            WindowTrackerChecks.Run(Check);
            PlatformChecks.Run(Check);
            SupportTrackingChecks.Run(Check);
            NavigationChecks.Run(Check);
            byte[] synthetic = [0,0,0,0, 255,255,255,255, 0,0,0,0, 255,255,255,255, 0,0,0,0, 0,0,0,0];
            var bitmap = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, synthetic, 12);
            var mask = NativeMethods.BuildMask(bitmap, 3, 2, false);
            Check(!mask[0] && mask[1] && !mask[2] && mask[3], "alpha holes excluded from native hit region");
            var mirror = NativeMethods.BuildMask(bitmap, 3, 2, true);
            Check(!mirror[3] && mirror[5], "mirrored hit region follows mirrored image");
            var letterbox = NativeMethods.BuildMask(bitmap, 6, 6, false);
            Check(!letterbox.Take(12).Any(v => v) && letterbox.Any(v => v), "uniform bottom-aligned letterbox stays click-through");
            // Create two real HWNDs in this test process, without injecting mouse input.
            var behind = new Window { Left = 10, Top = 10, Width = 120, Height = 120, WindowStyle = WindowStyle.None, Background = Brushes.White, ShowInTaskbar = false };
            behind.Show();
            var front = new Window { Left = 10, Top = 10, Width = 120, Height = 120, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true };
            front.Show();
            var handle = new System.Windows.Interop.WindowInteropHelper(front).Handle;
            NativeMethods.ApplyRegion(handle, bitmap, false);
            Check(GetWindowRgn(handle, bitmap), "native HWND region matches alpha mask including holes");
            using (var cache = new NativeMethods.RegionCache())
            {
                var frame = sprites.GetMovementFrame(MovementMode.Walking, 0);
                NativeMethods.ApplyRegion(handle, frame, false, cache);
                NativeMethods.ApplyRegion(handle, frame, false, cache);
                Check(cache.Count == 1 && GetWindowRgn(handle, frame), "native region cache reuses same frame and DPI");
                NativeMethods.ApplyRegion(handle, frame, true, cache);
                Check(cache.Count == 2 && GetWindowRgn(handle, frame, true), "native cache separates mirrored orientation");
                var next = sprites.GetMovementFrame(MovementMode.Walking, 1);
                NativeMethods.ApplyRegion(handle, next, true, cache);
                Check(cache.Count == 3 && GetWindowRgn(handle, next, true), "native cache separates animation frames");
                var scaled = cache.CopyRegion(frame, 315, 285, false); DeleteObject(scaled);
                Check(cache.Count == 4, "native cache separates DPI dimensions");
                cache.Dispose(); Check(cache.Count == 0, "cached GDI resources released on close");
            }
            front.Close(); behind.Close();
            using var hashes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "original-image-hashes.json")));
            foreach (var item in hashes.RootElement.EnumerateArray())
            {
                string path = Path.Combine(root, "SmileyImagens", Path.GetFileName(item.GetProperty("Path").GetString()!));
                string expected = item.GetProperty("Hash").GetString()!;
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == expected, $"original unchanged: {Path.GetFileName(path)}");
            }
            using var newHashes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "sprite-sheet-source-hashes.json")));
            Check(newHashes.RootElement.EnumerateArray().All(item =>
            {
                string path = Path.Combine(root, "SmileyImagens", Path.GetFileName(item.GetProperty("Path").GetString()!));
                return ImageIntegrity.MatchesHistorical(path, item.GetProperty("Hash").GetString()!);
            }),
                "remaining historical source PNGs unchanged; retired Caminhada_2 explicitly reported");
            using var currentHashes = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mark-iic-image-hashes.json")));
            Check(currentHashes.RootElement.EnumerateArray().All(item =>
                File.Exists(Path.Combine(root, item.GetProperty("Path").GetString()!)) &&
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, item.GetProperty("Path").GetString()!)))) == item.GetProperty("Hash").GetString()),
                "all PNGs in Mark II.C snapshot remain present and byte-identical");
            using var preservedCode = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "mark-iiia-preserved-code-hashes.json")));
            Check(preservedCode.RootElement.EnumerateArray().Where(item => item.GetProperty("File").GetString() is "SpriteManager.cs" or "DragVelocityTracker.cs").All(item =>
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, item.GetProperty("File").GetString()!)))) == item.GetProperty("Hash").GetString()),
                "Mark III.C preserves throw estimator and sprites byte-identically; behavior and physics are covered by regression tests");
            Console.WriteLine($"{passed} checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void TestAnimation(SpriteManager sprites)
    {
        var area = new WorkArea(-100000, 0, 100000, 1000);
        var options = new SmileyOptions { IdleMin = 1, IdleMax = 1, WalkMin = 100, WalkMax = 100 };
        SmileyController Create(MovementMode mode = MovementMode.Walking)
        {
            var c = new SmileyController(options, new Random(13));
            c.MoveTo(0, 810, area, 210, 190); c.SetMovementMode(mode); c.Tick(1, area, 210, 190); return c;
        }
        var walk = Create();
        Check(walk.State == SmileyState.Walking && walk.AnimationFrame == 0, "walk starts at frame 1");
        walk.Tick(0.249, area, 210, 190);
        Check(walk.AnimationFrame == 0, "walking frame lasts 250ms at 4fps");
        walk.Tick(0.001, area, 210, 190);
        Check(walk.AnimationFrame == 1, "walk frame order 1 then 2");
        walk.Tick(0.25, area, 210, 190);
        Check(walk.AnimationFrame == 2, "walking advances to frame 3 after 500ms");
        Check(Math.Abs(Math.Abs(walk.X) - 32.5) < 1e-7, "walking 65 DIPs per second");
        walk.Tick(0.125, area, 210, 190); int frameBefore = walk.AnimationFrame;
        walk.SetMovementMode(MovementMode.Walking); walk.Tick(0.125, area, 210, 190);
        Check(walk.AnimationFrame != frameBefore, "selecting active mode does not reset sequence");
        double x = walk.X, y = walk.Y; int direction = walk.Direction;
        walk.SetMovementMode(MovementMode.Running);
        Check(walk.State == SmileyState.Running && walk.AnimationFrame == 0 && walk.X == x && walk.Y == y && walk.Direction == direction,
            "switch to run resets cycle once without moving anchor or facing");
        walk.Tick(0.125, area, 210, 190); Check(walk.AnimationFrame == 1, "running frame 2 after 125ms");
        walk.Tick(0.125, area, 210, 190); Check(walk.AnimationFrame == 2, "running frame 3 after 250ms");
        walk.Tick(0.125, area, 210, 190); Check(walk.AnimationFrame == 3, "running advances to frame 4 after 375ms");
        Check(Math.Abs(Math.Abs(walk.X - x) - 52.5) < 1e-7, "running 140 DIPs per second");
        walk.Tick(0.625, area, 210, 190); Check(walk.AnimationFrame == 0, "eight running frames loop after one second at 8fps");
        var fullWalk = Create();
        for (int i = 1; i <= 8; i++) { fullWalk.Tick(0.25, area, 210, 190); Check(fullWalk.AnimationFrame == i % 8, $"walking frame order {i + 1} including full two-second cycle"); }
        var fullRun = Create(MovementMode.Running);
        for (int i = 1; i <= 8; i++) { fullRun.Tick(0.125, area, 210, 190); Check(fullRun.AnimationFrame == i % 8, $"running frame order {i + 1} including full one-second cycle"); }
        walk.SetMovementMode(MovementMode.Walking);
        Check(walk.State == SmileyState.Walking && walk.AnimationFrame == 0, "switch back to walking");
        foreach (var mode in Enum.GetValues<MovementMode>())
        {
            var a = Create(mode); var b = Create(mode);
            a.Tick(3.1875, area, 210, 190);
            for (int i = 0; i < 102; i++) b.Tick(0.03125, area, 210, 190);
            Check(a.AnimationFrame == b.AnimationFrame && Math.Abs(a.X - b.X) < 1e-7, $"{mode}: animation and travel independent of update rate");
        }
        var run = Create(MovementMode.Running);
        run.Tick(0.5, area, 210, 190, 1.5);
        Check(Math.Abs(Math.Abs(run.X) - 105) < 1e-7, "run speed scales with 150 percent DPI");
        x = run.X; run.React(); run.Tick(0.5, area, 210, 190);
        Check(run.State == SmileyState.Reacting && run.X == x && run.AnimationFrame == 0, "reaction interrupts run animation and movement");
        run.Tick(0.6, area, 210, 190);
        Check(run.State == SmileyState.Idle && run.Pose == Pose.Neutral, "reaction returns to neutral");
        run.Tick(1, area, 210, 190); run.BeginDrag(); x = run.X; run.Tick(50, area, 210, 190);
        Check(run.State == SmileyState.Dragging && run.X == x && run.AnimationFrame == 0, "drag interrupts running cycle");
        run.EndDrag(); Check(run.Pose == Pose.Neutral, "drag release returns to neutral");
        run.SetPaused(true); run.SetMovementMode(MovementMode.Walking); run.Tick(20, area, 210, 190);
        Check(run.Paused && run.State == SmileyState.Idle && run.X == x, "mode selection preserves pause");
        run.SetPaused(false); run.Tick(1, area, 210, 190);
        Check(run.State == SmileyState.Walking && run.AnimationFrame == 0, "resume uses selected movement mode");
        var timed = new SmileyController(new SmileyOptions { IdleMin = 1, IdleMax = 1, WalkMin = 1, WalkMax = 1 }, new Random(1));
        timed.MoveTo(0, 810, area, 210, 190); timed.SetMovementMode(MovementMode.Running); timed.Tick(2, area, 210, 190);
        Check(timed.State == SmileyState.Idle && timed.Pose == Pose.Neutral && timed.AnimationFrame == 0, "end of running interval returns to neutral and resets frame");
        var edge = Create(MovementMode.Running); var edgeArea = new WorkArea(-500, 0, 500, 1000);
        edge.MoveTo(edge.Direction > 0 ? 280 : -490, 810, edgeArea, 210, 190); edge.Tick(0.2, edgeArea, 210, 190);
        Check(edge.State == SmileyState.Idle && edge.Pose == Pose.Neutral && edge.X == (edge.Direction > 0 ? 290 : -500), "run stops at work area edge without walking in place");
        var custom = new SmileyController(new SmileyOptions { IdleMin = 1, IdleMax = 1, WalkMin = 100, WalkMax = 100, Speed = 50, RunSpeed = 100, WalkFramesPerSecond = 2, RunFramesPerSecond = 5 });
        custom.MoveTo(0, 810, area, 210, 190); custom.Tick(1.5, area, 210, 190);
        Check(custom.AnimationFrame == 1 && Math.Abs(Math.Abs(custom.X) - 25) < 1e-7, "custom walking speed and frame rate");
        custom.SetMovementMode(MovementMode.Running); x = custom.X; custom.Tick(0.4, area, 210, 190);
        Check(custom.AnimationFrame == 2 && Math.Abs(Math.Abs(custom.X - x) - 40) < 1e-7, "custom running speed and frame rate");
        bool invalid = false;
        try { _ = new SmileyController(new SmileyOptions { WalkFramesPerSecond = 0 }); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "invalid timing options rejected");

        var boxes = new List<Int32Rect>();
        foreach (var mode in Enum.GetValues<MovementMode>())
        for (int frame = 0; frame < SmileyController.FramesPerCycle; frame++)
        {
            var image = sprites.GetMovementFrame(mode, frame);
            Check(image.IsFrozen && ReferenceEquals(image, sprites.GetMovementFrame(mode, frame)), $"{mode} frame {frame + 1}: decoded image reused");
            Check(image.PixelWidth == 840 && image.PixelHeight == 760, $"{mode} frame {frame + 1}: common alignment canvas");
            foreach (int percent in new[] { 100, 125, 150, 200 })
            {
                int width = 210 * percent / 100, height = 190 * percent / 100;
                var normal = NativeMethods.BuildMask(image, width, height, false);
                var mirrored = NativeMethods.BuildMask(image, width, height, true);
                bool same = true;
                for (int yy = 0; yy < height; yy++)
                for (int xx = 0; xx < width; xx++) same &= normal[yy * width + xx] == mirrored[yy * width + width - xx - 1];
                Check(same && normal.Any(v => v) && !normal.Take(width).Any(v => v), $"{mode} frame {frame + 1}: mirrored anchor and alpha at {percent}% DPI");
                Check(normal.Skip((height - 2) * width).Any(v => v), $"{mode} frame {frame + 1}: visible contact within two physical pixels of floor at {percent}% DPI");
                if (percent == 100)
                {
                    int left = width, top = height, right = 0, bottom = 0;
                    for (int yy = 0; yy < height; yy++)
                    for (int xx = 0; xx < width; xx++)
                        if (normal[yy * width + xx]) { left = Math.Min(left, xx); top = Math.Min(top, yy); right = Math.Max(right, xx); bottom = Math.Max(bottom, yy); }
                    var box = new Int32Rect(left, top, right - left + 1, bottom - top + 1); boxes.Add(box);
                    Console.WriteLine($"ALIGN: {mode} {frame + 1}: {box}");
                }
            }
        }
        Check(boxes.All(b => b.Height >= 160 && b.Height <= 181), "whole-cell normalization keeps visual height between 160 and 181 DIPs");
        Check(boxes.All(b => b.Y >= 0 && b.Y + b.Height <= 190), "all sixteen frames stay within window bounds");
        Check(SpriteManager.WalkingAlignment.Concat(SpriteManager.RunningAlignment).Select(a => a.AnchorX).Distinct().Count() == 1,
            "body reference remains fixed across frames and cycles");
    }

    private static void TestSpriteSheets(string root)
    {
        foreach (string folder in new[] { "Corrida", "Caminhada" })
        {
            string directory = Path.Combine(root, "SmileyImagens", folder);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "frames.json")));
            string sourcePath = Path.Combine(root, "SmileyImagens", manifest.RootElement.GetProperty("Source").GetString()!);
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))) == manifest.RootElement.GetProperty("SourceSha256").GetString(), $"{folder}: original sheet unchanged");
            using var stream = File.OpenRead(sourcePath);
            var source = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(source.PixelWidth == 1774 && source.PixelHeight == 887 && source.Format == PixelFormats.Bgra32, $"{folder}: original RGBA grid dimensions");
            var pixels = new byte[1774 * 887 * 4]; source.CopyPixels(pixels, 1774 * 4, 0);
            int index = 0, covered = 0;
            foreach (var frame in manifest.RootElement.GetProperty("Frames").EnumerateArray())
            {
                string expectedName = $"frame_{++index:D2}.png";
                Check(frame.GetProperty("File").GetString() == expectedName, $"{folder}: export order {expectedName}");
                using var outputStream = File.OpenRead(Path.Combine(directory, expectedName));
                var bitmap = BitmapFrame.Create(outputStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Check(bitmap.PixelWidth == 444 && bitmap.PixelHeight == 444 && bitmap.Format == PixelFormats.Bgra32, $"{folder}/{expectedName}: 444x444 with alpha");
                var actual = new byte[444 * 444 * 4]; bitmap.CopyPixels(actual, 1776, 0);
                var expected = new byte[actual.Length];
                int x0 = frame.GetProperty("X").GetInt32(), y0 = frame.GetProperty("Y").GetInt32();
                int width = frame.GetProperty("Width").GetInt32(), height = frame.GetProperty("Height").GetInt32();
                int dx = frame.GetProperty("OffsetX").GetInt32(), dy = frame.GetProperty("OffsetY").GetInt32();
                int column = (index - 1) % 4, row = (index - 1) / 4;
                Check(x0 == (int)Math.Round(column * 1774.0 / 4, MidpointRounding.AwayFromZero) &&
                    y0 == (int)Math.Round(row * 887.0 / 2, MidpointRounding.AwayFromZero) &&
                    width == (int)Math.Round((column + 1) * 1774.0 / 4, MidpointRounding.AwayFromZero) - x0 &&
                    height == (int)Math.Round((row + 1) * 887.0 / 2, MidpointRounding.AwayFromZero) - y0,
                    $"{folder}/{expectedName}: exact proportional grid position");
                int visibleCount = 0;
                bool lost = false;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int sourceIndex = ((y0 + y) * 1774 + x0 + x) * 4;
                    int tx = x + dx, ty = y + dy;
                    if (pixels[sourceIndex + 3] != 0) visibleCount++;
                    if (tx < 0 || tx >= 444 || ty < 0 || ty >= 444) { lost |= pixels[sourceIndex + 3] != 0; continue; }
                    Array.Copy(pixels, sourceIndex, expected, (ty * 444 + tx) * 4, 4);
                }
                Check(!lost && actual.SequenceEqual(expected), $"{folder}/{expectedName}: exact RGBA copy, only integer translation and transparent padding");
                Check(visibleCount > 0 && actual.Where((_, i) => i % 4 == 3).Count(a => a != 0) == visibleCount, $"{folder}/{expectedName}: no silhouette pixels lost or duplicated");
                covered += width * height;
            }
            Check(index == 8 && covered == 1774 * 887, $"{folder}: proportional cells cover full source exactly once");
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int l, int t, int r, int b);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(nint region);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowRgn")] private static extern int ReadRegion(nint hwnd, nint region);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    private static bool GetWindowRgn(nint hwnd, BitmapSource bitmap, bool mirror = false)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (ReadRegion(hwnd, region) is not (2 or 3)) return false;
            NativeMethods.GetWindowRect(hwnd, out var rect);
            var expected = NativeMethods.BuildMask(bitmap, rect.Width, rect.Height, mirror);
            for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
                if (PtInRegion(region, x, y) != expected[y * rect.Width + x]) return false;
            return true;
        }
        finally { DeleteObject(region); }
    }
}
