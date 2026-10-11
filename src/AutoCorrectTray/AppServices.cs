using System.Windows;
using System.Windows.Threading;
using AutoCorrectTray.Exclusions;
using AutoCorrectTray.Hooks;
using AutoCorrectTray.Hotkeys;
using AutoCorrectTray.Input;
using AutoCorrectTray.Logging;
using AutoCorrectTray.Native;
using AutoCorrectTray.Settings;
using AutoCorrectTray.Spelling;
using AutoCorrectTray.TextCapture;
using AutoCorrectTray.Tray;
using AutoCorrectTray.Ui;
using AutoCorrectTray.Translation;

namespace AutoCorrectTray;

/// <summary>Composition root: wires hooks, dictionary, UI and settings.</summary>
public sealed class AppServices : IDisposable
{
    public const int HotkeyFixId = 1;
    public const int HotkeyTranslateId = 2;
    public const int HotkeyPauseId = 3;
    public const int HotkeyCaptureId = 4;

    public Dispatcher Dispatcher { get; }
    public Logger Log { get; }
    public SettingsStore SettingsStore { get; }
    public AppSettings Settings => SettingsStore.Current;

    public TypedTextBuffer Buffer { get; } = new();
    public InputInjector Injector { get; } = new();
    public SpellService Spell { get; }
    public UiTextService UiText { get; } = new();
    public AppExclusions Exclusions { get; }
    public ITranslationProvider Translator { get; }

