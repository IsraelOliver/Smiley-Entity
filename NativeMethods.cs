using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Smiley;

internal static class NativeMethods
{
    internal sealed class RegionCache : IDisposable
    {
        private readonly Dictionary<(BitmapSource Bitmap, int Width, int Height, bool Mirror), nint> regions = new();
        internal int Count => regions.Count;
        private bool disposed;

        internal nint CopyRegion(BitmapSource bitmap, int width, int height, bool mirror)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var key = (bitmap, width, height, mirror);
            if (!regions.TryGetValue(key, out nint original))
            {
                // Bound memory/GDI usage when a window visits many DPI configurations.
                if (regions.Count >= 64) Clear();
                original = BuildRegion(bitmap, width, height, mirror);
                regions.Add(key, original);
            }
            nint copy = CreateRectRgn(0, 0, 0, 0);
            if (copy == 0) throw new InvalidOperationException("Falha ao copiar região transparente.");
            if (CombineRgn(copy, original, 0, 5) == 0)
            { DeleteObject(copy); throw new InvalidOperationException("Falha ao copiar região transparente."); }
            return copy;
        }

        private void Clear() { foreach (nint region in regions.Values) DeleteObject(region); regions.Clear(); }
        public void Dispose() { if (disposed) return; Clear(); disposed = true; }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    internal static WorkArea GetWorkArea(nint hwnd)
        => ReadWorkArea(MonitorFromWindow(hwnd, 2));

    internal static WorkArea GetWorkAreaAt(double x, double y)
        => ReadWorkArea(MonitorFromPoint(new Point { X = (int)Math.Round(x), Y = (int)Math.Round(y) }, 2));

    private static WorkArea ReadWorkArea(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Não foi possível consultar a área útil do monitor.");
        return new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    internal static bool[] BuildMask(BitmapSource bitmap, int width, int height, bool mirror)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        double scale = Math.Min((double)width / bitmap.PixelWidth, (double)height / bitmap.PixelHeight);
        double drawWidth = bitmap.PixelWidth * scale, drawHeight = bitmap.PixelHeight * scale;
        double left = (width - drawWidth) / 2, top = height - drawHeight;
        var mask = new bool[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sx = (int)Math.Floor(((mirror ? width - x - 0.5 : x + 0.5) - left) / scale);
            int sy = (int)Math.Floor((y + 0.5 - top) / scale);
            mask[y * width + x] = sx >= 0 && sx < bitmap.PixelWidth && sy >= 0 && sy < bitmap.PixelHeight
                && pixels[(sy * bitmap.PixelWidth + sx) * 4 + 3] > SpriteManager.AlphaThreshold;
        }
        return mask;
    }

    private static nint BuildRegion(BitmapSource bitmap, int width, int height, bool mirror)
    {
        var mask = BuildMask(bitmap, width, height, mirror);
        nint region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) throw new InvalidOperationException("Falha ao criar região transparente.");
        try
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width;)
            {
                if (!mask[y * width + x]) { x++; continue; }
                int start = x++;
                while (x < width && mask[y * width + x]) x++;
                nint run = CreateRectRgn(start, y, x, y + 1);
                if (run == 0) throw new InvalidOperationException("Falha ao criar faixa transparente.");
                try { if (CombineRgn(region, region, run, 2) == 0) throw new InvalidOperationException("Falha ao combinar região transparente."); }
                finally { DeleteObject(run); }
            }
            return region;
        }
        catch { DeleteObject(region); throw; }
    }

    internal static void ApplyRegion(nint hwnd, BitmapSource bitmap, bool mirror, RegionCache? cache = null)
    {
        GetWindowRect(hwnd, out var rect);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        nint region = cache is null ? BuildRegion(bitmap, rect.Width, rect.Height, mirror)
            : cache.CopyRegion(bitmap, rect.Width, rect.Height, mirror);
        try
        {
            // A copy is transferred to Windows; cached masters remain ours until disposal.
            if (SetWindowRgn(hwnd, region, true) != 0) region = 0;
            else throw new InvalidOperationException("Não foi possível aplicar a região transparente.");
        }
        finally { if (region != 0) DeleteObject(region); }
    }
}
