using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Smiley;

// Optional geometric viewer only. It never reads names/content of foreign windows.
internal sealed class WindowTrackerDiagnosticWindow : Window
{
    private readonly WindowTracker tracker;
    private WindowPlatformMap platformMap = WindowPlatformMap.Unavailable;
    private WindowPlatform? activeSegment;
    private NavigationDiagnostic navigation;
    private readonly TextBlock summary = new() { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox details = new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(12, 0, 12, 12), Background = Brushes.WhiteSmoke
    };
    private readonly GeometryPreview preview = new() { Margin = new Thickness(12, 0, 12, 12), MinHeight = 160 };

    internal WindowTrackerDiagnosticWindow(WindowTracker tracker)
    {
        this.tracker = tracker;
        Title = "Smiley — diagnóstico geométrico de janelas";
        Width = 940; Height = 650; MinWidth = 640; MinHeight = 440;
        ShowActivated = false; ShowInTaskbar = true;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(preview, 1); Grid.SetRow(details, 2);
        layout.Children.Add(summary); layout.Children.Add(preview); layout.Children.Add(details); Content = layout;
        tracker.SnapshotChanged += OnSnapshot;
        Closed += (_, _) => tracker.SnapshotChanged -= OnSnapshot;
        Refresh();
    }

    private void OnSnapshot(object? sender, EventArgs e) => Refresh();
    internal void UpdateNavigation(NavigationDiagnostic status)
    {
        if (navigation == status) return;
        navigation = status; Refresh();
    }
    internal void UpdatePlatforms(WindowPlatformMap platforms, WindowSupport? support)
    {
        var segment = support?.Segment;
        if (ReferenceEquals(platformMap, platforms) && activeSegment == segment) return;
        platformMap = platforms; activeSegment = segment; Refresh();
    }
    private void Refresh()
    {
        var snapshot = tracker.Current;
        summary.Text = $"{snapshot.Windows.Length} janelas principais visíveis · versão {snapshot.Version} · intervalo {tracker.Interval.TotalMilliseconds:0} ms\n" +
            $"Pixels físicos. Z=0 à frente. Verde = plataforma; laranja = apoio; cinza = borda sem apoio. {platformMap.Platforms.Length} segmentos. " +
            (platformMap.IsAvailable ? "Geometria disponível." : "Plataformas suspensas: sem coleta recente.");
        var text = new StringBuilder("HWND               Z    LEFT    TOP    RIGHT  BOTTOM       SIZE      MAX\n");
        foreach (var window in snapshot.Windows)
            text.AppendLine($"0x{window.Handle:X16} {window.ZOrder,3} {window.Bounds.Left,7} {window.Bounds.Top,6} {window.Bounds.Right,7} {window.Bounds.Bottom,7} {window.Bounds.Width,5}x{window.Bounds.Height,-5} {(window.IsMaximized ? "sim" : "não")}");
        text.AppendLine("\nPlataformas expostas (pixels físicos):");
        foreach (var platform in platformMap.Platforms)
            text.AppendLine($"0x{platform.Handle:X} X=[{platform.Left}, {platform.Right}) Y={platform.Y}" + (activeSegment == platform ? "  ← APOIO" : ""));
        text.AppendLine($"\nNavegação: {navigation.Status}\nOrigem: 0x{navigation.Origin:X} · destino: 0x{navigation.Target:X}");
        if (navigation.DepartureX is { } departure)
            text.AppendLine($"Partida X={departure:0.##} · chegada X={navigation.LandingX:0.##} · pés Y={navigation.TargetY:0.##}");
        text.AppendLine("\nÚltimo lote de mudanças (não é um histórico):");
        int count = 0;
        foreach (var change in snapshot.Changes)
        {
            if (count++ == 32) { text.AppendLine("… lote truncado na apresentação."); break; }
            text.AppendLine($"0x{change.Handle:X} {change.Kind} → {change.After?.Visibility.ToString() ?? "ausente"}");
        }
        details.Text = text.ToString();
        preview.Snapshot = snapshot; preview.Platforms = platformMap; preview.Active = activeSegment; preview.InvalidateVisual();
    }

    private sealed class GeometryPreview : FrameworkElement
    {
        internal WindowMapSnapshot Snapshot { get; set; } = WindowMapSnapshot.Empty;
        internal WindowPlatformMap Platforms { get; set; } = WindowPlatformMap.Unavailable;
        internal WindowPlatform? Active { get; set; }
        private readonly Pen outline = new(Brushes.SteelBlue, 1);
        private readonly Pen topEdge = new(Brushes.Gray, 1);
        private readonly Pen usable = new(Brushes.ForestGreen, 3);
        private readonly Pen supported = new(Brushes.DarkOrange, 5);
        private readonly Brush fill = new SolidColorBrush(Color.FromArgb(20, 50, 110, 190));
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize));
            if (Snapshot.Windows.IsEmpty || ActualWidth <= 24 || ActualHeight <= 24) return;
            double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
            foreach (var window in Snapshot.Windows)
            {
                left = Math.Min(left, window.Bounds.Left); top = Math.Min(top, window.Bounds.Top);
                right = Math.Max(right, window.Bounds.Right); bottom = Math.Max(bottom, window.Bounds.Bottom);
            }
            double scale = Math.Min((ActualWidth - 24) / (right - left), (ActualHeight - 24) / (bottom - top));
            for (int i = Snapshot.Windows.Length - 1; i >= 0; i--)
            {
                var window = Snapshot.Windows[i];
                var rect = new Rect(12 + (window.Bounds.Left - left) * scale, 12 + (window.Bounds.Top - top) * scale,
                    window.Bounds.Width * scale, window.Bounds.Height * scale);
                drawing.DrawRectangle(fill, outline, rect);
                drawing.DrawLine(topEdge, rect.TopLeft, rect.TopRight);
                var label = new FormattedText($"Z{window.ZOrder} · 0x{window.Handle:X}", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Consolas"), 10, Brushes.MidnightBlue, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(label, new Point(rect.Left + 3, rect.Top + 3));
            }
            if (Platforms.IsAvailable && ReferenceEquals(Platforms.Source, Snapshot))
            foreach (var platform in Platforms.Platforms)
                drawing.DrawLine(Active == platform ? supported : usable,
                    new Point(12 + (platform.Left - left) * scale, 12 + (platform.Y - top) * scale),
                    new Point(12 + (platform.Right - left) * scale, 12 + (platform.Y - top) * scale));
        }
    }
}
