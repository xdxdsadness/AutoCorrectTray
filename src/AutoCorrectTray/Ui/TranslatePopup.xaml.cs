using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoCorrectTray.Native;
using AutoCorrectTray.Translation;

namespace AutoCorrectTray.Ui;

/// <summary>Translation popup. Does not steal focus - "Replace text" types the result into the original field.</summary>
public partial class TranslatePopup : Window
{
    private readonly AppServices _svc;
    private string _source = string.Empty;
    private TranslationMode _mode;
    private CancellationTokenSource? _cts;
    private DispatcherTimer? _copyResetTimer;
    private DispatcherTimer? _liveDebounce;
    private bool _live;

    /// <summary>Live mode: the window follows the text the user types in the source field.</summary>
    public bool IsLive => _live;

    private int _pendingLive;

    /// <summary>A live translate request is in flight (auto-replace does not duplicate it).</summary>
    public bool IsLiveBusy => Volatile.Read(ref _pendingLive) != 0;

    public TranslatePopup(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;

        CloseButton.Click += (_, _) => Hide();
        CloseAllButton.Click += (_, _) => Hide();
        SwapButton.Click += (_, _) => SwapLanguages();
        CmbSource.SelectionChanged += (_, _) => { SaveLanguageChoice(); _ = TranslateAsync(); };
        CmbTarget.SelectionChanged += (_, _) => { SaveLanguageChoice(); _ = TranslateAsync(); };
        CopyButton.Click += (_, _) => CopyResult();
        ReplaceButton.Click += (_, _) => ReplaceInField();
        // Auto-replace toggle right in the window: applies instantly, stored in settings
        ChkAutoReplace.Click += (_, _) =>
        {
            _svc.Settings.AutoReplaceLive = ChkAutoReplace.IsChecked == true;
            _svc.SettingsStore.Save();
            _svc.Log.Info($"Auto-translate (replace on period): {(ChkAutoReplace.IsChecked == true ? "on" : "off")}");
        };
        Closed += (_, _) => StopLive();
        WindowDragHelper.Attach(HeaderBar, this, SavePosition);

        _liveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _liveDebounce.Tick += (_, _) =>
        {
            _liveDebounce?.Stop();
            _ = TranslateAsync();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.SetNoActivate();
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return (IntPtr)NativeMethods.MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    public void ShowFor(string source, TranslationMode mode)
    {
        StopLive(); // window reopened - unsubscribe from the previous session

        ChkAutoReplace.IsChecked = _svc.Settings.AutoReplaceLive;
        _source = source;
        _mode = mode;
        SourceText.Text = source;
        DetectedText.Text = string.Empty;
        ShowResultMuted("…");
        CopyButton.Content = L10n.T("popup.btn.copy");

        _live = mode == TranslationMode.Typed;
        ModeText.Text = L10n.T("popup.mode.live");
        ModeText.Visibility = _live ? Visibility.Visible : Visibility.Collapsed;
        if (_live)
        {
            _svc.TypedLiveChanged += OnTypedLiveChanged;
            if (_source.Length == 0)
                ShowLivePlaceholder();
        }
        else
        {
            ShowSnapshotState(source);
        }

        CmbSource.ItemsSource = Languages.All;
        CmbTarget.ItemsSource = Languages.Concrete;
        SelectLanguage(CmbSource, _svc.Settings.SourceLanguage);
        SelectLanguage(CmbTarget, _svc.Settings.TargetLanguage);

        if (_svc.Settings.TranslateWindowLeft is { } savedLeft &&
            _svc.Settings.TranslateWindowTop is { } savedTop)
        {
            // The user has moved the window before - open it there; but if the active
            // window is on another monitor, the position moves to its monitor
            Left = savedLeft;
            Top = savedTop;
            MonitorFollow.SnapBeforeShow(this, NativeMethods.GetForegroundWindow());
            Show();
            this.ReaffirmTopmost();
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ClampIntoWorkArea));
        }
        else
        {
            PopupPositioner.ShowPositioned(this, null, 14);
        }

        if (_source.Length > 0)
            _ = TranslateAsync();
    }

    private void SavePosition()
    {
        try
        {
            _svc.Settings.TranslateWindowLeft = Left;
            _svc.Settings.TranslateWindowTop = Top;
            _svc.SettingsStore.Save();
        }
        catch { }
    }