    public PopupManager Popups { get; private set; } = null!;
    public ForegroundMonitor Foreground { get; private set; } = null!;
    public KeyboardHookService Keyboard { get; private set; } = null!;
    public MouseHookService Mouse { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public TrayService Tray { get; private set; } = null!;

    public bool Paused { get; private set; }
    public event Action? ShowSettingsRequested;

    /// <summary>Live typing: current buffer sentence after each change. Called from the hook thread.</summary>
    public event Action<string>? TypedLiveChanged;

    public void RequestShowSettings() => ShowSettingsRequested?.Invoke();

    /// <summary>Live typing: all text typed since the last buffer reset.</summary>
    public void RaiseTypedLive() => TypedLiveChanged?.Invoke(Buffer.Text);

    /// <summary>
    /// Click outside our windows (hook thread): the caret in the target app may
    /// have moved - the buffer no longer matches the position, so reset it.
    /// </summary>
    public void NotifyExternalClick(NativeMethods.POINT point)
    {
        if (IsPointOverOurWindow(point)) return;
        if (Buffer.Length == 0) return;
        Buffer.Reset();
        RaiseTypedLive();
    }

    private bool IsPointOverOurWindow(NativeMethods.POINT point)
    {
        var hit = NativeMethods.WindowFromPoint(point);
        if (hit == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hit, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    // ---------------- Selection tracking while the translate window is open ----------------

    private int _followSeq;

    /// <summary>
    /// Selection may have changed (LMB released, Shift+arrows, Ctrl+A).
    /// If the translate window is open, re-read the selection and translate it;
    /// the window stays in place. Runs on the UI thread via BeginInvoke from hooks.
    /// </summary>
    public void HandleSelectionMaybeChanged(NativeMethods.POINT? point)
    {
        if (Paused || !Settings.TranslateEnabled || !Settings.AutoTranslateSelection) return;
        if (!Popups.IsTranslateVisible) return;
        if (point is { } p && IsPointOverOurWindow(p)) return; // click on the translate window itself

        int seq = ++_followSeq;
        _ = FollowSelectionAsync(seq);
    }

    private async Task FollowSelectionAsync(int seq)
    {
        try
        {
            // Let the app settle the selection after mouse release;
            // this also debounces Shift+arrow sequences
            await Task.Delay(240);
            if (seq != _followSeq || !Popups.IsTranslateVisible) return;

            string? selection = null;
            for (int i = 0; i < 3 && string.IsNullOrWhiteSpace(selection); i++)
            {
                selection = await Task.Run(() => UiText.GetSelectedText());
                if (string.IsNullOrWhiteSpace(selection) && i < 2) await Task.Delay(130);
            }
            if (seq != _followSeq || !Popups.IsTranslateVisible) return;
            if (!string.IsNullOrWhiteSpace(selection))
                Popups.UpdateTranslationForSelection(selection.Trim());
        }
        catch (Exception ex)
        {
            Log.Error("Selection tracking", ex);
        }
    }

    private int _hintSeq;

    public AppServices(Dispatcher dispatcher, Logger log, SettingsStore store)
    {
        Dispatcher = dispatcher;
        Log = log;
        SettingsStore = store;
        Spell = new SpellService(log, () => Settings.UserWords);
        Exclusions = new AppExclusions(() => Settings.BlacklistedApps, log);
        Translator = new ChunkedTranslationProvider(
            new FallbackTranslationService(
                new GoogleTranslateProvider(GoogleTranslateProvider.DefaultEndpoint),
                new GoogleTranslateProvider(GoogleTranslateProvider.AlternateEndpoint),
                new MyMemoryProvider()));
    }

    public void Initialize()
    {
        Popups = new PopupManager(this);
        Foreground = new ForegroundMonitor(Log);
        Foreground.Raised += OnForegroundChanged;
        Keyboard = new KeyboardHookService(this);
        Mouse = new MouseHookService(this);
        Hotkeys = new HotkeyManager(this);
        Hotkeys.RegisterAll();
        Hotkeys.Fired += HandleHotkey;
        Spell.WarmUp();
        Tray = new TrayService(this);
        Exclusions.Refresh();
    }

    public void ApplySettings()
    {
        Hotkeys.RegisterAll();
        Log.Info("Settings applied");
    }

    /// <summary>While a combo is being recorded in settings: release all app hotkeys.</summary>
    public void SuspendHotkeys() => Hotkeys.SuspendAll();

    /// <summary>Recording finished - re-register hotkeys from settings.</summary>
    public void ResumeHotkeys() => Hotkeys.RegisterAll();

    private void OnForegroundChanged(IntPtr hwnd)
    {
        // Switching the foreground window to our own popups/dropdowns is not an event
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return;

        Exclusions.Invalidate(hwnd);
        Popups.HideAll();
        // The open translate window follows the monitor the user is working on
        Popups.FollowForeground(hwnd);
        Buffer.Reset();
        RaiseTypedLive();
    }

    public bool IsFeatureAllowedHere() => !Paused && Exclusions.IsCurrentAppAllowed;

    // ---------------- Spelling: suggestions ----------------

    /// <summary>Word completed (followed by space/punctuation) - show a hint if misspelled.</summary>
    public void OnWordCompleted(string word, string? separator)
    {
        if (!IsFeatureAllowedHere() || !Settings.SpellEnabled) return;
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2) return;
        int seq = Interlocked.Increment(ref _hintSeq);
        _ = ShowHintForWordAsync(word, separator, seq, debounceMs: 250);
    }

    /// <summary>Ctrl+Alt+F: check the word before the caret right now.</summary>
    public void RequestFixForCurrentWord()
    {
        if (Paused || !Settings.SpellEnabled) { Popups.HideAll(); return; }
        Log.Info("Fix: checking word before caret via hotkey");
        int seq = Interlocked.Increment(ref _hintSeq);
        _ = ShowHintForWordAsync(null, null, seq, debounceMs: 0);
    }

    private async Task ShowHintForWordAsync(string? forcedWord, string? separator, int seq, int debounceMs)
    {
        try
        {
            if (debounceMs > 0)
            {
                await Task.Delay(debounceMs);
                if (seq != Volatile.Read(ref _hintSeq)) return;
            }

            string? word = forcedWord ?? Buffer.CurrentWord;
            Rect? anchor = null;

            var info = await Task.Run(() => UiText.GetCaretWord());
            if (seq != Volatile.Read(ref _hintSeq)) return;
            if (info != null)
            {
                if (info.IsPassword) return;
                anchor = info.Rect;
                if (string.IsNullOrEmpty(word)) word = info.Word;
            }
            if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
            {
                Popups.HideAll();
                return;
            }

            bool misspelled = await Task.Run(() => Spell.IsMisspelled(word));
            if (seq != Volatile.Read(ref _hintSeq)) return;
            if (!misspelled)
            {
                if (forcedWord != null) Popups.HideAll();
                return;
            }

            var suggestions = await Task.Run(() => Spell.Suggest(word, 6));
            if (seq != Volatile.Read(ref _hintSeq)) return;

            anchor ??= RectFromPoint(MouseHookService.LastMousePosition);
            var fixedWord = word;
            var hintSeparator = separator;
            _ = Dispatcher.BeginInvoke(() => Popups.ShowSuggestions(
                fixedWord, suggestions, anchor,
                replacement => ReplaceWord(fixedWord, replacement, hintSeparator),
                () => AddToUserDictionary(fixedWord)));
        }
        catch (Exception ex)
        {
            Log.Error("Spelling hint", ex);
        }
    }

    public void ReplaceWord(string originalWord, string replacement, string? separator = null)
    {
        Popups.HideAll();
        if (string.IsNullOrEmpty(replacement) || replacement == originalWord) return;

        var plan = ReplacementPlanner.Plan(originalWord, replacement, separator, Buffer.Text);
        if (plan is not { } p) return; // buffer mismatch - hint is stale

        try
        {
            // Word at the caret without separator (hotkey/RMB): select it in the
            // app via UIA and type the replacement over the selection - more
            // accurate than counting backspaces.
            if (separator == null &&
                TrySelectWordForReplacement(originalWord))
            {
                Injector.TypeText(replacement);
                Buffer.Reset();
                return;
            }

            Injector.SendBackspaces(p.Backspaces);
            Injector.TypeText(p.Insert);
            Buffer.ApplyPlan(p.Backspaces, p.Insert);
        }
        catch (Exception ex)
        {
            Log.Error("Word replacement", ex);
        }
    }

    private bool TrySelectWordForReplacement(string word)
    {
        try
        {
            return Task.Run(() => UiText.TrySelectCaretWord(word)).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    private void AddToUserDictionary(string word)
    {
        Popups.HideAll();
        if (!Settings.UserWords.Contains(word, StringComparer.OrdinalIgnoreCase))
            Settings.UserWords.Add(word);
        SettingsStore.Save();
        Log.Info($"Word added to user dictionary: {word}");
    }

    // ---------------- Double-click on a word ----------------

    /// <summary>
    /// A double LMB click selects a word in the app; if it is misspelled,
    /// suggestions are shown. The replacement is typed over the selection.
    /// </summary>
    public async void HandleDoubleClick(NativeMethods.POINT point)
    {
        try
        {
            if (!IsFeatureAllowedHere() || !Settings.SpellEnabled || !Settings.DoubleClickFix)
            {
                Log.Info($"Double click skipped: paused={Paused}, SpellEnabled={Settings.SpellEnabled}, DoubleClickFix={Settings.DoubleClickFix}");
                return;
            }

            // Apps do not update the selection instantly after a double click:
            // poll a few times until the selection appears
            (string Word, Rect? Rect)? info = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(130);
                if (!IsFeatureAllowedHere()) return;
                info = await Task.Run(() => UiText.GetSelectedWord());
                if (info != null) break;
            }
            if (info == null)
            {
                Log.Info("Double click: app did not provide a selection (TextPattern unavailable or selection empty)");
                return;
            }
            var (raw, rect) = info.Value;

            var word = SpellService.CleanWord(raw.Trim());
            if (word.Length < 2 || !word.All(char.IsLetter))
            {
                Log.Info($"Double click: no word in selection (\"{raw}\")");
                return;
            }
            if (!await Task.Run(() => Spell.IsMisspelled(word)))
            {
                Log.Info($"Double click: \"{word}\" is spelled correctly");
                return;
            }

            var suggestions = await Task.Run(() => Spell.Suggest(word, 6));
            Log.Info($"Double click: \"{word}\", {suggestions.Length} suggestions");
            var anchor = rect ?? RectFromPoint(point);
            _ = Dispatcher.BeginInvoke(() => Popups.ShowSuggestions(
                word, suggestions, anchor,
                replacement => ReplaceSelection(replacement),
                () => AddToUserDictionary(word)));
        }
        catch (Exception ex)
        {
            Log.Error("Double click on word", ex);
        }
    }

    /// <summary>Replaces the double-clicked selection by typing over it.</summary>
    public void ReplaceSelection(string replacement)
    {
        Popups.HideAll();
        if (string.IsNullOrEmpty(replacement)) return;
        try
        {
            Injector.TypeText(replacement);
            Buffer.Reset();
            RaiseTypedLive();
        }
        catch (Exception ex)
        {
            Log.Error("Selection replacement", ex);
        }
    }

    // ---------------- Right mouse button ----------------

    public async void HandleRightClickIntercepted(NativeMethods.POINT clickPoint)
    {
        try
        {
            await Task.Delay(60); // let the app process the click and move the caret
            var info = await Task.Run(() => UiText.GetCaretWord());

            if (info == null || !info.IsEditable || info.IsPassword ||
                string.IsNullOrWhiteSpace(info.Word) || info.Word.Length < 2 ||
                !IsClickNearCaret(info.Rect, clickPoint))
            {
                ReinjectRightClick();
                return;
            }

            string word = info.Word.Trim();
            bool misspelled = await Task.Run(() => Spell.IsMisspelled(word));
            if (!misspelled)
            {
                ReinjectRightClick(); // word is correct - show the app's usual menu
                return;
            }

            var suggestions = await Task.Run(() => Spell.Suggest(word, 6));
            var anchor = info.Rect ?? RectFromPoint(clickPoint);
            _ = Dispatcher.BeginInvoke(() => Popups.ShowSuggestions(
                word, suggestions, anchor,
                replacement => ReplaceWord(word, replacement),
                () => AddToUserDictionary(word)));
        }
        catch (Exception ex)
        {
            Log.Error("Right-click handling", ex);
            ReinjectRightClick();
        }

        void ReinjectRightClick()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { Injector.SendRightClick(); }
                catch (Exception ex) { Log.Error("Right-click reinject", ex); }
            }));
        }
    }

    /// <summary>Our menu only makes sense for the word at the caret (RMB click near it).</summary>
    private static bool IsClickNearCaret(Rect? caretRect, NativeMethods.POINT clickPoint)
    {
        if (!caretRect.HasValue) return true;
        var r = caretRect.Value;
        double centerY = (r.Top + r.Bottom) / 2;
        return Math.Abs(centerY - clickPoint.Y) < 60 &&
               clickPoint.X > r.Left - 60 &&
               clickPoint.X < r.Right + 250;
    }

    // ---------------- Translation ----------------

    public async Task TranslateSelectedAsync()
    {
        try
        {
            if (Paused || !Settings.TranslateEnabled) return;

            string source;
            TranslationMode mode;

            var selection = await Task.Run(() => UiText.GetSelectedText());
            // UIA selection read is race-prone right after hotkey: retry briefly
            for (int i = 0; i < 3 && string.IsNullOrWhiteSpace(selection); i++)
            {
                await Task.Delay(120);
                selection = await Task.Run(() => UiText.GetSelectedText());
            }
            Log.Info($"Translate: selection=[{TruncateForLog(selection)}]");
            if (!string.IsNullOrWhiteSpace(selection))
            {
                source = selection.Trim();
                mode = TranslationMode.Selection;
            }
            else
            {
                // Ctrl+C: if the buffer is unchanged there was no selection, and
                // stale clipboard content must not be taken for user text
                // Detect the copy by clipboard SEQUENCE NUMBER: it changes on every
                // new copy even when the text equals the old clipboard content
                // (text-only comparison misfired when the same text was copied before)
                var beforeSeq = NativeMethods.GetClipboardSequenceNumber();
                Injector.SendCopy();
                var after = TryGetClipboardText();
                for (int i = 0; i < 10 && NativeMethods.GetClipboardSequenceNumber() == beforeSeq; i++)
                {
                    await Task.Delay(80);
                    after = TryGetClipboardText();
                }
                bool copied = NativeMethods.GetClipboardSequenceNumber() != beforeSeq;
                Log.Info($"Translate: copied={copied} clipAfter=[{TruncateForLog(after)}]");
                if (copied && !string.IsNullOrWhiteSpace(after))
                {
                    source = after;
                    mode = TranslationMode.ClipboardSelection;
                    ScheduleClipboardRestore(TryGetClipboardObject());
                }
                else
                {
                    source = string.Empty;
                    mode = TranslationMode.Typed; // live mode: follow typing
                }
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                // No selection: open live translation - the window follows typing.
                // Use all typed text, not just the last sentence, so the whole
                // paragraph is translated.
                source = Buffer.Text;
                mode = TranslationMode.Typed;
            }

            Popups.ShowTranslation(source, mode);
        }
        catch (Exception ex)
        {
            Log.Error("Translation", ex);
        }
    }

    /// <summary>
    /// Replaces the typed text with its translation (backspaces + typing).
    /// Strict equality check: if the user has already typed something else,
    /// the replacement is cancelled - otherwise the new input would be erased.
    /// </summary>
    public bool ReplaceTypedText(string original, string replacement)
    {
        try
        {
            if (!Buffer.Text.Equals(original, StringComparison.Ordinal)) return false;
            Injector.SendBackspaces(original.Length);
            Buffer.TrimEnd(original.Length);
            Injector.TypeText(replacement);
            Buffer.Append(replacement);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Typed text replacement", ex);
            return false;
        }
    }

    // ---------------- Text capture under the cursor ----------------

    /// <summary>
    /// Capture hotkey - "reverse scissors" mode: the screen dims, the user
    /// outlines an area with the mouse, all text lines inside are read via UI
    /// Automation (no screenshots) and translated by an overlay on top of each
    /// line. Pressing the hotkey again hides the overlay.
    /// </summary>
    public async Task CaptureUnderCursorAsync()
    {
        try
        {
            if (Paused || !Settings.TranslateEnabled) return;

            // Pressing again hides the result
            if (Popups.IsWindowOverlayVisible)
            {
                Popups.HideOverlay();
                return;
            }
            if (Popups.IsRegionSelectorOpen) return;

            Log.Info("Capture: region selection mode");
            Popups.StartRegionSelection(rect => _ = TranslateRegionAsync(rect));
        }
        catch (Exception ex)
        {
            Log.Error("Text capture (region)", ex);
        }
    }

    private async Task TranslateRegionAsync(Rect region)
    {
        try
        {
            var seed = new NativeMethods.POINT
            {
                X = (int)(region.Left + region.Width / 2),
                Y = (int)(region.Top + region.Height / 2),
            };
            var lines = await Task.Run(() => UiText.GetLinesInRegion(region, seed));

            if (lines == null || lines.Count == 0)
            {
                var why = await Task.Run(() => UiText.DescribePoint(seed));
                Log.Info($"Capture failed: {why}");
                Tray.ShowBalloon(L10n.T("popup.title"), L10n.T("balloon.capture.fail", why));
                return;
            }

            Log.Info($"Capture: {lines.Count} lines in region {region.Width:F0}x{region.Height:F0}");
            Popups.ShowWindowOverlay(lines, region);
        }
        catch (Exception ex)
        {
            Log.Error("Region translation", ex);
        }
    }

    /// <summary>
    /// Replaces the typed text with its translation (backspaces + typing).
    /// Strict equality check: if the user has already typed something else,
    /// the replacement is cancelled - otherwise we would erase the new input.
    /// </summary>
    public bool ReplaceTypedSentence(string original, string replacement)
    {
        try
        {
            if (!Buffer.Text.Equals(original, StringComparison.Ordinal)) return false;
            Injector.SendBackspaces(original.Length);
            Injector.TypeText(replacement);
            Buffer.TrimEnd(original.Length);
            Buffer.Append(replacement);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Replace typed sentence", ex);
            return false;
        }
    }

    // ---------------- Live translation auto-replace ----------------

    private int _liveSeq;

    /// <summary>
    /// Auto-replace mode: a sentence was finished with a terminator - translate
    /// the whole typed text and print it instead of the original (no mouse, no
    /// buttons). Called from the keyboard hook thread; heavy work goes to a
    /// background task and then to the dispatcher.
    /// </summary>
    public void OnTypedSentenceCompleted(string typedText)
    {
        try
        {
            if (!Settings.AutoReplaceLive || !Settings.TranslateEnabled) return;
            if (Popups.IsTranslateLiveVisible && Popups.IsTranslationBusy) return;

            var original = typedText;
            if (original.Length < 2 || original.Trim().Length == 0) return;

            int seq = Interlocked.Increment(ref _liveSeq);
            string sourceLang = Settings.SourceLanguage;
            string targetLang = Settings.TargetLanguage;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var result = await Translator.TranslateAsync(original, sourceLang, targetLang, cts.Token);
                    if (seq != Volatile.Read(ref _liveSeq)) return;
                    var translated = result?.Text;
                    if (string.IsNullOrWhiteSpace(translated)) return;

                    await Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (seq != Volatile.Read(ref _liveSeq)) return;
                            // The user typed more meanwhile - cancel, or we would erase it
                            if (!Buffer.Text.Equals(original, StringComparison.Ordinal)) return;
                            Injector.SendBackspaces(original.Length);
                            Injector.TypeText(translated);
                            Buffer.TrimEnd(original.Length);
                            Buffer.Append(translated);
                            Log.Info("Auto-replace of live translation done");
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Auto-replace of live translation", ex);
                        }
                    }));
                }
                catch (Exception ex)
                {
                    Log.Error("Auto-translate sentence", ex);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("Auto-replace of live translation", ex);
        }
    }

    internal static string TruncateForLog(string? s)
    {
        if (s == null) return "null";
        var t = s.Replace("\r", " ").Replace("\n", " ");
        return t.Length <= 60 ? t : t.Substring(0, 60) + "...";
    }

    private static System.Windows.IDataObject? TryGetClipboardObject()
    {
        try { return Clipboard.GetDataObject(); }
        catch { return null; }
    }

    private static string TryGetClipboardText()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var text = Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
                if (string.IsNullOrEmpty(text))
                    text = Clipboard.GetText(System.Windows.TextDataFormat.Text);
                return text ?? string.Empty;
            }
            catch
            {
                Thread.Sleep(120);
            }
        }
        return string.Empty;
    }

    private void ScheduleClipboardRestore(System.Windows.IDataObject? saved)
    {
        if (saved == null) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(8));
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Clipboard.SetDataObject(saved, true);
                        return;
                    }
                    catch
                    {
                        Thread.Sleep(120);
                    }
                }
            }));
        });
    }

    // ---------------- Hotkeys / pause ----------------

    private void HandleHotkey(int id)
    {
        // While paused, actions are silent - turn the silence into a clear message
        if (Paused && id != HotkeyPauseId)
        {
            Tray.ShowBalloon(L10n.T("tray.pause"), L10n.T("balloon.paused.action", Settings.PauseHotkey));
            return;
        }

        switch (id)
        {
            case HotkeyFixId:
                RequestFixForCurrentWord();
                break;
            case HotkeyTranslateId:
                // Pressing again while the window is open closes it:
                // the same key toggles the translation window
                if (Popups.IsTranslateVisible)
                    Popups.HideTranslation();
                else
                    _ = TranslateSelectedAsync();
                break;
            case HotkeyCaptureId:
                _ = CaptureUnderCursorAsync();
                break;
            case HotkeyPauseId:
                TogglePause();
                break;
        }
    }

    public void TogglePause()
    {
        Paused = !Paused;
        if (Paused) Popups.HideAll(forceTranslateHide: true); // pause also hides the sticky translate window
        Tray.UpdateState();
        Tray.ShowBalloon(Paused ? L10n.T("tray.pause") : L10n.T("tray.tooltip"),
            L10n.T(Paused ? "balloon.pause.on" : "balloon.pause.off", Settings.PauseHotkey));
        Log.Info(Paused ? "Application paused" : "Application resumed");
    }

    private static Rect RectFromPoint(NativeMethods.POINT p) => new(p.X, p.Y, 1, 1);

    public void Dispose()
    {
        Hotkeys?.Dispose();
        Keyboard?.Dispose();
        Mouse?.Dispose();
        Foreground?.Dispose();
        Tray?.Dispose();
    }
}
