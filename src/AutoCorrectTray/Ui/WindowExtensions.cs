using AutoCorrectTray.Native;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AutoCorrectTray.Ui;

/// <summary>Window helpers: no-activate style, topmost maintenance.</summary>
internal static class WindowExtensions
{
    public static void SetNoActivate(this Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);
        window.RoundCorners();
    }

    /// <summary>Rounded corners from DWM (Windows 11): no layered window, dragging unaffected.</summary>
    public static void RoundCorners(this Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int pref = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// TOPMOST + tool window, but WITHOUT WS_EX_NOACTIVATE: the window does not
    /// steal focus on appearing (ShowActivated=False), yet activates on click -
    /// needed so text selection with the mouse and Ctrl+C work in the overlay.
    /// </summary>
    public static void SetTopmostToolWindow(this Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)exStyle);
        window.RoundCorners();
    }

    /// <summary>Themed dark context menu instead of the standard white one (Copy on RMB).</summary>
    public static void UseThemedCopyMenu(this TextBox tb)
    {
        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = L10n.T("popup.btn.copy") };
        copyItem.Click += (_, _) =>
        {
            try { if (!string.IsNullOrEmpty(tb.SelectedText)) Clipboard.SetText(tb.SelectedText); }
            catch { }
        };
        menu.Items.Add(copyItem);
        tb.ContextMenu = menu;
    }

    /// <summary>Dark title bar for regular windows (settings) in the dark theme.</summary>
    public static void UseDarkTitleBar(this Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// Re-asserts TOPMOST in the Z-order. Shell surfaces (search panel, toasts) and
    /// other topmost windows re-assert themselves and slide above an idle topmost
    /// window; this puts ours back on top. Does not steal focus (SWP_NOACTIVATE).
    /// </summary>
    public static void ReaffirmTopmost(this Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// While the window is visible, periodically re-asserts TOPMOST: shell flyouts
    /// (Windows search, toasts) keep re-asserting themselves above idle topmost
    /// windows. Cheap no-op ticks when the window is hidden.
    /// Skipped while an in-window ComboBox dropdown is open: raising the owner
    /// would sink its non-topmost dropdown popup behind the window.
    /// </summary>
    public static void EnableTopmostKeeper(this Window window)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            if (!window.IsVisible) return;
            if (HasOpenDropDown(window)) return;
            window.ReaffirmTopmost();
        };
        timer.Start();
    }

    private static bool HasOpenDropDown(Window window)
    {
        // Any open popup child of this window (ComboBox dropdowns are popups too)
        return VisualTreeHelperEx.FindOpenPopup(window) != null;
    }

    /// <summary>Leaves the TOPMOST band (dropdown popups are non-topmost and would
    /// render below a topmost owner).</summary>
    public static void RemoveTopmost(this Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// A topmost owner always renders above its non-topmost ComboBox dropdown, so
    /// the language list sinks behind the window. Fix: while a dropdown is open we
    /// leave the TOPMOST band, and restore TOPMOST when it closes.
    /// </summary>
    public static void EnableDropDownTopmostFix(this Window window)
    {
        foreach (var combo in ComboBoxesOf(window))
        {
            combo.DropDownOpened += (_, _) =>
            {
                try { window.RemoveTopmost(); } catch { }
            };
            combo.DropDownClosed += (_, _) =>
            {
                try { if (window.IsVisible) window.ReaffirmTopmost(); } catch { }
            };
        }
    }

    private static IEnumerable<System.Windows.Controls.ComboBox> ComboBoxesOf(System.Windows.Media.Visual root)
    {
        var result = new List<System.Windows.Controls.ComboBox>();
        Collect(root, result);
        return result;
    }

    private static void Collect(System.Windows.DependencyObject? node, List<System.Windows.Controls.ComboBox> into)
    {
        if (node == null) return;
        if (node is System.Windows.Controls.ComboBox cb) into.Add(cb);
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            Collect(System.Windows.Media.VisualTreeHelper.GetChild(node, i), into);
    }
}