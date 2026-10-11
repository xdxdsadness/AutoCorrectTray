using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoCorrectTray.Hotkeys;
using AutoCorrectTray.Native;
using AutoCorrectTray.Settings;

namespace AutoCorrectTray.Hooks;

/// <summary>Registers global hotkeys (RegisterHotKey) via a message-only window.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly AppServices _svc;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private readonly HwndSourceHook _hook;

    public event Action<int>? Fired;

    public HotkeyManager(AppServices svc)
    {
        _svc = svc;
        _hook = WndProc;
    }

    public void RegisterAll()
    {
        EnsureWindow();
        UnregisterAll();
        Register(AppServices.HotkeyFixId, _svc.Settings.FixHotkey);
        Register(AppServices.HotkeyTranslateId, _svc.Settings.TranslateHotkey);
        Register(AppServices.HotkeyPauseId, _svc.Settings.PauseHotkey);
        Register(AppServices.HotkeyCaptureId, _svc.Settings.CaptureHotkey);
    }

    private void EnsureWindow()
    {
        if (_source != null) return;
        // Message-only window (HWND_MESSAGE); falls back to a plain hidden window.
        _source = new HwndSource(0, 0, 0, 0, 0, "AutoCorrectTrayHotkeys", new IntPtr(-3));
        if (_source.Handle == IntPtr.Zero)
        {
            _source.Dispose();
            _source = new HwndSource(0, 0, 0, 0, 0, "AutoCorrectTrayHotkeys", IntPtr.Zero);
        }
        _hwnd = _source.Handle;
        _source.AddHook(_hook);
        _svc.Log.Info($"Hotkey window created (hwnd={_hwnd})");
    }

    /// <summary>
    /// Temporarily release all hotkeys (recording mode in settings): registered
    /// combos are captured by the system before the recording box sees them.
    /// </summary>
    public void SuspendAll()
    {
        EnsureWindow();
        UnregisterAll();
        _svc.Log.Info("Hotkeys suspended (recording a new combo)");
    }

    private void Register(int id, string? text)
    {
        var combo = HotkeyCombo.Parse(text);
        if (combo is not { } c)
        {
            _svc.Log.Info($"Hotkey #{id} not set or invalid: \"{text}\"");
            return;
        }
        if (!NativeMethods.RegisterHotKey(_hwnd, id, c.Modifiers | NativeMethods.MOD_NOREPEAT, c.VirtualKey))
        {
            _svc.Log.Info($"Failed to register hotkey \"{text}\" (#{id}) - it may be in use by another program");
            // On first startup Tray does not exist yet; show the warning on re-registration
            _svc.Tray?.ShowBalloon("Hotkeys",
                $"Could not register \"{text}\" - the combo may already be in use by another program.");
        }
        else
            _svc.Log.Info($"Hotkey #{id} = {text}");
    }

    private void UnregisterAll()
    {
        for (int id = 1; id <= 4; id++)
            NativeMethods.UnregisterHotKey(_hwnd, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            handled = true;
            Fired?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterAll();
            _hwnd = IntPtr.Zero;
        }
        _source?.Dispose();
        _source = null;
    }
}
