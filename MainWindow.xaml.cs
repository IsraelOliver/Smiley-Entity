using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Smiley;

public partial class MainWindow : Window
{
    private readonly SpriteManager sprites = new();
    private readonly SmileyController controller = new();
    private readonly SmileyBehaviorController behavior;
    private CursorPosition? cursorPosition;
    private const double FrameInterval = 1.0 / 60;
    private TimeSpan? lastRenderingTime;
    private double frameBudget;
    private readonly Stopwatch clock = new();
    private nint hwnd;
    private HwndSource? source;
    private ImageSource? displayedSprite;
    private readonly NativeMethods.RegionCache regions = new();
    private int displayedDirection;
    private double previousTime;
    private bool pointerDown, dragging, menuOpen;
    private NativeMethods.Point pressPoint;
    private double startX, startY;
    private readonly DragVelocityTracker dragVelocity = new();
    private WindowTracker? windowTracker;
    private WindowTrackerDiagnosticWindow? windowDiagnostics;
    private readonly bool startWindowDiagnostics;
    private readonly WindowPlatformProvider platformProvider = new();
    private SmileyChatHotKey? chatHotKey;
    private SmileyChatWindow? chatWindow;
    private SmileySpeechOverlay? speechOverlay;
    private bool closing;
    public WindowMapSnapshot ObservedWindows => windowTracker?.Current ?? WindowMapSnapshot.Empty;

    public MainWindow(bool startPaused = false, bool enableWindowDiagnostics = false)
    {
        InitializeComponent();
        startWindowDiagnostics = enableWindowDiagnostics;
        behavior = new SmileyBehaviorController(controller);
        if (startPaused) controller.SetPaused(true);
        Character.Source = sprites[Pose.Neutral];
        Loaded += OnLoaded;
        SizeChanged += (_, _) => { if (hwnd != 0) UpdateSprite(true); };
        Closed += (_, _) => { closing = true; chatHotKey?.Dispose(); chatWindow?.Close(); speechOverlay?.Close(); CompositionTarget.Rendering -= OnRendering; clock.Stop(); windowDiagnostics?.Close(); windowTracker?.Dispose(); source?.RemoveHook(WindowHook); regions.Dispose(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (windowTracker is not null) return;
        hwnd = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WindowHook);
        chatHotKey = new SmileyChatHotKey(hwnd);
        ChatItem.InputGestureText = chatHotKey.IsRegistered ? "Alt+S" : "";
        ChatItem.ToolTip = chatHotKey.IsRegistered ? "Abrir ou focar a conversa" : "Alt+S está indisponível. A conversa continua acessível por este menu.";
        var area = NativeMethods.GetWorkArea(hwnd);
        NativeMethods.GetWindowRect(hwnd, out var rect);
        controller.MoveTo(area.Right - rect.Width - 60, area.Bottom - FootOffset(rect), area, rect.Width, rect.Height, FootOffset(rect));
        Place();
        UpdateSprite(true);
        clock.Start(); CompositionTarget.Rendering += OnRendering;
        windowTracker = new WindowTracker(hwnd);
        windowTracker.Start();
        if (startWindowDiagnostics) ShowWindowDiagnostics();
        if (sprites.Warnings.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, sprites.Warnings), "Smiley — imagens com possível fundo opaco");
    }

