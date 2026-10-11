using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Ui;

/// <summary>
/// The translate window follows the monitor of the active application: the user
/// should not have to drag the popup to where they are typing. Monitor and window
/// coordinates are physical pixels (reliable across DPI); conversion to WPF DIPs
/// uses the scale of the target monitor. The move itself goes through SetWindowPos
/// without activation.
/// </summary>
internal static class MonitorFollow
{
    /// <summary>
    /// Pure geometry (covered by tests): shift the window by the delta of the
    /// monitor origins and clamp to the working area of the target monitor.
    /// </summary>
    public static NativeMethods.RECT ShiftIntoMonitor(NativeMethods.RECT window, NativeMethods.RECT fromMonitor, NativeMethods.RECT toMonitor, NativeMethods.RECT toWorkArea)
    {
        int width = window.Right - window.Left;
        int height = window.Bottom - window.Top;
        int left = window.Left + (toMonitor.Left - fromMonitor.Left);
        int top = window.Top + (toMonitor.Top - fromMonitor.Top);

        int workW = toWorkArea.Right - toWorkArea.Left;
        int workH = toWorkArea.Bottom - toWorkArea.Top;
        if (width >= workW) left = toWorkArea.Left;
        else left = Math.Clamp(left, toWorkArea.Left, toWorkArea.Right - width);
        if (height >= workH) top = toWorkArea.Top;
        else top = Math.Clamp(top, toWorkArea.Top, toWorkArea.Bottom - height);

        return new NativeMethods.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    /// <summary>
    /// Opening at a saved position: if it is on a different monitor than the
    /// active window, move the position to its monitor before Show (no flicker
    /// on the old monitor). The window does not exist yet, so physical sizes are
    /// computed via the target monitor scale.
    /// </summary>
    public static void SnapBeforeShow(Window window, IntPtr foregroundHwnd)
    {
        try
        {
            if (foregroundHwnd == IntPtr.Zero) return;
            if (InfoOf(NativeMethods.MonitorFromWindow(foregroundHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST)) is not { } target)
                return;

            double scale = NativeMethods.GetScaleForPoint(Center(target.rcMonitor));
            if (scale <= 0) scale = 1;

            double dipW = window.Width > 0 ? window.Width : (window.ActualWidth > 0 ? window.ActualWidth : 480);
            double dipH = window.ActualHeight > 0 ? window.ActualHeight : 280;

            var win = new NativeMethods.RECT
            {
                Left = (int)Math.Round(window.Left * scale),
                Top = (int)Math.Round(window.Top * scale),
            };
            win.Right = win.Left + (int)Math.Round(dipW * scale);
            win.Bottom = win.Top + (int)Math.Round(dipH * scale);

            var probe = new NativeMethods.POINT { X = win.Left, Y = win.Top };
            if (InfoOf(NativeMethods.MonitorFromPoint(probe, NativeMethods.MONITOR_DEFAULTTONEAREST)) is not { } from)
                return;
            if (from.rcMonitor.Left == target.rcMonitor.Left && from.rcMonitor.Top == target.rcMonitor.Top)
                return; // already on the active window's monitor

            var moved = ShiftIntoMonitor(win, from.rcMonitor, target.rcMonitor, target.rcWork);
            window.Left = moved.Left / scale;
            window.Top = moved.Top / scale;
        }
        catch { }
    }

    /// <summary>
    /// Moves a visible window to the monitor of the active window: shift by the
    /// origin delta, clamp to the working area, SWP_NOACTIVATE - focus untouched.
    /// </summary>
    public static void MoveToMonitorOfWindow(Window window, IntPtr foregroundHwnd)
    {
        try
        {
            if (foregroundHwnd == IntPtr.Zero || !window.IsVisible) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (!NativeMethods.GetWindowRect(hwnd, out var win)) return;

            var fromH = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var toH = NativeMethods.MonitorFromWindow(foregroundHwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (fromH == IntPtr.Zero || toH == IntPtr.Zero || fromH == toH) return;
            if (InfoOf(fromH) is not { } from || InfoOf(toH) is not { } target)
                return;

            double scaleFrom = NativeMethods.GetScaleForPoint(Center(from.rcMonitor));
            double scaleTo = NativeMethods.GetScaleForPoint(Center(target.rcMonitor));
            if (scaleFrom <= 0) scaleFrom = 1;
            if (scaleTo <= 0) scaleTo = 1;

            // Size after the DPI change (WPF keeps it when moving between monitors):
            // account for it in the clamp so the window does not go off the edge
            int width = win.Right - win.Left, height = win.Bottom - win.Top;
            int newW = (int)Math.Round(width * scaleTo / scaleFrom);
            int newH = (int)Math.Round(height * scaleTo / scaleFrom);

            var moved = ShiftIntoMonitor(
                new NativeMethods.RECT { Left = win.Left, Top = win.Top, Right = win.Left + newW, Bottom = win.Top + newH },
                from.rcMonitor, target.rcMonitor, target.rcWork);

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, moved.Left, moved.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

            window.Left = moved.Left / scaleTo;
            window.Top = moved.Top / scaleTo;
        }
        catch { }
    }

    private static NativeMethods.MONITORINFO? InfoOf(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero) return null;
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        return NativeMethods.GetMonitorInfo(hMonitor, ref info) ? info : null;
    }

    private static NativeMethods.POINT Center(NativeMethods.RECT r) =>
        new() { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
}
