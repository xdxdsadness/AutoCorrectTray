# Architecture

AutoCorrect Tray is a system-tray WPF app (.NET 8) that watches keyboard input globally and
provides word correction, auto-punctuation and translation in any Windows text field.

## Big picture

```
Keyboard hook (WH_KEYBOARD_LL)          Mouse hook (WH_MOUSE_LL)
        |                                       |
        v                                       v
  TypedTextBuffer  <---- reset ---->  ForegroundMonitor (EVENT_SYSTEM_FOREGROUND)
        |                                       |
        v                                       v
  PunctuationEngine / SpellService     Popups (HintPopup / TranslatePopup)
        |                                       ^
        +-------------- AppServices ------------+
                            |
        +-------------------+--------------------+
        v                   v                    v
  UiTextService       Translation/            TrayService
  (UI Automation)     GoogleTranslateProvider (tray icon, menu)
```

`AppServices` is the composition root: it owns the hooks, buffers, services and popups, and wires
their events together. It is created in `App.OnStartup` and disposed on exit.

## Input pipeline

1. `KeyboardHookService` (WH_KEYBOARD_LL, background thread) tracks printable characters into
   `TypedTextBuffer`, suppresses and re-injects auto-punctuation (`PunctuationEngine`) and raises
   word-completion checks.
2. `SpellService` checks the finished word with Hunspell (`Spelling/`, ru + en dictionaries bundled
   with the app). Misspelled words raise a suggestion bubble (`HintPopup`) at the caret;
   digits `1-9` pick a suggestion.
3. `InputInjector` performs all typing/backspacing via `SendInput`. Replacements never happen
   without an explicit user action.

## Translation

- `TranslateSelectedAsync` reads the selection via UI Automation (`UiTextService`) or, with nothing
  selected, switches the popup into live mode (follows typing).
- `CaptureUnderCursorAsync` opens the region selector (`RegionSelector`); on release, all text lines
  inside the region are read with UI Automation (`UiTextService.GetLinesInRegion`) and translated
  line-by-line into `WindowTranslateOverlay`, where each block of lines becomes one selectable
  read-only text box positioned over its original lines.
- `ChunkedTranslationProvider` splits long texts into sentence-bounded chunks before hitting the
  free Google Translate endpoint; `FallbackTranslationService` tries the main endpoint, a mirror,
  and MyMemory in order.

## Popups and focus

All popups are `WS_EX_NOACTIVATE` tool windows (they never steal keyboard focus), except the region
translation overlay, which is activatable on click so text selection and `Ctrl+C` work.
`MonitorFollow` moves the translation window to the monitor of the foreground app, keeping its
relative position and converting coordinates across different DPI scales (the app is
PerMonitorV2-aware).

## Hotkeys and settings

`HotkeyManager` registers global hotkeys (`RegisterHotKey`) on a message-only window;
`HotkeyCombo` parses/validates combinations (modifier-required unless the key is "safe" like
F1-F24). Settings live in `%APPDATA%\AutoCorrectTray\settings.json` (`SettingsStore`), the log in
`log.txt` next to it. UI strings are stored in `Strings/en.xaml` / `Strings/ru.xaml` and swapped at
runtime by `L10n`; colors live in `Themes/Light.xaml` / `Themes/Dark.xaml` swapped by `ThemeManager`.

## Tests

Pure logic (punctuation rules, typed-text buffer, hotkey parsing, replacement planning, language
detection, translator response parsing, region geometry math) is covered by xUnit tests in
`tests/AutoCorrectTray.Tests`. UI and hooks are kept thin and untested by design.