    private nint WindowHook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == SmileyChatHotKey.Id) // WM_HOTKEY, only registered Alt+S.
        { handled = true; ShowChat(); return 0; }
        if (message is 0x02E0 or 0x007E or 0x001A) // DPI, display layout or work area changed.
            Dispatcher.BeginInvoke(() => { if (IsLoaded) { Constrain(); UpdateSprite(true); } });
        return 0;
    }

    private void Constrain()
    {
        UpdatePlatforms();
        controller.ReconcilePlatforms(NativeMethods.GetWorkAreaAt);
        Place();
        NativeMethods.GetWindowRect(hwnd, out var rect);
        controller.UpdateEnvironment(NativeMethods.GetWorkArea(hwnd), rect.Width, rect.Height, FootOffset(rect));
        Place();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var frame = (RenderingEventArgs)e;
        if (lastRenderingTime == frame.RenderingTime) return;
        frameBudget += lastRenderingTime is { } last ? (frame.RenderingTime - last).TotalSeconds : FrameInterval;
        lastRenderingTime = frame.RenderingTime;
        if (frameBudget + 1e-6 < FrameInterval) return;
        // Retain fractional time at high refresh rates; never replay missed frames.
        frameBudget = Math.Max(0, frameBudget - Math.Floor((frameBudget + 1e-6) / FrameInterval) * FrameInterval);
        double now = clock.Elapsed.TotalSeconds, delta = now - previousTime;
        previousTime = now;
        speechOverlay?.Expire(now);
        UpdatePlatforms();
        if (pointerDown || menuOpen) return;
        controller.ReconcilePlatforms(NativeMethods.GetWorkAreaAt);
        Place();
        NativeMethods.GetWindowRect(hwnd, out var rect);
        var dpi = VisualTreeHelper.GetDpi(this);
        if (behavior.NeedsCursorSample(delta))
            cursorPosition = NativeMethods.GetCursorPos(out var cursor) ? new CursorPosition(cursor.X, cursor.Y) : null;
        behavior.Tick(delta, NativeMethods.GetWorkArea(hwnd), rect.Width, rect.Height, dpi.DpiScaleX, dpi.DpiScaleY, cursorPosition, FootOffset(rect));
        Place(); UpdateSprite();
        windowDiagnostics?.UpdatePlatforms(platformProvider.Current, controller.ActiveSupport);
        windowDiagnostics?.UpdateNavigation(behavior.NavigationStatus);
    }

    private void UpdatePlatforms()
    {
        if (windowTracker is null) return;
        var map = platformProvider.Update(windowTracker.Current, windowTracker.LastPollSucceeded, windowTracker.LastPollAtUtc,
            clock.Elapsed.TotalSeconds, controller.MinimumPlatformWidth * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var physicalMap = map;
        if (map.IsAvailable && !controller.Paused && !pointerDown && !menuOpen && controller.ActiveSupport is { } support)
        {
            var geometry = windowTracker.TryRefreshWindow(support.Handle, out var live) ? live
                : new WindowGeometry(support.Handle, default, WindowVisibility.Unavailable, false, -1);
            physicalMap = platformProvider.RefreshSupport(map, geometry,
                controller.MinimumPlatformWidth * VisualTreeHelper.GetDpi(this).DpiScaleX);
        }
        controller.SetPlatforms(physicalMap);
        behavior.SetNavigationMap(physicalMap, windowTracker.LastPollSucceeded && map.IsAvailable);
        windowDiagnostics?.UpdatePlatforms(map, controller.ActiveSupport);
    }

    private void Place()
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        int x = (int)Math.Round(controller.X), y = (int)Math.Round(controller.Y);
        if (rect.Left != x || rect.Top != y)
            NativeMethods.SetWindowPos(hwnd, 0, x, y, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
        UpdateSpeechPosition();
    }

    private void UpdateSpeechPosition()
    {
        if (speechOverlay is not { IsVisible: true }) return;
        NativeMethods.GetWindowRect(hwnd, out var rect);
        speechOverlay.Follow(rect, NativeMethods.GetWorkArea(hwnd), VisualTreeHelper.GetDpi(this), clock.Elapsed.TotalSeconds);
    }

    private void ShowSpeech(string text)
    {
        if (closing) return;
        speechOverlay ??= new SmileySpeechOverlay { Owner = this };
        NativeMethods.GetWindowRect(hwnd, out var rect);
        speechOverlay.Present(text, rect, NativeMethods.GetWorkArea(hwnd), VisualTreeHelper.GetDpi(this), clock.Elapsed.TotalSeconds);
    }

    private void OnChat(object sender, RoutedEventArgs e) => ShowChat();
    private void ShowChat()
    {
        if (closing) return;
        if (chatWindow is null)
        {
            chatWindow = new SmileyChatWindow(ShowSpeech);
            chatWindow.Closed += (_, _) => chatWindow = null;
            chatWindow.Show();
        }
        chatWindow.FocusInput();
    }

    private static double FootOffset(NativeMethods.Rect rect)
    {
        // Same Uniform + bottom alignment as the Image and native alpha region.
        double scale = Math.Min(rect.Width / SpriteManager.CanvasWidth, rect.Height / SpriteManager.CanvasHeight);
        return rect.Height - (SpriteManager.CanvasHeight - SpriteManager.GroundLine) * scale;
    }

    private void UpdateSprite(bool force = false)
    {
        int direction = controller.Direction;
        var bitmap = controller.IsMoving ? sprites.GetMovementFrame(controller.MovementMode, controller.AnimationFrame) : sprites[controller.DisplayPose];
        if (!force && ReferenceEquals(displayedSprite, bitmap) && displayedDirection == direction) return;
        displayedSprite = bitmap; displayedDirection = direction;
        Character.Source = bitmap;
        Facing.ScaleX = direction;
        NativeMethods.ApplyRegion(hwnd, bitmap, direction < 0, regions);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out pressPoint)) return;
        pointerDown = true; dragging = false;
        behavior.NotifyManualInteraction();
        startX = controller.X; startY = controller.Y;
        dragVelocity.Reset(clock.Elapsed.TotalSeconds, startX, startY);
        CaptureMouse(); e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!pointerDown || !NativeMethods.GetCursorPos(out var point)) return;
        double dx = point.X - pressPoint.X, dy = point.Y - pressPoint.Y;
        var dpi = VisualTreeHelper.GetDpi(this);
        if (!dragging && (Math.Abs(dx) >= SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX || Math.Abs(dy) >= SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY))
        { dragging = true; controller.BeginDrag(); UpdateSprite(); }
        if (!dragging) return;
        NativeMethods.GetWindowRect(hwnd, out var rect);
        // Position first to let MonitorFromWindow choose the destination monitor, then constrain.
        NativeMethods.SetWindowPos(hwnd, 0, (int)Math.Round(startX + dx), (int)Math.Round(startY + dy), 0, 0, 0x0015);
        controller.MoveTo(startX + dx, startY + dy, NativeMethods.GetWorkArea(hwnd), rect.Width, rect.Height, FootOffset(rect));
        dragVelocity.Add(clock.Elapsed.TotalSeconds, controller.X, controller.Y);
        Place(); e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!pointerDown) return;
        pointerDown = false;
        if (dragging)
        {
            var velocity = dragVelocity.Release(clock.Elapsed.TotalSeconds, controller.X, controller.Y);
            var dpi = VisualTreeHelper.GetDpi(this);
            controller.EndDrag(velocity.X, velocity.Y, dpi.DpiScaleX, dpi.DpiScaleY);
        }
        else controller.React();
        behavior.NotifyManualInteraction();
        previousTime = clock.Elapsed.TotalSeconds;
        dragging = false; ReleaseMouseCapture(); UpdateSprite(); e.Handled = true;
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!pointerDown) return;
        pointerDown = dragging = false; controller.EndDrag(); behavior.NotifyManualInteraction(); previousTime = clock.Elapsed.TotalSeconds; UpdateSprite();
    }

    private void UpdateMovementMenu()
    {
        AutoItem.IsChecked = behavior.Preference == MovementPreference.Automatic;
        WalkItem.IsChecked = behavior.Preference == MovementPreference.Walking;
        RunItem.IsChecked = behavior.Preference == MovementPreference.Running;
    }
    private void OnAutoMode(object sender, RoutedEventArgs e) { behavior.SetPreference(MovementPreference.Automatic); UpdateMovementMenu(); }
    private void OnWalkMode(object sender, RoutedEventArgs e) { behavior.SetPreference(MovementPreference.Walking); UpdateMovementMenu(); UpdateSprite(); }
    private void OnRunMode(object sender, RoutedEventArgs e) { behavior.SetPreference(MovementPreference.Running); UpdateMovementMenu(); UpdateSprite(); }
    private void OnAutonomy(object sender, RoutedEventArgs e) { behavior.SetEnabled(AutonomyItem.IsChecked); UpdateSprite(); }
    private void OnExplore(object sender, RoutedEventArgs e) { behavior.SetExploreWindows(ExploreItem.IsChecked); UpdateExploreMenu(); UpdateSprite(); }
    private void UpdateExploreMenu()
    {
        ExploreItem.IsChecked = behavior.ExploreWindows;
        ExploreItem.Header = behavior.ExploreWindows ? "Explorar janelas: ativado" : "Explorar janelas: desativado";
    }
    private void OnMenuOpened(object sender, RoutedEventArgs e) { menuOpen = true; PauseItem.Header = controller.Paused ? "Retomar" : "Pausar"; JumpItem.IsEnabled = controller.CanJump; AutonomyItem.IsChecked = behavior.Enabled; AutonomyItem.Header = behavior.Enabled ? "Autonomia ativada" : "Autonomia desativada"; TopmostItem.IsChecked = Topmost; UpdateMovementMenu(); UpdateExploreMenu(); }
    private void OnMenuClosed(object sender, RoutedEventArgs e) { menuOpen = false; previousTime = clock.Elapsed.TotalSeconds; }
    private void OnPause(object sender, RoutedEventArgs e) { controller.SetPaused(!controller.Paused); UpdateSprite(); }
    private void OnJump(object sender, RoutedEventArgs e) { Constrain(); if (controller.TryJump(VisualTreeHelper.GetDpi(this).DpiScaleY)) behavior.NotifyManualInteraction(); UpdateSprite(); }
    private void OnTopmost(object sender, RoutedEventArgs e) => Topmost = TopmostItem.IsChecked;
    private void ShowWindowDiagnostics()
    {
        if (windowTracker is null || windowDiagnostics is not null) return;
        windowDiagnostics = new WindowTrackerDiagnosticWindow(windowTracker) { Owner = this };
        windowDiagnostics.UpdatePlatforms(platformProvider.Current, controller.ActiveSupport);
        windowDiagnostics.Closed += (_, _) => { windowDiagnostics = null; WindowDiagnosticsItem.IsChecked = false; };
        WindowDiagnosticsItem.IsChecked = true;
        windowDiagnostics.Show();
    }
    private void OnWindowDiagnostics(object sender, RoutedEventArgs e)
    {
        if (WindowDiagnosticsItem.IsChecked) ShowWindowDiagnostics(); else windowDiagnostics?.Close();
    }
    private void OnExit(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
