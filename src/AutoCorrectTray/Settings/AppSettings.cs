namespace AutoCorrectTray.Settings;

public sealed class AppSettings
{
    // --- Autopunctuation ---
    public bool AutoPeriod { get; set; } = true;
    public bool AutoCapitalize { get; set; } = true;
    public bool SpaceAfterPunct { get; set; } = false;

    // --- Features ---
    public bool SpellEnabled { get; set; } = true;
    public bool RightClickFix { get; set; } = true;
    public bool DoubleClickFix { get; set; } = true;
    public bool TranslateEnabled { get; set; } = true;
    /// <summary>While the translate window is open: a new selection is translated automatically, the window stays open.</summary>
    public bool AutoTranslateSelection { get; set; } = true;

    // --- Misc ---
    public bool Autostart { get; set; } = false;
    public bool AutoReplaceLive { get; set; } = true;

    // --- Appearance ---
    /// <summary>UI theme: Light or Dark.</summary>
    public string Theme { get; set; } = "Light";
    /// <summary>UI language: en or ru.</summary>
    public string Language { get; set; } = "en";

    // --- Translation ---
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "ru";
    public double? TranslateWindowLeft { get; set; }
    public double? TranslateWindowTop { get; set; }

    // --- Hotkeys ---
    public string FixHotkey { get; set; } = "Alt+1";
    // Single key or Alt+digit: select text - press one or two keys.
    // Pressing again closes the window.
    public string TranslateHotkey { get; set; } = "Alt+2";
    public string PauseHotkey { get; set; } = "Alt+4";
    /// <summary>Translation of a selected region (works even where text cannot be selected).</summary>
    public string CaptureHotkey { get; set; } = "Alt+3";

    // --- Exclusions and personal dictionary ---
    public List<string> BlacklistedApps { get; set; } = DefaultBlacklist();
    public List<string> UserWords { get; set; } = new();

    public static List<string> DefaultBlacklist() => new()
    {
        // IDEs and code editors
        "devenv", "code", "idea64", "idea", "rider64", "pycharm64", "webstorm64",
        "clion64", "goland64", "datagrip64", "studio64", "rstudio",
        // Terminals
        "windowsterminal", "openconsole", "conhost", "cmd", "powershell", "pwsh",
        "mintty", "alacritty", "wezterm-gui", "hyper",
        // Remote sessions
        "mstsc", "msrdc", "ssh",
    };
}
