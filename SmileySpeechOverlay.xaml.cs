using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Smiley;

// A bounded layered surface with ONLY text and a line. All placement inputs are physical pixels.
public partial class SmileySpeechOverlay : Window
{
    private nint hwnd;
    private HwndSource? source;
    private double expiresAt;
    private bool dirty = true, positioned;
    private NativeMethods.Rect previousCharacter;
    private WorkArea previousArea;
    private DpiScale previousDpi;
    private double maximumTextWidth = 320;
    public double MaximumTextWidth
    {
        get => maximumTextWidth;
        set { if (!double.IsFinite(value) || value < 80 || value > 600) throw new ArgumentOutOfRangeException(nameof(value)); maximumTextWidth = value; dirty = true; }
    }

    public SmileySpeechOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle; source = HwndSource.FromHwnd(hwnd);
            // Layered + TRANSPARENT passes mouse input to other processes, not just WPF hit tests.
            SetWindowLong(hwnd, -20, GetWindowLong(hwnd, -20) | 0x20 | 0x08000000 | 0x80);
            source?.AddHook(WindowHook);
        };
        Closed += (_, _) => { source?.RemoveHook(WindowHook); source = null; };
    }

    private nint WindowHook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0084) { handled = true; return -1; } // HTTRANSPARENT.
        if (message == 0x0021) { handled = true; return 3; } // MA_NOACTIVATE.
        if (message == 0x02E0) dirty = true;
        return 0;
    }

    internal void Present(string text, NativeMethods.Rect character, WorkArea area, DpiScale characterDpi, double now)
    {
        text = text.Trim(); if (text.Length == 0) return;
        var elements = new StringInfo(text);
        Speech.Text = elements.LengthInTextElements > 360 ? elements.SubstringByTextElements(0, 360).TrimEnd() + "…" : text;
        expiresAt = now + Math.Clamp(3 + Speech.Text.Length / 18.0, 5, 18);
        dirty = true;
        new WindowInteropHelper(this).EnsureHandle();
        Follow(character, area, characterDpi, now);
        if (!IsVisible) Show();
    }

    internal void Expire(double now)
    {
        if (IsVisible && now >= expiresAt) { Hide(); positioned = false; }
    }

    internal void Follow(NativeMethods.Rect character, WorkArea area, DpiScale characterDpi, double now)
    {
        if (now >= expiresAt) { Expire(now); return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        if (Math.Abs(dpi.DpiScaleX - characterDpi.DpiScaleX) > 0.001 || Math.Abs(dpi.DpiScaleY - characterDpi.DpiScaleY) > 0.001)
        {
            // Move into the physical destination monitor before measuring WPF text there.
            NativeMethods.SetWindowPos(hwnd, 0, character.Left, character.Top, 0, 0, 0x0015);
            dpi = VisualTreeHelper.GetDpi(this);
        }
        if (!dirty && positioned && character.Equals(previousCharacter) && area == previousArea && dpi.Equals(previousDpi)) return;
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        if (dirty || area != previousArea || !dpi.Equals(previousDpi))
        {
            Speech.MaxWidth = Math.Min(MaximumTextWidth, Math.Max(1, (area.Right - area.Left) / sx - 24));
            Speech.MaxHeight = Math.Min(260, Math.Max(1, (area.Bottom - area.Top) / sy - 24));
            Speech.Measure(new Size(Speech.MaxWidth, Speech.MaxHeight));
        }
        double tw = Speech.DesiredSize.Width * sx, th = Speech.DesiredSize.Height * sy;
        double headX = Math.Clamp(character.Left + character.Width * 0.5, area.Left + 4, area.Right - 4);
        double headY = Math.Clamp(character.Top + character.Height * 0.08, area.Top + 4, area.Bottom - 4);
        // Offset text above one shoulder: short diagonal connector, never a horizontal underline across the face.
        (double X, double Y) aboveRight = (headX + 28 * sx, character.Top - 34 * sy - th);
        (double X, double Y) aboveLeft = (headX - 28 * sx - tw, character.Top - 34 * sy - th);
        (double X, double Y) left = (character.Left - 18 * sx - tw, character.Top + character.Height * 0.22 + 42 * sy);
        (double X, double Y) right = (character.Right + 18 * sx, left.Y);
        (double X, double Y) below = (headX - tw / 2, character.Bottom + 18 * sy);
        bool Fits((double X, double Y) p) => p.X >= area.Left + 8 * sx && p.X + tw <= area.Right - 8 * sx &&
            p.Y >= area.Top + 8 * sy && p.Y + th <= area.Bottom - 8 * sy;
        bool atRight = aboveRight.X + tw > area.Right - 8 * sx;
        var preferred = atRight ? aboveLeft : aboveRight;
        var alternate = atRight ? aboveRight : aboveLeft;
        var side = atRight ? left : right;
        var otherSide = atRight ? right : left;
        var location = Fits(preferred) ? preferred : Fits(alternate) ? alternate : Fits(side) ? side : Fits(otherSide) ? otherSide : Fits(below) ? below : preferred;
        double tx = Math.Clamp(location.X, area.Left + 8 * sx, Math.Max(area.Left + 8 * sx, area.Right - 8 * sx - tw));
        double ty = Math.Clamp(location.Y, area.Top + 8 * sy, Math.Max(area.Top + 8 * sy, area.Bottom - 8 * sy - th));
        double lineX, lineY;
        if (ty + th < headY - 12 * sy)
        {
            lineX = Math.Clamp(headX, tx + 3 * sx, Math.Max(tx + 3 * sx, tx + tw - 3 * sx));
            lineY = ty + th + 6 * sy; // Small breathing space between glyphs and connector.
        }
        else
        {
            bool onRight = tx + tw / 2 > headX;
            headX = Math.Clamp(character.Left + character.Width * (onRight ? 0.77 : 0.23), area.Left + 4, area.Right - 4);
            headY = Math.Clamp(character.Top + character.Height * 0.22, area.Top + 4, area.Bottom - 4);
            lineX = onRight ? tx - 6 * sx : tx + tw + 6 * sx;
            lineY = ty + Math.Min(th / 2, 16 * sy);
        }
        int x = (int)Math.Floor(Math.Min(tx - 8 * sx, Math.Min(headX, lineX) - 3 * sx)), y = (int)Math.Floor(Math.Min(ty - 8 * sy, Math.Min(headY, lineY) - 3 * sy));
        int width = Math.Max(1, (int)Math.Ceiling(Math.Max(tx + tw + 8 * sx, Math.Max(headX, lineX) + 3 * sx) - x));
        int height = Math.Max(1, (int)Math.Ceiling(Math.Max(ty + th + 8 * sy, Math.Max(headY, lineY) + 3 * sy) - y));
        Width = width / sx; Height = height / sy;
        Canvas.SetLeft(Speech, (tx - x) / sx); Canvas.SetTop(Speech, (ty - y) / sy);
        PointerLine.X1 = (headX - x) / sx; PointerLine.Y1 = (headY - y) / sy;
        PointerLine.X2 = (lineX - x) / sx; PointerLine.Y2 = (lineY - y) / sy;
        NativeMethods.SetWindowPos(hwnd, 0, x, y, width, height, 0x0014); // NOZORDER | NOACTIVATE.
        previousCharacter = character; previousArea = area; previousDpi = dpi; dirty = false; positioned = true;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hwnd, int index, int value);
}
