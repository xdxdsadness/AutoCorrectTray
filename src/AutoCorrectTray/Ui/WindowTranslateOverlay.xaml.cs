using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Ui;

/// <summary>
/// Overlay for the whole window: translations sit on top of their lines ("lens"),
/// but lines of one paragraph merge into a single text block - mouse selection
/// spans the whole paragraph like normal text (LMB, double click, Ctrl+C, Ctrl+A).
/// The button on top copies all translations at once. Hides on click-away,
/// Esc, the hotkey again, and by itself after 25 seconds.
/// </summary>
public partial class WindowTranslateOverlay : Window
{
    private readonly AppServices _svc;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _autoHide;
    private readonly List<(Border Box, TextBox Edit, double BaseFont, double BoxHeight, List<int> LineIndices)> _placed = new();
    private List<string?> _translations = new();

    public WindowTranslateOverlay(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        CopyAllButton.Click += CopyAllClick;
        Canvas.PreviewMouseLeftButtonDown += (_, _) => RestartAutoHide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // TOPMOST + tool window, but WITHOUT WS_EX_NOACTIVATE: clicking a translation
        // activates the window so mouse selection and Ctrl+C work like in normal text
        this.SetTopmostToolWindow();
    }

    /// <summary>Show the overlay above a region (all coordinates are physical pixels).</summary>
    public void ShowFor(IReadOnlyList<(string Text, Rect Rect, bool LabelStrip)> lines, Rect windowRect)
    {
        Hide();
        _cts = new CancellationTokenSource();

        double scale = NativeMethods.GetScaleForPoint(new NativeMethods.POINT
        {
            X = (int)(windowRect.Left + windowRect.Width / 2),
            Y = (int)(windowRect.Top + windowRect.Height / 2),
        });
        if (scale <= 0) scale = 1;

        double regionW = Math.Max(120, windowRect.Width / scale);
        double regionH = Math.Max(80, windowRect.Height / scale);

        Left = windowRect.Left / scale;
        Top = windowRect.Top / scale;
        Width = regionW;
        Height = regionH;

        Canvas.Children.Clear();
        _placed.Clear();
        _translations = new string?[lines.Count].ToList();
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // --- Grouping lines into paragraphs: vertically adjacent lines with close
        //     alignment form one block (one text box per paragraph) ---
        var order = Enumerable.Range(0, lines.Count)
            .OrderBy(i => (lines[i].Rect.Top, lines[i].Rect.Left))
            .ToList();
        var blocks = new List<List<int>>();
        foreach (var idx in order)
        {
            if (blocks.Count == 0) { blocks.Add(new List<int> { idx }); continue; }
            var block = blocks[^1];
            var bounds = BlockBounds(lines, block);
            var cur = lines[idx].Rect;
            var prev = lines[block[^1]].Rect;
            double lineH = Math.Min(cur.Height, prev.Height);
            double gap = cur.Top - bounds.Bottom;
            bool verticalOk = gap <= 0.65 * Math.Max(12, lineH) && cur.Top >= bounds.Top - 2;
            bool horizontalOk = cur.Left >= bounds.Left - 24 && cur.Left <= bounds.Right + 24;
            if (verticalOk && horizontalOk) block.Add(idx);
            else blocks.Add(new List<int> { idx });
        }

        // --- One text box per paragraph ---
        foreach (var block in blocks)
        {
            var bounds = BlockBounds(lines, block);
            double x = (bounds.Left - windowRect.Left) / scale;
            double y = (bounds.Top - windowRect.Top) / scale;
            double w = Math.Max(40, bounds.Width / scale);
            double h = Math.Max(12, bounds.Height / scale);

            bool strip = block.Count == 1 && lines[block[0]].LabelStrip && h > 36;
            double boxTop, boxH, font, boxW, boxX;
            if (strip)
            {
                // Tile (icon + label): measure the label, the plate fits the text exactly
                font = 12.5;
                var ft = new FormattedText(lines[block[0]].Text, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    font, Brushes.Black, pixelsPerDip);
                double textW = ft.Width + 12;
                if (textW <= w)
                {
                    boxW = textW;
                    boxH = font * 1.4 + 4;
                    boxTop = w >= h * 1.5 ? y + h / 2 - boxH / 2 : y + h - boxH - 1;
                    boxX = x + (w - boxW) / 2;
                }
                else
                {
                    boxW = w;
                    int linesCount = Math.Max(1, (int)Math.Ceiling(textW / Math.Max(40, w)));
                    boxH = Math.Min(h, linesCount * font * 1.4 + 4);
                    boxTop = w >= h * 1.5 ? y + h / 2 - boxH / 2 : y + h - boxH - 1;
                    boxX = x;
                }
            }
            else
            {
                font = Math.Clamp(h * 0.75, 9, 20);
                boxH = h;
                boxW = Math.Max(40, Math.Min(w, regionW - x - 4));
                boxTop = y;
                boxX = x;
            }

            var edit = new TextBox
            {
                Text = "…",
                FontStyle = FontStyles.Italic,
                FontSize = font,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Width = Math.Max(30, boxW - 10),
                Cursor = Cursors.IBeam,
            };
            edit.SetResourceReference(TextBox.ForegroundProperty, "OverlayText");
            edit.SetResourceReference(TextBox.CaretBrushProperty, "OverlayText");
            edit.UseThemedCopyMenu();

            var box = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Width = boxW,
                Height = boxH,
                Child = edit,
            };
            box.SetResourceReference(Border.BackgroundProperty, "OverlayBg");

            Canvas.SetLeft(box, boxX - 5);
            Canvas.SetTop(box, boxTop - 1);
            Canvas.Children.Add(box);
            _placed.Add((box, edit, font, boxH, block));
        }

