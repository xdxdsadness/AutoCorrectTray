using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace AutoCorrectTray.Ui;

/// <summary>
/// Dragging a frameless window the system way: WM_NCLBUTTONDOWN + HTCAPTION.
/// Windows itself runs the modal move loop (smooth DWM drag); our code does not
/// handle MouseMove per pixel, so dragging stays smooth even when windows are busy.
/// Works for WS_EX_NOACTIVATE popups: the system moves them without activation.
/// The message is sent AFTER WPF finishes the button-down processing (BeginInvoke):
/// a synchronous modal loop inside the handler intermittently swallows the first
/// drag attempt and leaves the mouse in a stuck state.
/// </summary>
internal static class WindowDragHelper
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Attach(FrameworkElement dragBar, Window window, Action? onDragEnd = null)
    {
        dragBar.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (dragBar.IsMouseCaptured) dragBar.ReleaseMouseCapture();

            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Plain click without movement - do not start a drag
                if (Mouse.LeftButton != MouseButtonState.Pressed) return;
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                onDragEnd?.Invoke();
            }));
        };
    }
}
