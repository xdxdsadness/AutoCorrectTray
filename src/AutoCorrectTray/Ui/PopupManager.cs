using AutoCorrectTray.Native;
using AutoCorrectTray.Translation;
using System.Windows;

namespace AutoCorrectTray.Ui;

/// <summary>Manages the two popups: spelling hints and translation.</summary>
public sealed class PopupManager
{
    private readonly AppServices _svc;
    private readonly HintPopup _hint;
    private readonly TranslatePopup _translate;
    private readonly WindowTranslateOverlay _windowOverlay;
    private readonly RegionSelector _regionSelector;

    public PopupManager(AppServices svc)
    {
        _svc = svc;
        _hint = new HintPopup(svc);
        _translate = new TranslatePopup(svc);
        _windowOverlay = new WindowTranslateOverlay(svc);
        _regionSelector = new RegionSelector();
        // Keep both windows above shell flyouts (Windows search, toasts)
        _hint.EnableTopmostKeeper();
        _translate.EnableTopmostKeeper();
        _translate.EnableDropDownTopmostFix();
    }

    /// <summary>Region selection mode open?</summary>
    public bool IsRegionSelectorOpen
    {
        get { try { return _regionSelector.IsVisible; } catch { return false; } }
    }

    /// <summary>"Reverse scissors" mode: the user outlines an area - callback receives its rectangle (physical pixels).</summary>
    public void StartRegionSelection(Action<Rect> onSelected)
    {
        try
        {
            _regionSelector.Start(onSelected);
        }
        catch (Exception ex)
        {
            _svc.Log.Error("Failed to open region selection", ex);
        }
    }

    /// <summary>Translate window open (in any mode).</summary>
    public bool IsTranslateVisible
    {
        get { try { return _translate.IsVisible; } catch { return false; } }
    }

    /// <summary>The translate window is "sticky": follows new selections and does not close by itself.</summary>
    private bool TranslateStickyFollow =>
        _svc.Settings.TranslateEnabled && _svc.Settings.AutoTranslateSelection;

    public void ShowSuggestions(string word, string[] suggestions, Rect? anchorPhysical,
        Action<string> onPick, Action onAddToDictionary)
    {
        try
        {
            // Both windows stay active: the spell bubble does not close the translate window
            _hint.ShowFor(word, suggestions, anchorPhysical, onPick, onAddToDictionary);
        }
        catch { }
    }

    public void ShowTranslation(string source, TranslationMode mode)
    {
        try
        {
            _hint.Hide();
            _translate.ShowFor(source, mode);
        }
        catch { }
    }

    /// <summary>New selection while the window is open: update the text and translation without moving the window.</summary>
    public void UpdateTranslationForSelection(string text)
    {
        try { _translate.UpdateForSelection(text); } catch { }
    }

    /// <summary>Close the translate window (hotkey pressed again, pause).</summary>
    public void HideTranslation()
    {
        try { if (_translate.IsVisible) _translate.HideWindow(); } catch { }
    }

    /// <summary>Whole-window overlay shown? (pressing the hotkey again hides it)</summary>
    public bool IsWindowOverlayVisible
    {
        get { try { return _windowOverlay.IsVisible; } catch { return false; } }
    }

    /// <summary>Show the "translate on top of text" overlay for the whole window.</summary>
    public void ShowWindowOverlay(IReadOnlyList<(string Text, Rect Rect, bool LabelStrip)> lines, Rect windowRect)
    {
        try
        {
            _hint.Hide();
            _windowOverlay.ShowFor(lines, windowRect);
        }
        catch { }
    }

    /// <summary>Hide the overlay (click, Esc, window change, auto-timer) and close region selection.</summary>
    public void HideOverlay()
    {
        try { if (_windowOverlay.IsVisible) _windowOverlay.Hide(); } catch { }
        try { if (_regionSelector.IsVisible) _regionSelector.Hide(); } catch { }
    }

    /// <summary>The active window changed: the open translate window moves to its monitor.</summary>
    public void FollowForeground(IntPtr foregroundHwnd)
    {
        try { _translate.FollowToForegroundWindow(foregroundHwnd); } catch { }
    }

    public void HideAll() => HideAll(forceTranslateHide: false);

    /// <summary>
    /// forceTranslateHide - hide the translate window even in "sticky" mode (pause).
    /// Normally the sticky window survives a click-away, scroll and window change.
    /// </summary>
    public void HideAll(bool forceTranslateHide)
    {
        try { if (_hint.IsVisible) _hint.Hide(); } catch { }
        // The region overlay is NOT hidden on context change: it is the result of a
        // selection and lives until a click, Esc, the hotkey again or the timer -
        // otherwise the translation vanished the moment focus returned to the app
        // after the selection. The live translate window is closed only on context
        // change (window switch/pause), not on space, scroll and similar typing events.
        if (forceTranslateHide || !TranslateStickyFollow)
        {
            try { if (!_translate.IsLive && _translate.IsVisible) _translate.HideWindow(); } catch { }
        }
    }

    /// <summary>Live translate window open?</summary>
    public bool IsTranslateLiveVisible
    {
        get { try { return _translate.IsVisible && _translate.IsLive; } catch { return false; } }
    }

    /// <summary>A live translate request is in flight (do not duplicate auto-replace).</summary>
    public bool IsTranslationBusy
    {
        get { try { return _translate.IsLiveBusy; } catch { return false; } }
    }

    /// <summary>Hide only the hint bubble (the translate window is untouched).</summary>
    public void HideHintIfVisible()
    {
        try { if (_hint.IsVisible) _hint.Hide(); } catch { }
    }

    /// <summary>Hint bubble open? Then a digit 1-9 picks a suggestion.</summary>
    public bool TryPickSuggestionByDigit(char digit)
    {
        try { return _hint.IsVisible && _hint.HandleDigit(digit); }
        catch { return false; }
    }

    public void HandleClickAway(NativeMethods.POINT point)
    {
        // A click on any window of our process (including ComboBox dropdowns,
        // which live in separate popup windows) must not close our popups.
        var hit = NativeMethods.WindowFromPoint(point);
        if (hit != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(hit, out var pid);
            if (pid == (uint)Environment.ProcessId)
                return;
        }

        try { if (_hint.IsVisible) _hint.Hide(); } catch { }
        HideOverlay(); // click-away hides the overlay
        // The "sticky" translate window is not closed by a click: the user is
        // watching the translation of new selections; it is closed by its button
        // or by pressing the hotkey again
        if (!TranslateStickyFollow)
        {
            try { if (!_translate.IsLive && _translate.IsVisible) _translate.HideWindow(); } catch { }
        }
    }
}
