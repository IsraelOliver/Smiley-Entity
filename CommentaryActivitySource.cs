using System.Runtime.InteropServices;
using System.Text;

namespace Smiley;

// Only session idle age and opaque foreground HWND. No input hooks or application content.
internal sealed class CommentaryActivitySource : IDisposable
{
    private readonly nint hwnd;
    private readonly bool registered;
    private bool locked;
    internal CommentaryActivitySource(nint hwnd) { this.hwnd = hwnd; registered = WTSRegisterSessionNotification(hwnd, 0); }
    internal bool HandleMessage(int message, nint parameter)
    {
        if (message == 0x02B1)
        {
            if (parameter is 7 or 6) locked = true;
            if (parameter is 8 or 5) locked = false;
            return true;
        }
        return message == 0x0218 && parameter is 4 or 7 or 18;
    }
    internal ActivitySample Read()
    {
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        if (locked || !GetLastInputInfo(ref input) || !InteractiveDesktop()) return new(0, 0, false);
        uint elapsed = unchecked((uint)Environment.TickCount - input.Time);
        return elapsed > int.MaxValue ? new(0, 0, false) : new(elapsed / 1000.0, GetForegroundWindow());
    }
    private static bool InteractiveDesktop()
    {
        nint desktop = OpenInputDesktop(0, false, 1);
        if (desktop == 0) return false;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) && name.ToString() == "Default";
        }
        finally { CloseDesktop(desktop); }
    }
    public void Dispose() { if (registered) WTSUnRegisterSessionNotification(hwnd); }
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetUserObjectInformationW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, int length, out int needed);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")] private static extern void WTSUnRegisterSessionNotification(nint hwnd);
}
