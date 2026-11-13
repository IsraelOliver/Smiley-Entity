using System.Runtime.InteropServices;
using System.Text;

namespace Smiley;

internal readonly record struct WindowMetadata(nint Handle, uint ProcessId, bool IsValid, bool IsRoot, bool HasOwner,
    uint Style, uint ExtendedStyle, string ClassName);

internal static class WindowClassifier
{
    internal const uint Child = 0x40000000, ToolWindow = 0x80, NoActivate = 0x08000000, Transparent = 0x20;
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "tooltips_class32", "#32768",
        "SysShadow", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "ApplicationManager_DesktopShellWindow",
        "NotifyIconOverflowWindow", "DV2ControlHost", "IME", "MSCTFIME UI"
    };
    internal static bool IsMainWindow(WindowMetadata window, nint excludedHandle, uint excludedProcessId)
        => window.IsValid && window.Handle != 0 && window.Handle != excludedHandle &&
           (excludedProcessId == 0 || window.ProcessId != excludedProcessId) && window.IsRoot && !window.HasOwner &&
           (window.Style & Child) == 0 && (window.ExtendedStyle & (ToolWindow | NoActivate | Transparent)) == 0 &&
           window.ClassName.Length > 0 && !ShellClasses.Contains(window.ClassName);
}

// Only native identity, class/style metadata and geometry are read. No window text,
// accessibility, process names, screenshots, SendMessage, or user-content APIs.
internal sealed class NativeWindowGeometrySource : IWindowGeometrySource, ISingleWindowGeometrySource, IDisposable
{
    private readonly nint excludedHandle;
    private readonly uint excludedProcessId;
    private readonly List<nint> enumerated = new();
    private readonly HashSet<nint> candidates = new(), visited = new();
    private readonly List<nint> ordered = new(), staleClasses = new();
    private readonly Dictionary<nint, (uint Pid, uint Tid, string Name)> classes = new();
    private readonly StringBuilder classBuffer = new(256);
    private readonly EnumProc callback;
    private readonly Action? beforeFinalValidation;
    private const int MaximumWindows = 16384;

    internal NativeWindowGeometrySource(nint excludedHandle, uint? excludedProcessId = null, Action? beforeFinalValidation = null)
    {
        this.excludedHandle = excludedHandle;
        this.excludedProcessId = excludedProcessId ?? (uint)Environment.ProcessId;
        this.beforeFinalValidation = beforeFinalValidation;
        callback = (handle, _) => { enumerated.Add(handle); return enumerated.Count < MaximumWindows; };
    }

    public bool TryCapture(List<WindowObservation> destination)
    {
        destination.Clear(); enumerated.Clear(); candidates.Clear(); visited.Clear(); ordered.Clear();
        if (!EnumWindows(callback, 0)) return false;
        foreach (nint handle in enumerated) candidates.Add(handle);
        // EnumWindows discovers candidates. A separate BOUNDED traversal establishes Z
        // explicitly; cycle detection prevents the unsafe unbounded GetWindow loop.
        nint current = GetTopWindow(0);
        while (current != 0)
        {
            if (visited.Count >= MaximumWindows || !visited.Add(current)) return false;
            if (candidates.Contains(current)) ordered.Add(current);
            current = GetWindow(current, 2); // GW_HWNDNEXT, front to back.
        }
        foreach (nint handle in enumerated)
            if (!visited.Contains(handle) && IsWindow(handle)) return false; // Z-order changed during capture; retry next poll.
        foreach (nint handle in ordered)
        {
            if (TryRead(handle, out var observation)) destination.Add(observation);
        }
        beforeFinalValidation?.Invoke();
        // A window may die AFTER its DWM query. Do not publish that stale rectangle.
        for (int i = destination.Count - 1; i >= 0; i--)
        {
            var observation = destination[i];
            uint thread = GetWindowThreadProcessId(observation.Geometry.Handle, out uint process);
            if (!IsWindow(observation.Geometry.Handle) || process != observation.ProcessId || thread != observation.ThreadId)
            { destination.RemoveAt(i); continue; }
            if (observation.Geometry.IsVisible)
            {
                nint handle = observation.Geometry.Handle;
                var visibility = IsIconic(handle) ? WindowVisibility.Minimized : !IsWindowVisible(handle) ? WindowVisibility.Hidden : WindowVisibility.Visible;
                if (visibility == WindowVisibility.Visible)
                {
                    int result = DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int));
                    if (result != 0) visibility = WindowVisibility.Unavailable;
                    else if (cloaked != 0) visibility = WindowVisibility.Cloaked;
                }
                if (visibility != WindowVisibility.Visible)
                    destination[i] = observation with { Geometry = observation.Geometry with { Visibility = visibility, Bounds = default } };
            }
        }
        staleClasses.Clear();
        foreach (nint handle in classes.Keys) if (!candidates.Contains(handle)) staleClasses.Add(handle);
        foreach (nint handle in staleClasses) classes.Remove(handle);
        return true;
    }

    public bool TryCaptureWindow(nint handle, out WindowObservation observation)
    {
        if (!TryRead(handle, out observation)) return false;
        uint thread = GetWindowThreadProcessId(handle, out uint process);
        return IsWindow(handle) && thread == observation.ThreadId && process == observation.ProcessId;
    }

    private bool TryRead(nint handle, out WindowObservation observation)
    {
        observation = default;
        uint thread = GetWindowThreadProcessId(handle, out uint process);
        if (thread == 0 || handle == excludedHandle || process == excludedProcessId || !IsWindow(handle)) return false;
        uint style = unchecked((uint)GetWindowLong(handle, -16));
        uint extended = unchecked((uint)GetWindowLong(handle, -20));
        classBuffer.Clear();
        if (GetClassName(handle, classBuffer, classBuffer.Capacity) == 0) return false;
        string name;
        if (classes.TryGetValue(handle, out var cached) && cached.Pid == process && cached.Tid == thread && SameClass(classBuffer, cached.Name)) name = cached.Name;
        else
        {
            name = classBuffer.ToString(); classes[handle] = (process, thread, name);
        }
        var metadata = new WindowMetadata(handle, process, true, GetAncestor(handle, 2) == handle, GetWindow(handle, 4) != 0, style, extended, name);
        if (!WindowClassifier.IsMainWindow(metadata, excludedHandle, excludedProcessId)) return false;
        var visibility = WindowVisibility.Visible;
        var bounds = default(WindowBounds);
        if (IsIconic(handle)) visibility = WindowVisibility.Minimized;
        else if (!IsWindowVisible(handle)) visibility = WindowVisibility.Hidden;
        else if (DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int)) != 0) visibility = WindowVisibility.Unavailable;
        else if (cloaked != 0) visibility = WindowVisibility.Cloaked;
        else if (DwmGetWindowAttribute(handle, 9, out NativeRect rect, Marshal.SizeOf<NativeRect>()) != 0) visibility = WindowVisibility.Unavailable;
        else
        {
            bounds = new(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!bounds.IsValid) visibility = WindowVisibility.Unavailable;
        }
        observation = new(new(handle, bounds, visibility, IsZoomed(handle), 0), process, thread);
        return true;
    }

    private static bool SameClass(StringBuilder buffer, string name)
    {
        if (buffer.Length != name.Length) return false;
        for (int i = 0; i < name.Length; i++) if (buffer[i] != name[i]) return false;
        return true;
    }

    public void Dispose() { classes.Clear(); enumerated.Clear(); candidates.Clear(); visited.Clear(); ordered.Clear(); staleClasses.Clear(); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumProc(nint hwnd, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
}
