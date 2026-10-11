using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Ui;

/// <summary>
/// Region selection with the mouse (like Snipping Tool, but no screenshot):
/// the screen dims, the user outlines a rectangle - its rectangle in physical
/// pixels is passed out. Esc or a plain click cancels the selection.
/// The shading updates on a timer (~25 fps), the border on every move:
/// this keeps dragging smooth even on a large layered window.
/// </summary>
public partial class RegionSelector : Window
{
    private Action<Rect>? _onSelected;
    private Point? _start;
    private bool _dragging;
    private Point _lastMove;
    private DispatcherTimer? _shadeTimer;

    public RegionSelector()
    {
        InitializeComponent();

        _shadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _shadeTimer.Tick += (_, _) =>
        {
            if (_dragging) UpdateShades(CurrentRect(_lastMove));
        };

        Canvas.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(this);
            _lastMove = _start.Value;
            _dragging = true;
            Canvas.CaptureMouse();
            SelectionBox.Visibility = Visibility.Visible;
            UpdateBorder(_start.Value);
            _shadeTimer.Start();
            e.Handled = true;
        };
        Canvas.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            _lastMove = e.GetPosition(this);
            UpdateBorder(_lastMove);
        };
        Canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            Canvas.ReleaseMouseCapture();
            _shadeTimer.Stop();
            var p = e.GetPosition(this);
            Update(p);
            var rect = CurrentRect(p);
            Finish(rect.Width > 12 && rect.Height > 12 ? ToPhysical(rect) : null);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(null);
        };
    }

    /// <summary>Opens the selection mode. The callback runs on the UI thread after completion.</summary>
    public void Start(Action<Rect> onSelected)
    {
        _onSelected = onSelected;
        _start = null;
        _dragging = false;
        SelectionBox.Visibility = Visibility.Hidden;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        UpdateShades(new Rect(0, 0, 0, 0));

        Show();
        PositionHintOnMouseMonitor();
        Activate();
        Focus();
    }

    /// <summary>Hint - centered on the monitor where the cursor is (not across two monitors).</summary>
    private void PositionHintOnMouseMonitor()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var mouse = Hooks.MouseHookService.LastMousePosition;
                var hMon = NativeMethods.MonitorFromPoint(
                    new NativeMethods.POINT { X = mouse.X, Y = mouse.Y },
                    NativeMethods.MONITOR_DEFAULTTONEAREST);
                var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                double scale = 1;
                if (NativeMethods.GetMonitorInfo(hMon, ref info))
                {
                    scale = NativeMethods.GetScaleForPoint(new NativeMethods.POINT
                    {
                        X = (info.rcMonitor.Left + info.rcMonitor.Right) / 2,
                        Y = (info.rcMonitor.Top + info.rcMonitor.Bottom) / 2,
                    });
                }
                if (scale <= 0) scale = 1;

                // Monitor center in window coordinates (the window covers the whole virtual screen)
                double monitorCenterX = (info.rcMonitor.Left + info.rcMonitor.Right) / 2.0 / scale
                                        - SystemParameters.VirtualScreenLeft;
                double hintTop = Math.Max(26, info.rcMonitor.Top / scale - SystemParameters.VirtualScreenTop + 26);

                double hintWidth = HintBorder.ActualWidth > 0 ? HintBorder.ActualWidth : 460;
                double x = Math.Clamp(monitorCenterX - hintWidth / 2, 8,
                    Math.Max(8, ActualWidth - hintWidth - 8));
                Canvas.SetLeft(HintBorder, x);
                Canvas.SetTop(HintBorder, hintTop);
            }
            catch
            {
                Canvas.SetLeft(HintBorder, 24);
                Canvas.SetTop(HintBorder, 26);
            }
        }), DispatcherPriority.Loaded);
    }

    private Rect CurrentRect(Point p)
    {
        if (_start is not { } s) return new Rect();
        return new Rect(
            Math.Min(s.X, p.X),
            Math.Min(s.Y, p.Y),
            Math.Abs(p.X - s.X),
            Math.Abs(p.Y - s.Y));
    }

    private void Update(Point p)
    {
        UpdateBorder(p);
        UpdateShades(CurrentRect(p));
    }

    /// <summary>Selection border only (cheap, on every mouse move).</summary>
    private void UpdateBorder(Point p)
    {
        var r = CurrentRect(p);
        Canvas.SetLeft(SelectionBox, r.Left);
        Canvas.SetTop(SelectionBox, r.Top);
        SelectionBox.Width = r.Width;
        SelectionBox.Height = r.Height;
    }

    /// <summary>Shading around the selected area; with no area yet, the whole screen is shaded.</summary>
    private void UpdateShades(Rect r)
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (r.Width <= 0 || r.Height <= 0)
        {
            // Nothing selected: shade the whole screen
            SetRect(ShadeTop, 0, 0, w, h);
            SetRect(ShadeBottom, 0, 0, 0, 0);
            SetRect(ShadeLeft, 0, 0, 0, 0);
            SetRect(ShadeRight, 0, 0, 0, 0);
            return;
        }

        SetRect(ShadeTop, 0, 0, w, r.Top);
        SetRect(ShadeBottom, 0, r.Bottom, w, h - r.Bottom);
        SetRect(ShadeLeft, 0, r.Top, r.Left, r.Height);
        SetRect(ShadeRight, r.Right, r.Top, w - r.Right, r.Height);
    }

    private static void SetRect(Rectangle rect, double x, double y, double w, double h)
    {
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        rect.Width = Math.Max(0, w);
        rect.Height = Math.Max(0, h);
    }

    private Rect ToPhysical(Rect dipRect)
    {
        // The window covers the whole virtual screen: DIP → physical pixels via the window
        var tl = PointToScreen(dipRect.TopLeft);
        var br = PointToScreen(dipRect.BottomRight);
        return new Rect(tl, br);
    }

    private void Finish(Rect? physical)
    {
        Hide();
        _dragging = false;
        _start = null;
        _shadeTimer?.Stop();
        SelectionBox.Visibility = Visibility.Hidden;
        var callback = _onSelected;
        _onSelected = null;
        if (physical is { } r && callback != null) callback(r);
    }
}
