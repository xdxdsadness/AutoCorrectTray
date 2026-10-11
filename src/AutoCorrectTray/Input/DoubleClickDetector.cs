namespace AutoCorrectTray.Input;

/// <summary>Double-click detection math. Pure logic - covered by tests.</summary>
public static class DoubleClickDetector
{
    /// <summary>
    /// A second click is a double click if it arrives within the system
    /// double-click interval and X/Y tolerance (SM_CXDOUBLECLK/SM_CYDOUBLECLK).
    /// The first click (prevMs = 0) never counts as a double click.
    /// </summary>
    public static bool IsDoubleClick(uint nowMs, int x, int y,
        uint prevMs, int prevX, int prevY,
        uint intervalMs, int toleranceX, int toleranceY)
    {
        if (prevMs == 0) return false;
        return nowMs >= prevMs &&
               nowMs - prevMs <= intervalMs &&
               Math.Abs(x - prevX) <= toleranceX &&
               Math.Abs(y - prevY) <= toleranceY;
    }
}
