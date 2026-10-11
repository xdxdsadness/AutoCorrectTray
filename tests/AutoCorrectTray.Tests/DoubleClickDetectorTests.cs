using AutoCorrectTray.Input;
using Xunit;

namespace AutoCorrectTray.Tests;

public class DoubleClickDetectorTests
{
    [Fact]
    public void FastSecondClick_SamePlace_IsDouble()
    {
        Assert.True(DoubleClickDetector.IsDoubleClick(
            nowMs: 10_500, x: 100, y: 200,
            prevMs: 10_300, prevX: 100, prevY: 200,
            intervalMs: 500, toleranceX: 4, toleranceY: 4));
    }

    [Fact]
    public void SlowSecondClick_IsNotDouble()
    {
        Assert.False(DoubleClickDetector.IsDoubleClick(
            nowMs: 11_000, x: 100, y: 200,
            prevMs: 10_300, prevX: 100, prevY: 200,
            intervalMs: 500, toleranceX: 4, toleranceY: 4));
    }

    [Fact]
    public void FarSecondClick_IsNotDouble()
    {
        Assert.False(DoubleClickDetector.IsDoubleClick(
            nowMs: 10_400, x: 150, y: 260,
            prevMs: 10_300, prevX: 100, prevY: 200,
            intervalMs: 500, toleranceX: 4, toleranceY: 4));
    }

    [Fact]
    public void FirstClickEver_IsNotDouble()
    {
        // prevMs = 0 — состояния «прошлого клика» ещё нет
        Assert.False(DoubleClickDetector.IsDoubleClick(
            nowMs: 500, x: 100, y: 200,
            prevMs: 0, prevX: 0, prevY: 0,
            intervalMs: 500, toleranceX: 4, toleranceY: 4));
    }

    [Fact]
    public void WithinTolerance_Wraps_IsDouble()
    {
        // Допуск по X/Y включительный, смещение на 1 пиксель — обычное дело
        Assert.True(DoubleClickDetector.IsDoubleClick(
            nowMs: 10_450, x: 101, y: 199,
            prevMs: 10_300, prevX: 100, prevY: 200,
            intervalMs: 500, toleranceX: 4, toleranceY: 4));
    }
}
