using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.TextCapture;

public sealed record CaretWordInfo(string Word, Rect? Rect, bool IsPassword, bool IsEditable);

/// <summary>
/// Reads text from other applications via UI Automation: the word at the
/// caret, selected text, password field detection. All methods are blocking;
/// call from a background thread.
/// </summary>
public sealed class UiTextService
{
    private static readonly Regex WordTailRegex = new(@"[\p{L}\p{Nd}\p{Mn}''\-]+$", RegexOptions.Compiled);

    /// <summary>Word at the caret of the focused field. The caret moves on RMB click in most apps.</summary>
    public CaretWordInfo? GetCaretWord()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;

            bool isPassword = IsPasswordElement(element);
            bool editable = IsEditable(element);
            if (!editable)
                return new CaretWordInfo(string.Empty, NativeCaretRect(), isPassword, false);
            if (isPassword)
                return new CaretWordInfo(string.Empty, null, true, true);

            if (element.GetCurrentPattern(TextPattern.Pattern) is TextPattern textPattern)
            {
                var selection = textPattern.GetSelection();
                if (selection is { Length: > 0 })
                {
                    var range = selection[0];
                    var text = SafeRangeText(range);
                    if (string.IsNullOrEmpty(text))
                    {
                        range = range.Clone();
                        range.ExpandToEnclosingUnit(TextUnit.Word);
                        text = SafeRangeText(range);
                    }
                    text = text.Trim();
                    if (text.Length > 0)
                    {
                        var rect = FirstBoundingRect(range) ?? NativeCaretRect();
                        return new CaretWordInfo(text, rect, false, true);
                    }
                }
            }

            if (element.GetCurrentPattern(ValuePattern.Pattern) is ValuePattern valuePattern)
            {
                var value = valuePattern.Current.Value ?? string.Empty;
                var match = WordTailRegex.Match(value);
                return new CaretWordInfo(match.Value, NativeCaretRect(), false, true);
            }