    /// <summary>Keeps the saved position from getting lost outside all monitors.</summary>
    private void ClampIntoWorkArea()
    {
        try
        {
            double width = ActualWidth, height = ActualHeight;
            if (width <= 0 || height <= 0) return;
            var left = SystemParameters.VirtualScreenLeft;
            var top = SystemParameters.VirtualScreenTop;
            var right = left + SystemParameters.VirtualScreenWidth;
            var bottom = top + SystemParameters.VirtualScreenHeight;

            Left = Math.Min(Math.Max(Left, left + 8), Math.Max(left + 8, right - width - 8));
            Top = Math.Min(Math.Max(Top, top + 8), Math.Max(top + 8, bottom - height - 8));
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        StopLive();
        base.OnClosed(e);
    }

    private void StopLive()
    {
        if (!_live) return;
        _live = false;
        _liveDebounce?.Stop();
        _svc.TypedLiveChanged -= OnTypedLiveChanged;
    }

    /// <summary>Arrives from the keyboard hook thread - marshaled to the UI.</summary>
    private void OnTypedLiveChanged(string text) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_live || !IsVisible) return;
            ApplyLiveText(text);
        }));

    private void ApplyLiveText(string text)
    {
        // No Trim(): ReplaceTypedSentence compares the buffer char-by-char;
        // any mismatch would cancel the replacement
        if (text == _source) return;
        _source = text;
        SourceText.Text = text;

        if (text.Length == 0)
        {
            _liveDebounce?.Stop();
            ShowLivePlaceholder();
            return;
        }
        ShowResultMuted("…");
        _liveDebounce?.Stop();
        _liveDebounce?.Start();
    }

    private void ShowLivePlaceholder()
    {
        ShowResultMuted(L10n.T("popup.live.placeholder"));
        DetectedText.Text = string.Empty;
    }

    private void ShowSnapshotState(string source)
    {
        SourceText.Text = source;
        DetectedText.Text = string.Empty;
        ShowResultMuted("…");
    }

    /// <summary>Result block states; brushes come from theme resources (change with the theme).</summary>
    private void ShowResultMuted(string text)
    {
        ResultText.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        ResultText.FontStyle = FontStyles.Italic;
        ResultText.Text = text;
    }

    private void ShowResultFinal(string text)
    {
        ResultText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        ResultText.FontStyle = FontStyles.Normal;
        ResultText.Text = text;
    }

    private void ShowResultError(string text)
    {
        ResultText.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        ResultText.FontStyle = FontStyles.Normal;
        ResultText.Text = text;
    }

    private new void Hide()
    {
        StopLive();
        base.Hide();
    }

    /// <summary>Close the window from outside (hotkey pressed again, pause).</summary>
    public void HideWindow() => Hide();

    /// <summary>
    /// The user switched to a window on another monitor - the popup moves there
    /// by itself without stealing focus. Runs on the UI thread on window change.
    /// </summary>
    public void FollowToForegroundWindow(IntPtr foregroundHwnd) =>
        MonitorFollow.MoveToMonitorOfWindow(this, foregroundHwnd);

    /// <summary>
    /// New selection while the window is open: translate the new text right here,
    /// without reopening the window or changing its position. Live mode turns off.
    /// </summary>
    public void UpdateForSelection(string text)
    {
        if (!IsVisible)
        {
            ShowFor(text, TranslationMode.Selection);
            return;
        }
        if (_mode == TranslationMode.Selection && text == _source) return; // same selection

        StopLive();
        _source = text;
        _mode = TranslationMode.Selection;
        ModeText.Visibility = Visibility.Collapsed;
        ShowSnapshotState(text);
        _ = TranslateAsync();
    }

    private async Task TranslateAsync()
    {
        if (_live && _source.Length == 0)
        {
            ShowLivePlaceholder();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var source = (CmbSource.SelectedItem as LanguageInfo)?.Code ?? "auto";
        var target = (CmbTarget.SelectedItem as LanguageInfo)?.Code ?? "ru";

        if (_live) Interlocked.Increment(ref _pendingLive);
        try
        {
            await TranslateCoreAsync(source, target, ct);
        }
        finally
        {
            if (_live) Interlocked.Decrement(ref _pendingLive);
        }
    }

    private async Task TranslateCoreAsync(string source, string target, CancellationToken ct)
    {
        ShowResultMuted(L10n.T("popup.translating"));

        try
        {
            var result = await _svc.Translator.TranslateAsync(_source, source, target, ct);
            if (ct.IsCancellationRequested) return;
            ShowResultFinal(string.IsNullOrEmpty(result?.Text) ? L10n.T("popup.failed") : result!.Text);
            DetectedText.Text = string.IsNullOrEmpty(result?.DetectedLanguage)
                ? string.Empty
                : L10n.T("popup.detected", result!.DetectedLanguage);
        }
        catch
        {
            if (ct.IsCancellationRequested) return;
            ShowResultError(L10n.T("popup.error.net"));
        }
    }

    private void SwapLanguages()
    {
        var source = CmbSource.SelectedItem as LanguageInfo;
        var target = CmbTarget.SelectedItem as LanguageInfo;
        if (source == null || target == null) return;

        var itemsSource = CmbSource.ItemsSource;
        CmbSource.ItemsSource = Languages.All;
        SelectLanguage(CmbSource, target.Code);
        CmbTarget.ItemsSource = Languages.Concrete;
        SelectLanguage(CmbTarget, source.Code == "auto" ? "ru" : source.Code);
        _ = itemsSource == null;
    }

    private static void SelectLanguage(ComboBox combo, string code)
    {
        var items = (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<LanguageInfo>().ToList();
        if (items == null) return;
        combo.SelectedItem = items.FirstOrDefault(l => l.Code == code) ?? items[0];
    }

    private void SaveLanguageChoice()
    {
        if (CmbSource.SelectedItem is LanguageInfo source) _svc.Settings.SourceLanguage = source.Code;
        if (CmbTarget.SelectedItem is LanguageInfo target) _svc.Settings.TargetLanguage = target.Code;
        _svc.SettingsStore.Save();
    }

    private void CopyResult()
    {
        try
        {
            Clipboard.SetText(ResultText.Text);
            CopyButton.Content = L10n.T("popup.copied");
            _copyResetTimer?.Stop();
            _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _copyResetTimer.Tick += (_, _) =>
            {
                _copyResetTimer?.Stop();
                CopyButton.Content = L10n.T("popup.btn.copy");
            };
            _copyResetTimer.Start();
        }
        catch { }
    }

    private void ReplaceInField()
    {
        var translation = ResultText.Text;
        Hide();
        if (string.IsNullOrEmpty(translation)) return;

        if (_mode == TranslationMode.Typed)
            _svc.ReplaceTypedSentence(_source, translation);
        else
        {
            // Replace the active selection by simply typing the text
            _svc.Injector.TypeText(translation);
            _svc.Buffer.Reset();
        }
    }
}
