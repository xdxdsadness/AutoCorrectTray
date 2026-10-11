using System.Runtime.InteropServices;
using System.Windows.Threading;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Hooks;

/// <summary>
/// Global WH_MOUSE_LL mouse hook:
/// intercepts RMB for the fix menu (with suppression and re-sending),
/// hides popups on click-away and scroll.
/// </summary>
public sealed class MouseHookService : IDisposable
{
    private readonly AppServices _svc;
    private readonly NativeMethods.LowLevelHookProc _proc;
    private IntPtr _hook;
    private bool _interceptingRightClick;
    private NativeMethods.POINT _lastClickPt;
    private uint _lastClickTime;

    public static NativeMethods.POINT LastMousePosition;

    public MouseHookService(AppServices svc)
    {
        _svc = svc;
        _proc = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            _svc.Log.Error($"Failed to install the mouse hook: {Marshal.GetLastWin32Error()}");
        else
            _svc.Log.Info("Mouse hook installed");
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if ((info.flags & NativeMethods.LLMHF_INJECTED) == 0)
                {
                    switch (wParam.ToInt32())
                    {
                        case NativeMethods.WM_MOUSEMOVE:
                            LastMousePosition = info.pt;
                            break;

                        case NativeMethods.WM_LBUTTONDOWN:
                        {
                            bool isDoubleClick = IsDoubleClick(info);
                            // A click outside our windows resets the buffer: the caret may have moved
                            _svc.NotifyExternalClick(info.pt);
                            _svc.Dispatcher.BeginInvoke(new Action(() => _svc.Popups.HandleClickAway(info.pt)));
                            if (isDoubleClick)
                                _svc.Dispatcher.BeginInvoke(new Action(() => _svc.HandleDoubleClick(info.pt)));
                            break;
                        }

                        case NativeMethods.WM_LBUTTONUP:
                            // LMB release - mouse/double-click selection finished:
                            // if the translate window is open, refresh the translation
                            _svc.Dispatcher.BeginInvoke(new Action(() => _svc.HandleSelectionMaybeChanged(info.pt)));
                            break;

                        case NativeMethods.WM_MOUSEWHEEL:
                            _svc.Dispatcher.BeginInvoke(new Action(() => _svc.Popups.HideAll()));
                            break;

                        case NativeMethods.WM_RBUTTONDOWN:
                            if (ShouldIntercept())
                            {
                                _interceptingRightClick = true;
                                _svc.Dispatcher.BeginInvoke(new Action(() => _svc.HandleRightClickIntercepted(info.pt)));
                                return (IntPtr)1; // suppress; decide after the quick word check
                            }
                            break;

                        case NativeMethods.WM_RBUTTONUP:
                            if (_interceptingRightClick)
                            {
                                _interceptingRightClick = false;
                                return (IntPtr)1;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _svc.Log.Error("Mouse hook error", ex);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Quick checks only - the word check itself runs asynchronously.</summary>
    private bool ShouldIntercept() =>
        !_svc.Paused &&
        _svc.Settings.RightClickFix &&
        _svc.Settings.SpellEnabled &&
        _svc.Exclusions.IsCurrentAppAllowed;

    /// <summary>Second click within the system double-click time/distance.</summary>
    private bool IsDoubleClick(NativeMethods.MSLLHOOKSTRUCT info)
    {
        uint now = info.time;
        var prevPt = _lastClickPt;
        uint prev = _lastClickTime;
        _lastClickTime = now;
        _lastClickPt = info.pt;

        return Input.DoubleClickDetector.IsDoubleClick(
            now, info.pt.X, info.pt.Y,
            prev, prevPt.X, prevPt.Y,
            NativeMethods.GetDoubleClickTime(),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK));
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
