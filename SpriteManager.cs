using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Smiley;

public sealed class SpriteManager
{
    public const double CanvasWidth = 210, CanvasHeight = 190, VisualHeight = 180;
    // Whole grid cells retain one horizontal anchor. The foot reference is derived
    // once from visible alpha for presentation; no source/export PNG is rewritten.
    public readonly record struct Alignment(double AnchorX, double TopY, double GroundY);
    public static IReadOnlyList<Alignment> WalkingAlignment { get; } = Array.AsReadOnly(Enumerable.Repeat(new Alignment(0.5, 0, 1), SmileyController.FramesPerCycle).ToArray());
    public static IReadOnlyList<Alignment> RunningAlignment { get; } = Array.AsReadOnly(Enumerable.Repeat(new Alignment(0.5, 0, 1), SmileyController.FramesPerCycle).ToArray());
    public const byte AlphaThreshold = 8;
    private readonly Dictionary<Pose, BitmapSource> sprites = new();
    private readonly BitmapSource[] walking;
    private readonly BitmapSource[] running;
    public List<string> Warnings { get; } = new();
    public BitmapSource this[Pose pose] => sprites[pose];
    public BitmapSource GetMovementFrame(MovementMode mode, int frame) => (mode == MovementMode.Running ? running : walking)[frame];
    public const double GroundLine = CanvasHeight;

    public SpriteManager(string? directory = null)
    {
        directory ??= Path.Combine(AppContext.BaseDirectory, "SmileyImagens");
        var names = new Dictionary<Pose, string>
        {
            [Pose.Neutral] = "Neutral Hooded Shadow Mascot.png",
            [Pose.Running] = "Running Hooded Sprite in Motion.png",
            [Pose.Startled] = "Startled Hooded Chibi Character.png",
            [Pose.Frowning] = "Frowning Hooded Void Mascot.png",
            [Pose.Sceptical] = "Sceptical Hooded Chibi Character.png",
            [Pose.Falling] = "Mascote Sombra em Queda.png"
        };
        foreach (var pair in names)
            sprites.Add(pair.Key, Normalize(Load(Path.Combine(directory, pair.Value), Warnings)));
        walking = LoadCycle(directory, "Caminhada", WalkingAlignment);
        running = LoadCycle(directory, "Corrida", RunningAlignment);
    }

    private BitmapSource[] LoadCycle(string directory, string prefix, IReadOnlyList<Alignment> alignments)
    {
        var frames = new BitmapSource[alignments.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            var bitmap = Load(Path.Combine(directory, prefix, $"frame_{i + 1:D2}.png"), Warnings, preserveCanvas: true);
            double foot = (double)FindFootLine(bitmap) / bitmap.PixelHeight;
            // Preserve the EXACT same scale (GroundY - TopY = 1) for every frame;
            // translate vertically so the visible foot reference lands at GroundLine.
            frames[i] = Normalize(bitmap, alignments[i] with { GroundY = foot, TopY = foot - 1 });
        }
        return frames;
    }

    internal static int FindFootLine(BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        for (int y = height - 1; y >= 0; y--)
        for (int x = 0; x < width; x++)
            if (pixels[(y * width + x) * 4 + 3] > AlphaThreshold) return y + 1;
        throw new InvalidDataException("Imagem sem referência de contato visível.");
    }

    // Render once at startup at 4x logical resolution; no image allocations during playback.
    private static BitmapSource Normalize(BitmapSource bitmap, Alignment? alignment = null)
    {
        Rect placement;
        if (alignment is { } anchor)
        {
            double scale = VisualHeight / ((anchor.GroundY - anchor.TopY) * bitmap.PixelHeight);
            placement = new Rect(CanvasWidth / 2 - anchor.AnchorX * bitmap.PixelWidth * scale,
                CanvasHeight - anchor.GroundY * bitmap.PixelHeight * scale, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale);
        }
        else
        {
            double scale = Math.Min(CanvasWidth / bitmap.PixelWidth, VisualHeight / bitmap.PixelHeight);
            double width = bitmap.PixelWidth * scale, height = bitmap.PixelHeight * scale;
            placement = new Rect((CanvasWidth - width) / 2, CanvasHeight - height, width, height);
        }
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen()) context.DrawImage(bitmap, placement);
        var result = new RenderTargetBitmap((int)CanvasWidth * 4, (int)CanvasHeight * 4, 384, 384, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }

    public static BitmapSource Load(string path, List<string> warnings, bool preserveCanvas = false)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        int left = width, top = height, right = -1, bottom = -1;
        bool transparent = false;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte alpha = pixels[(y * width + x) * 4 + 3];
            transparent |= alpha == 0;
            if (alpha <= AlphaThreshold) continue;
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (!transparent) warnings.Add($"{Path.GetFileName(path)}: não contém pixels totalmente transparentes; o fundo pode ser opaco. O original foi preservado.");
        if (right < left) throw new InvalidDataException($"Imagem totalmente transparente: {path}");
        if (preserveCanvas) { bitmap.Freeze(); return bitmap; }
        // Only crop the decoded image in memory. Original files are never written.
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        cropped.Freeze();
        return cropped;
    }
}
