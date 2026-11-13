using System.Runtime.InteropServices;

namespace Smiley;

internal sealed class SmileyChatHotKey : IDisposable
{
    internal const int Id = 0x534D;
    private readonly nint hwnd;
    public bool IsRegistered { get; private set; }
    internal SmileyChatHotKey(nint hwnd)
    {
        this.hwnd = hwnd;
        IsRegistered = RegisterHotKey(hwnd, Id, 0x0001 | 0x4000, 0x53); // ALT | NOREPEAT, S.
    }
    public void Dispose()
    {
        if (!IsRegistered) return;
        UnregisterHotKey(hwnd, Id); IsRegistered = false;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
