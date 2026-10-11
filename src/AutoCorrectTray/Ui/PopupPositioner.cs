using System.Windows;
using System.Windows.Threading;
using AutoCorrectTray.Hooks;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Ui;

/// <summary>Shows popups with monitor DPI awareness and clamping to the working area.</summary>
internal static class PopupPositioner
{
    /// <summary>Shows a window: first off-screen, then positions it (anchor is physical pixels).</summary>
    public static void ShowPositioned(Window window, Rect? anchorPhysical, double gapPx = 6)
    {
        window.Left = -30000;
        window.Top = -30000;
        window.Show();
        window.ReaffirmTopmost();

        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            try
            {
                var anchorPoint = anchorPhysical.HasValue
                    ? new NativeMethods.POINT
                    {
                        X = (int)(anchorPhysical.Value.Left + anchorPhysical.Value.Width / 2),
                        Y = (int)anchorPhysical.Value.Bottom,
                    }
                    : MouseHookService.LastMousePosition;

                double scale = NativeMethods.GetScaleForPoint(anchorPoint);
                if (scale <= 0) scale = 1;

                double width = window.ActualWidth;
                double height = window.ActualHeight;

                double x = anchorPoint.X / scale - width / 2;
                double y = (anchorPoint.Y + gapPx) / scale;

                var workArea = SystemParameters.WorkArea;
                if (x < workArea.Left + 8) x = workArea.Left + 8;
                if (x + width > workArea.Right - 8) x = workArea.Right - width - 8;
                if (y + height > workArea.Bottom - 8)
                    y = Math.Max(workArea.Top + 8, anchorPoint.Y / scale - height - gapPx / scale - 12);

                window.Left = x;
                window.Top = y;
            }
            catch
            {
                // positioning is not critical
            }
        }));
    }
}
