using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Smiley;

// Cache outlined glyphs when content/width/DPI changes; moving the overlay does not rebuild text.
public sealed class OutlinedSpeechText : FrameworkElement
{
    private string text = "";
    private Geometry? glyphs;
    private Size measured, constraint;
    private double pixelsPerDip;
    private static readonly Pen Outline = CreateOutline();
    public string Text
    {
        get => text;
        set { if (text == value) return; text = value; glyphs = null; InvalidateMeasure(); InvalidateVisual(); }
    }
    private static Pen CreateOutline()
    {
        var pen = new Pen(Brushes.Black, 2.4) { LineJoin = PenLineJoin.Round };
        pen.Freeze(); return pen;
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 320;
        double height = double.IsFinite(availableSize.Height) ? availableSize.Height : 260;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var size = new Size(width, height);
        if (glyphs is not null && constraint == size && pixelsPerDip == dpi) return measured;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 17, Brushes.White, dpi)
        {
            MaxTextWidth = Math.Max(1, width - 6), MaxTextHeight = Math.Max(1, height - 6),
            LineHeight = 24, Trimming = TextTrimming.CharacterEllipsis
        };
        glyphs = formatted.BuildGeometry(new Point(3, 3)); glyphs.Freeze();
        measured = new Size(Math.Min(width, formatted.WidthIncludingTrailingWhitespace + 6), Math.Min(height, formatted.Height + 6));
        constraint = size; pixelsPerDip = dpi;
        return measured;
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (glyphs is not null)
        {
            drawingContext.DrawGeometry(null, Outline, glyphs);
            drawingContext.DrawGeometry(Brushes.White, null, glyphs);
        }
    }
}
