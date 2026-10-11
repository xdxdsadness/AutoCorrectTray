using AutoCorrectTray.Logging;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Hooks;

/// <summary>Foreground window change event (EVENT_SYSTEM_FOREGROUND).</summary>
public sealed class ForegroundMonitor : IDisposable
{
    private readonly NativeMethods.WinEventProc _proc;
    private IntPtr _hook;

    public event Action<IntPtr>? Raised;

    public ForegroundMonitor(Logger log)
    {
        _proc = OnWinEvent;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero)
            log.Error("Failed to set the WinEventHook for foreground window tracking");
    }

    private void OnWinEvent(IntPtr hHook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == NativeMethods.OBJID_WINDOW)
            Raised?.Invoke(hwnd);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