        CopyAllButton.Content = L10n.T("popup.btn.copy");
        Canvas.Children.Add(CopyAllButton);
        CopyAllButton.Visibility = Visibility.Visible;

        Show();
        this.ReaffirmTopmost();
        RestartAutoHide();
        _ = TranslateAllAsync(lines);
    }

    /// <summary>Union rectangle of a group of lines.</summary>
    private static Rect BlockBounds(IReadOnlyList<(string Text, Rect Rect, bool LabelStrip)> lines, List<int> block)
    {
        var r = lines[block[0]].Rect;
        foreach (var idx in block.Skip(1)) r.Union(lines[idx].Rect);
        return r;
    }

    public new void Hide()
    {
        _autoHide?.Stop();
        _cts?.Cancel();
        base.Hide();
    }

    private void RestartAutoHide()
    {
        _autoHide?.Stop();
        _autoHide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        _autoHide.Tick += (_, _) =>
        {
            _autoHide?.Stop();
            Hide();
        };
        _autoHide.Start();
    }

    private async Task TranslateAllAsync(IReadOnlyList<(string Text, Rect Rect, bool LabelStrip)> lines)
    {
        var ct = _cts?.Token ?? default;
        var cache = new Dictionary<string, string>();
        using var gate = new SemaphoreSlim(5); // do not hammer the translator with all lines at once

        var tasks = lines.Select(async ((string Text, Rect Rect, bool LabelStrip) line, int idx) =>
        {
            string? translated = null;
            try
            {
                await gate.WaitAsync(ct);
                try
                {
                    lock (cache)
                    {
                        if (cache.TryGetValue(line.Text, out var cached))
                            translated = cached;
                    }
                    if (translated == null)
                    {
                        var result = await _svc.Translator.TranslateAsync(
                            line.Text, _svc.Settings.SourceLanguage, _svc.Settings.TargetLanguage, ct);
                        translated = result?.Text ?? string.Empty;
                        lock (cache) cache[line.Text] = translated;
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            catch
            {
                // the line stays untranslated
            }

            await Dispatcher.BeginInvoke(new Action(() => ApplyTranslation(idx, translated)));
        }).ToArray();

        await Task.WhenAll(tasks);
        await Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible || _placed.Count == 0) return;
            if (_translations.All(t => string.IsNullOrWhiteSpace(t)))
            {
                Hide();
                _svc.Tray.ShowBalloon(L10n.T("popup.title"), L10n.T("balloon.translate.fail"));
            }
        }));
    }

    /// <summary>
    /// A line translation is ready: assemble the whole paragraph text from the
    /// ready translations of its lines and update the box (fitting the font to
    /// the paragraph bounds).
    /// </summary>
    private void ApplyTranslation(int idx, string? translated)
    {
        if (idx >= _translations.Count || !IsVisible) return;
        _translations[idx] = string.IsNullOrWhiteSpace(translated) ? null : translated;

        int placedIdx = _placed.FindIndex(p => p.LineIndices.Contains(idx));
        if (placedIdx < 0) return;
        var (box, edit, baseFont, boxHeight, lineIndices) = _placed[placedIdx];

        var parts = lineIndices
            .Select(i => _translations[i])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>()
            .ToList();
        if (parts.Count == 0) return; // wait for the first translated line of the paragraph

        edit.Text = string.Join(" ", parts);
        edit.FontStyle = FontStyles.Normal;

        // Fit the paragraph bounds: if the translation does not fit at the base
        // size - shrink the font so the whole text is visible
        try
        {
            edit.FontSize = baseFont;
            var ft = new FormattedText(edit.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                baseFont, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double linesCount = Math.Max(1, Math.Ceiling(ft.Width / Math.Max(20, edit.Width - 2)));
            double needed = linesCount * baseFont * 1.35;
            if (needed > boxHeight)
                edit.FontSize = Math.Max(8, baseFont * boxHeight / needed);
        }
        catch { }
    }

    /// <summary>Copy all translated paragraphs at once (each on its own line).</summary>
    private void CopyAllClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = string.Join("\n", _placed
                .Select(p => BuildBlockText(p.LineIndices))
                .Where(t => !string.IsNullOrWhiteSpace(t)));
            if (text.Length == 0) return;
            Clipboard.SetText(text);
            CopyAllButton.Content = L10n.T("popup.copied");
            RestartAutoHide();
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                CopyAllButton.Content = L10n.T("popup.btn.copy");
            };
            reset.Start();
        }
        catch { }
    }

    private string BuildBlockText(List<int> lineIndices) =>
        string.Join(" ", lineIndices
            .Select(i => i < _translations.Count ? _translations[i] : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Cast<string>());
}