            return new CaretWordInfo(string.Empty, NativeCaretRect(), isPassword, editable);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Selects the word at the caret in the app itself (TextPattern.Select) -
    /// the next typed text replaces the selection. Exact, no backspace counting.
    /// </summary>
    public bool TrySelectCaretWord(string expectedWord)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element?.GetCurrentPattern(TextPattern.Pattern) is not TextPattern textPattern)
                return false;

            var selection = textPattern.GetSelection();
            if (selection is not { Length: > 0 })
                return false;

            var range = selection[0].Clone();
            range.ExpandToEnclosingUnit(TextUnit.Word);
            var text = SafeRangeText(range).Trim();
            if (!text.Equals(expectedWord, StringComparison.OrdinalIgnoreCase))
                return false;

            range.Select();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Selected word and its screen rectangle (after a double click).</summary>
    public (string Word, Rect? Rect)? GetSelectedWord()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element?.GetCurrentPattern(TextPattern.Pattern) is not TextPattern textPattern)
                return null;

            var selection = textPattern.GetSelection();
            if (selection is not { Length: > 0 })
                return null;

            var range = selection[0];
            var text = SafeRangeText(range)?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;
            return (text, FirstBoundingRect(range));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Text selected in the focused window (via TextPattern).</summary>
    public string? GetSelectedText()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return null;
            if (element.GetCurrentPattern(TextPattern.Pattern) is TextPattern textPattern)
            {
                var selection = textPattern.GetSelection();
                if (selection is { Length: > 0 })
                {
                    var text = selection[0].GetText(-1);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        catch { }
        return null;
    }

    private static Rect? UnionRect(TextPatternRange range)
    {
        try
        {
            var rects = range.GetBoundingRectangles();
            if (rects is not { Length: > 0 }) return null;
            var result = rects[0];
            foreach (var r in rects.Skip(1)) result.Union(r);
            return result;
        }
        catch { return null; }
    }

    private static Rect? ElementRect(AutomationElement element)
    {
        try
        {
            var r = element.Current.BoundingRectangle;
            return r.IsEmpty ? null : r;
        }
        catch { return null; }
    }

    /// <summary>
    /// Text lines inside the selected region with their screen rectangles -
    /// for the "translate on top of text" overlay. Works even where text cannot
    /// be selected (Windows accessibility, no screenshots).
    /// Text in a region is often split across elements (plain text + links),
    /// so lines are collected from ALL text elements of the window and merged
    /// without overlaps; the fallback is element names (icons, buttons).
    /// Call from a background thread.
    /// </summary>
    public List<(string Text, Rect Rect, bool LabelStrip)>? GetLinesInRegion(Rect region, NativeMethods.POINT seedPoint)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(seedPoint.X, seedPoint.Y));
            if (element == null || IsPasswordElement(element)) return null;

            var top = ClimbToTopWindow(element);

            // Candidates: closest to the region first (element under the point
            // + its ancestors), then the most text-heavy elements of the window
            var candidates = new List<TextPattern>();
            var probe = element;
            for (int depth = 0; probe != null && depth < 5; depth++)
            {
                try
                {
                    if (probe.GetCurrentPattern(TextPattern.Pattern) is TextPattern tp && !candidates.Contains(tp))
                        candidates.Add(tp);
                }
                catch { }
                probe = TreeWalker.RawViewWalker.GetParent(probe);
            }
            foreach (var tp in FindTextPatterns(top, 4))
                if (!candidates.Contains(tp))
                    candidates.Add(tp);

            // Lines from all candidates, merged without overlaps: parts of a
            // line living in neighboring elements (links, highlights) fill gaps
            var lines = new List<(string Text, Rect Rect, bool LabelStrip)>();
            foreach (var candidate in candidates)
            {
                var add = CollectLines(candidate, region, 40 - lines.Count);
                if (add == null) continue;
                foreach (var l in add)
                {
                    if (lines.Count >= 40) break;
                    if (!lines.Any(e => Overlaps(e.Rect, l.Rect)))
                        lines.Add(l);
                }
                if (lines.Count >= 40) break;
            }

            if (lines.Count > 0) return lines;

            // Fallback: element names (Name) in the region - icons, buttons, items
            return CollectNamesInRegion(top, region);
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement ClimbToTopWindow(AutomationElement element)
    {
        var top = element;
        for (int i = 0; i < 15; i++)
        {
            var parent = TreeWalker.RawViewWalker.GetParent(top);
            if (parent == null || parent == AutomationElement.RootElement) break;
            top = parent;
        }
        return top;
    }

    /// <summary>Whether the added rectangle overlaps an existing one by more than half.</summary>
    private static bool Overlaps(Rect existing, Rect added)
    {
        var inter = Rect.Intersect(existing, added);
        if (inter.IsEmpty) return false;
        double area = added.Width * added.Height;
        return area <= 0 || inter.Width * inter.Height > area * 0.5;
    }

    /// <summary>Text elements of the window, sorted by amount of text.</summary>
    private static List<TextPattern> FindTextPatterns(AutomationElement top, int take)
    {
        var found = new List<(TextPattern Tp, int Len)>();
        try
        {
            var queue = new Queue<(AutomationElement El, int Depth)>();
            queue.Enqueue((top, 0));
            int visited = 0;
            while (queue.Count > 0 && visited < 300)
            {
                var (el, depth) = queue.Dequeue();
                visited++;
                try
                {
                    if (!IsPasswordElement(el) && el.GetCurrentPattern(TextPattern.Pattern) is TextPattern tp)
                        found.Add((tp, tp.DocumentRange.GetText(4001)?.Length ?? 0));
                }
                catch { }

                if (depth < 6)
                {
                    try
                    {
                        foreach (var kid in el.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition)
                                     .Cast<AutomationElement>())
                            queue.Enqueue((kid, depth + 1));
                    }
                    catch { }
                }
            }
        }
        catch { }
        return found.OrderByDescending(f => f.Len).Take(take).Select(f => f.Tp).ToList();
    }

    /// <summary>TextPattern lines intersecting the region.</summary>
    private static List<(string Text, Rect Rect, bool LabelStrip)>? CollectLines(TextPattern textPattern, Rect region, int max)
    {
        try
        {
            var lines = new List<(string Text, Rect Rect, bool LabelStrip)>();
            var range = textPattern.DocumentRange.Clone();
            range.ExpandToEnclosingUnit(TextUnit.Line);
            for (int i = 0; i < 400 && lines.Count < max; i++)
            {
                var text = SafeRangeText(range).Trim();
                var rect = UnionRect(range);
                if (!string.IsNullOrWhiteSpace(text) && text.Any(char.IsLetter) &&
                    rect is { } r && r.IntersectsWith(region) &&
                    (lines.Count == 0 || lines[^1].Item1 != text))
                {
                    lines.Add((Cap(text, 500), r, false));
                }
                if (range.Move(TextUnit.Line, 1) == 0) break;
                range.ExpandToEnclosingUnit(TextUnit.Line);
            }
            return lines;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fallback for regions without TextPattern (desktop, icon lists, button
    /// panels): names (Name) of elements inside the region. Containers covering
    /// smaller labels are discarded - otherwise one huge plate would hide
    /// all the small text.
    /// </summary>
    private static List<(string Text, Rect Rect, bool LabelStrip)>? CollectNamesInRegion(AutomationElement top, Rect region)
    {
        try
        {
            var lines = new List<(string Text, Rect Rect, bool LabelStrip)>();
            var queue = new Queue<(AutomationElement El, int Depth)>();
            queue.Enqueue((top, 0));
            int visited = 0;
            double regionArea = region.Width * region.Height;
            while (queue.Count > 0 && visited < 300 && lines.Count < 40)
            {
                var (el, depth) = queue.Dequeue();
                visited++;
                try
                {
                    if (!IsPasswordElement(el))
                    {
                        var name = el.Current.Name?.Trim();
                        var r = el.Current.BoundingRectangle;
                        if (!string.IsNullOrWhiteSpace(name) && name.Any(char.IsLetter) &&
                            !r.IsEmpty && r.IntersectsWith(region) && r.Width > 8 && r.Height > 4 &&
                            // element no larger than the region itself - otherwise a container
                            r.Width <= region.Width && r.Height <= region.Height &&
                            r.Width * r.Height <= regionArea * 0.6 &&
                            !lines.Any(e => e.Item1 == name && e.Rect == r))
                        {
                            lines.Add((Cap(name, 300), r, r.Height > 36));
                        }
                    }
                }
                catch { }

                if (depth < 6)
                {
                    try
                    {
                        foreach (var kid in el.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition)
                                     .Cast<AutomationElement>())
                            queue.Enqueue((kid, depth + 1));
                    }
                    catch { }
                }
            }

            // Drop containers fully covering smaller labels
            var filtered = lines
                .Where(l => !lines.Any(o =>
                    o.Rect != l.Rect &&
                    o.Rect.Width * o.Rect.Height < l.Rect.Width * l.Rect.Height &&
                    ContainsRect(l.Rect, o.Rect)))
                .ToList();
            return filtered.Count > 0 ? filtered : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsRect(Rect big, Rect small) =>
        big.Left <= small.Left + 2 && big.Top <= small.Top + 2 &&
        big.Right >= small.Right - 2 && big.Bottom >= small.Bottom - 2;

    private static string Cap(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";

    /// <summary>
    /// Diagnostics for capture failures: which app and element is under the
    /// cursor, and whether it exposes text via the accessibility layer.
    /// Call from a background thread.
    /// </summary>
    public string DescribePoint(NativeMethods.POINT screenPoint)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(screenPoint.X, screenPoint.Y));
            if (element == null) return "element not found";

            string procName = "";
            try
            {
                var p = System.Diagnostics.Process.GetProcessById(element.Current.ProcessId);
                procName = p.ProcessName;
            }
            catch { }

            string controlType = "", className = "", name = "";
            bool hasText = false, hasValue = false;
            try
            {
                controlType = element.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "?";
                className = element.Current.ClassName ?? "";
                name = element.Current.Name ?? "";
                hasText = element.GetCurrentPattern(TextPattern.Pattern) != null;
                hasValue = element.GetCurrentPattern(ValuePattern.Pattern) != null;
            }
            catch { }

            return $"app {procName}: {controlType} \"{Cap(className, 40)}\"" +
                   $", text{(hasText ? " available" : " NOT available")}, value{(hasValue ? " available" : " unavailable")}" +
                   $", name \"{Cap(name, 40)}\"";
        }
        catch (Exception ex)
        {
            return "UIA error: " + ex.Message;
        }
    }

    public bool IsFocusedElementPassword()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return element != null && IsPasswordElement(element);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEditable(AutomationElement element)
    {
        try
        {
            var controlType = element.Current.ControlType;
            bool byType = controlType == ControlType.Edit || controlType == ControlType.Document;
            bool hasValue = element.GetCurrentPattern(ValuePattern.Pattern) != null;
            bool hasText = element.GetCurrentPattern(TextPattern.Pattern) != null;
            return byType || hasValue || hasText;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPasswordElement(AutomationElement element)
    {
        try
        {
            if (element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true) is bool isPassword && isPassword)
                return true;

            var hwnd = (IntPtr)element.Current.NativeWindowHandle;
            if (hwnd != IntPtr.Zero)
            {
                var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
                if ((style & NativeMethods.ES_PASSWORD) != 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static string SafeRangeText(TextPatternRange range)
    {
        try { return range.GetText(-1) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static Rect? FirstBoundingRect(TextPatternRange range)
    {
        try
        {
            var rects = range.GetBoundingRectangles();
            if (rects is { Length: > 0 })
                return rects[0];
        }
        catch { }
        return null;
    }

    /// <summary>Fast native way: system caret position (Win32 caret fields; usually unavailable in browsers).</summary>
    public static Rect? NativeCaretRect()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var gti = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (!NativeMethods.GetGUIThreadInfo(threadId, ref gti) || gti.hwndCaret == IntPtr.Zero)
                return null;

            var point = new NativeMethods.POINT { X = gti.rcCaret.Left, Y = gti.rcCaret.Bottom };
            if (!NativeMethods.ClientToScreen(gti.hwndCaret, ref point))
                return null;
            int height = Math.Max(1, gti.rcCaret.Bottom - gti.rcCaret.Top);
            return new Rect(point.X, point.Y - height, 1, height);
        }
        catch
        {
            return null;
        }
    }
}
