using AutoCorrectTray.Native;
using AutoCorrectTray.Ui;
using Xunit;

namespace AutoCorrectTray.Tests;

public class MonitorFollowTests
{
    private static NativeMethods.RECT Rect(int l, int t, int r, int b) =>
        new NativeMethods.RECT { Left = l, Top = t, Right = r, Bottom = b };

    [Fact]
    public void SameMonitor_KeepsPosition()
    {
        var mon = Rect(0, 0, 1920, 1080);
        var win = Rect(600, 400, 1080, 660);
        var moved = MonitorFollow.ShiftIntoMonitor(win, mon, mon, Rect(0, 0, 1920, 1040));
        Assert.Equal(600, moved.Left);
        Assert.Equal(400, moved.Top);
    }

    [Fact]
    public void ShiftsByMonitorDelta()
    {
        var from = Rect(0, 0, 1920, 1080);
        var to = Rect(1920, 0, 3840, 1080); // второй монитор справа
        var win = Rect(600, 400, 1080, 660);
        var moved = MonitorFollow.ShiftIntoMonitor(win, from, to, Rect(1920, 0, 3840, 1040));
        Assert.Equal(2520, moved.Left);
        Assert.Equal(400, moved.Top);
    }

    [Fact]
    public void ClampsIntoWorkArea()
    {
        var from = Rect(0, 0, 1920, 1080);
        var to = Rect(1920, 0, 3840, 1080);
        var win = Rect(1500, 400, 1980, 660); // у правого края первого монитора
        var moved = MonitorFollow.ShiftIntoMonitor(win, from, to, Rect(1920, 0, 3840, 1040));
        // после сдвига 3420..3900 — не влезает в рабочую область (до 3840), прижат к краю
        Assert.Equal(3840, moved.Right);
        Assert.Equal(3840 - 480, moved.Left);
    }

    [Fact]
    public void ShiftsToMonitorBelow()
    {
        var from = Rect(0, 0, 1920, 1080);
        var to = Rect(0, 1080, 1920, 2160); // второй монитор снизу
        var win = Rect(600, 400, 1080, 660);
        var moved = MonitorFollow.ShiftIntoMonitor(win, from, to, Rect(0, 1080, 1920, 2120));
        Assert.Equal(600, moved.Left);
        Assert.Equal(1480, moved.Top);
    }

    [Fact]
    public void WindowWiderThanWorkArea_GoesToWorkAreaOrigin()
    {
        var mon = Rect(0, 0, 1920, 1080);
        var win = Rect(100, 100, 2500, 400);
        var moved = MonitorFollow.ShiftIntoMonitor(win, mon, mon, Rect(0, 0, 1920, 1040));
        Assert.Equal(0, moved.Left);
        Assert.Equal(2500 - 100, moved.Right - moved.Left); // размер не меняется
    }
}
