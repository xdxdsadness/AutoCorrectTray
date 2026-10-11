using AutoCorrectTray.Logging;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Exclusions;

/// <summary>
/// App blacklist (IDEs, terminals, password managers): matched by process name
/// of the foreground window.
/// </summary>
public sealed class AppExclusions
{
    private readonly Func<List<string>> _blacklistProvider;
    private (IntPtr hwnd, bool decided, bool allowed) _cache;

    public AppExclusions(Func<List<string>> blacklistProvider, Logger log)
    {
        _blacklistProvider = blacklistProvider;
    }

    public void Refresh() => _cache = default;

    public void Invalidate(IntPtr hwnd)
    {
        if (_cache.hwnd != hwnd)
            _cache = default;
    }

    /// <summary>Fast check (cached per foreground hwnd) - called from hooks.</summary>
    public bool IsCurrentAppAllowed
    {
        get
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return true;
            if (_cache.hwnd == hwnd) return _cache.allowed;

            var name = GetProcessName(hwnd);
            var list = _blacklistProvider();
            bool allowed = list is null || name.Length == 0 ||
                           !list.Any(x => string.Equals(x?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            _cache = (hwnd, true, allowed);
            return allowed;
        }
    }

    private static string GetProcessName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return string.Empty;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process?.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
