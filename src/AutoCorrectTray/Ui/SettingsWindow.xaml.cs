using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoCorrectTray.Hotkeys;
using AutoCorrectTray.Settings;
using AutoCorrectTray.Translation;

namespace AutoCorrectTray.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppServices _svc;
    private TextBox? _recordingBox;
    private bool _loading = true;

    public SettingsWindow(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;
        Icon = Tray.TrayIconFactory.CreateAppIcon();
        Load();

        BtnSave.Click += (_, _) => Save();
        BtnClose.Click += (_, _) => Close();

        BtnRecordFix.Click += (_, _) => StartRecording(TxtFixHotkey);
        BtnRecordTranslate.Click += (_, _) => StartRecording(TxtTranslateHotkey);
        BtnRecordCapture.Click += (_, _) => StartRecording(TxtCaptureHotkey);
        BtnRecordPause.Click += (_, _) => StartRecording(TxtPauseHotkey);
        foreach (var box in new[] { TxtFixHotkey, TxtTranslateHotkey, TxtCaptureHotkey, TxtPauseHotkey })
            box.UseThemedCopyMenu();
        // Window-level interception: recording works even if focus left the field
        // (click-away, tab, dropdown) - until Esc or a combo is pressed
        this.PreviewKeyDown += RecorderOnPreviewKeyDown;

        BtnAddWord.Click += (_, _) => AddToList(LstUserWords, TxtNewWord);
        BtnRemoveWord.Click += (_, _) => RemoveSelected(LstUserWords);
        BtnAddApp.Click += (_, _) => AddToList(LstBlacklist, TxtNewApp);
        BtnRemoveApp.Click += (_, _) => RemoveSelected(LstBlacklist);

        // Window closed right during recording - hotkeys must come back
        Closed += (_, _) =>
        {
            _recordingBox = null;
            ResumeHotkeysAfterRecording();
        };

        _loading = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.UseDarkTitleBar(ThemeManager.IsDark);
    }

    /// <summary>Instant theme preview before "Save".</summary>
    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbTheme.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string theme)
        {
            ThemeManager.Apply(theme);
            this.UseDarkTitleBar(ThemeManager.IsDark);
        }
    }

    /// <summary>Instant UI language change (saved on "Save").</summary>
    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbLanguage.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string lang)
        {
            L10n.Apply(lang);
            this.UseDarkTitleBar(ThemeManager.IsDark);
        }
    }

    private void Load()
    {
        var s = _svc.Settings;
        ChkSpell.IsChecked = s.SpellEnabled;
        ChkRightClick.IsChecked = s.RightClickFix;
        ChkDoubleClick.IsChecked = s.DoubleClickFix;
        ChkAutoPeriod.IsChecked = s.AutoPeriod;
        ChkAutoCapital.IsChecked = s.AutoCapitalize;
        ChkSpaceAfter.IsChecked = s.SpaceAfterPunct;
        ChkTranslate.IsChecked = s.TranslateEnabled;
        ChkAutoReplaceLive.IsChecked = s.AutoReplaceLive;
        ChkAutoTranslateSelection.IsChecked = s.AutoTranslateSelection;
        ChkAutostart.IsChecked = s.Autostart;

        TxtFixHotkey.Text = s.FixHotkey;
        TxtTranslateHotkey.Text = s.TranslateHotkey;
        TxtCaptureHotkey.Text = s.CaptureHotkey;
        TxtPauseHotkey.Text = s.PauseHotkey;

        foreach (var item in CmbTheme.Items.Cast<System.Windows.Controls.ComboBoxItem>())
            if (string.Equals(item.Tag as string, s.Theme, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;
        foreach (var item in CmbLanguage.Items.Cast<System.Windows.Controls.ComboBoxItem>())
            if (string.Equals(item.Tag as string, s.Language, StringComparison.OrdinalIgnoreCase))
                item.IsSelected = true;

        CmbSourceLang.ItemsSource = Languages.All;
        CmbTargetLang.ItemsSource = Languages.Concrete;
        SelectLanguage(CmbSourceLang, s.SourceLanguage);
        SelectLanguage(CmbTargetLang, s.TargetLanguage);

        foreach (var word in s.UserWords) LstUserWords.Items.Add(word);
        foreach (var app in s.BlacklistedApps) LstBlacklist.Items.Add(app);
    }

    private static void SelectLanguage(ComboBox combo, string code)
    {
        var items = (combo.ItemsSource as IEnumerable)?.Cast<LanguageInfo>().ToList();
        if (items == null) return;
        combo.SelectedItem = items.FirstOrDefault(l => l.Code == code) ?? items[0];
    }

    private void Save()
    {
        var s = _svc.Settings;
        s.SpellEnabled = ChkSpell.IsChecked == true;
        s.RightClickFix = ChkRightClick.IsChecked == true;
        s.DoubleClickFix = ChkDoubleClick.IsChecked == true;
        s.AutoPeriod = ChkAutoPeriod.IsChecked == true;
        s.AutoCapitalize = ChkAutoCapital.IsChecked == true;
        s.SpaceAfterPunct = ChkSpaceAfter.IsChecked == true;
        s.TranslateEnabled = ChkTranslate.IsChecked == true;
        s.AutoReplaceLive = ChkAutoReplaceLive.IsChecked == true;
        s.AutoTranslateSelection = ChkAutoTranslateSelection.IsChecked == true;
        s.Autostart = ChkAutostart.IsChecked == true;

        SaveHotkeys(s);

        s.UserWords = LstUserWords.Items.Cast<string>().ToList();
        s.BlacklistedApps = LstBlacklist.Items.Cast<string>().ToList();

        if (CmbSourceLang.SelectedItem is LanguageInfo src) s.SourceLanguage = src.Code;
        if (CmbTargetLang.SelectedItem is LanguageInfo tgt) s.TargetLanguage = tgt.Code;
        if (CmbTheme.SelectedItem is System.Windows.Controls.ComboBoxItem themeItem && themeItem.Tag is string theme)
            s.Theme = theme;
        if (CmbLanguage.SelectedItem is System.Windows.Controls.ComboBoxItem langItem && langItem.Tag is string language)
            s.Language = language;

        _svc.SettingsStore.Save();
        AutostartHelper.Set(s.Autostart);
        _svc.ApplySettings();
        ThemeManager.Apply(s.Theme); // the preview may have been reverted by other changes - reapply
        L10n.Apply(s.Language);
        this.UseDarkTitleBar(ThemeManager.IsDark);
        Close();
    }

    /// <summary>
    /// Saves all four hotkeys: drops invalid ones (with a warning) and prevents
    /// assigning the same combo to two actions.
    /// </summary>
    private void SaveHotkeys(Settings.AppSettings s)
    {
        var fields = new (TextBox Box, Action<string> Assign, string Current)[]
        {
            (TxtFixHotkey, v => s.FixHotkey = v, s.FixHotkey),
            (TxtTranslateHotkey, v => s.TranslateHotkey = v, s.TranslateHotkey),
            (TxtCaptureHotkey, v => s.CaptureHotkey = v, s.CaptureHotkey),
            (TxtPauseHotkey, v => s.PauseHotkey = v, s.PauseHotkey),
        };

        var parsed = fields
            .Select(f => (Field: f, Combo: HotkeyCombo.Parse(f.Box.Text.Trim())))
            .ToList();

        var duplicate = parsed
            .Where(x => x.Combo is not null)
            .GroupBy(x => x.Combo!.Value)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
        {
            _svc.Tray.ShowBalloon(L10n.T("tray.settings"), L10n.T("balloon.hotkey.dup", duplicate.Key));
            return;
        }

        foreach (var (field, combo) in parsed)
        {
            if (combo is { } c)
                field.Assign(c.ToString());
            else if (field.Box.Text.Trim() != field.Current)
                _svc.Tray.ShowBalloon(L10n.T("tray.settings"),
                    L10n.T("balloon.hotkey.invalid", field.Box.Text.Trim(), field.Current));
        }
    }

    // ---------------- Hotkey recorder ----------------

    private bool _hotkeysSuspended;

    private void StartRecording(TextBox box)
    {
        if (_recordingBox != null && _recordingBox != box)
        {
            _recordingBox.Text = _originalText;
            StopRecording(_recordingBox);
        }
        _recordingBox = box;
        _originalText = box.Text;
        box.Text = L10n.T("hotkey.recording");
        // Highlight from the theme: yellow Moccasin is unreadable in the dark theme
        box.SetResourceReference(System.Windows.Controls.TextBox.BackgroundProperty, "BgAccent");
        box.Focus();

        // While recording, app hotkeys are released: otherwise registered
        // combos are captured by the system before this window sees them
        if (!_hotkeysSuspended)
        {
            _svc.SuspendHotkeys();
            _hotkeysSuspended = true;
        }
    }

    private string _originalText = string.Empty;

    private void RecorderOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = _recordingBox;
        if (box == null) return; // not recording - the window works as usual
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            box.Text = _originalText;
            TxtHotkeyStatus.Text = "";
            StopRecording(box);
            return;
        }

        bool isPureModifier = e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
        if (isPureModifier && e.Key != Key.System) return;

        var pressed = e.Key == Key.System ? e.SystemKey : e.Key;
        // Alt itself arrives as a System event (SystemKey = LeftAlt) - otherwise
        // "Alt+0xA4" would be recorded instead of the combo. Wait for the main key.
        if (pressed is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var combo = HotkeyCombo.FromWpfKey(Keyboard.Modifiers, pressed);
        if (combo == null)
        {
            // Not silent: a letter/digit without modifiers is forbidden - explain and keep recording
            box.Text = L10n.T("hotkey.invalid.press");
            return;
        }

        box.Text = combo.Value.ToString();
        TxtHotkeyStatus.Text = L10n.T("hotkey.status", combo.Value);
        StopRecording(box);
    }

    private void StopRecording(TextBox box)
    {
        box.Background = null; // restore the theme style background (not white on dark)
        if (_recordingBox == box) _recordingBox = null;
        ResumeHotkeysAfterRecording();
    }

    private void ResumeHotkeysAfterRecording()
    {
        if (_recordingBox != null || !_hotkeysSuspended) return;
        _hotkeysSuspended = false;
        _svc.ResumeHotkeys();
    }

    // ---------------- Lists ----------------

    private static void AddToList(ListBox list, TextBox input)
    {
        var value = input.Text.Trim();
        if (value.Length == 0) return;
        if (list.Items.Cast<string>().Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
        list.Items.Add(value);
        input.Text = string.Empty;
    }

    private static void RemoveSelected(ListBox list)
    {
        if (list.SelectedItem is string selected)
            list.Items.Remove(selected);
    }
}
