using System.Windows.Input;
using AutoCorrectTray.Native;

namespace AutoCorrectTray.Hotkeys;

/// <summary>Key combo: modifiers + virtual key.</summary>
public readonly record struct HotkeyCombo(uint Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<string, uint> ModifiersByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = NativeMethods.MOD_CONTROL,
        ["control"] = NativeMethods.MOD_CONTROL,
        ["alt"] = NativeMethods.MOD_ALT,
        ["shift"] = NativeMethods.MOD_SHIFT,
        ["win"] = NativeMethods.MOD_WIN,
        ["windows"] = NativeMethods.MOD_WIN,
    };

    private static readonly Dictionary<string, uint> KeysByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["space"] = 0x20,
        ["insert"] = 0x2D, ["ins"] = 0x2D,
        ["delete"] = 0x2E, ["del"] = 0x2E,
        ["home"] = 0x24, ["end"] = 0x23,
        ["pgup"] = 0x21, ["pgdn"] = 0x22,
        ["tab"] = 0x09, ["esc"] = 0x1B,
        ["backspace"] = 0x08,
        ["scroll"] = 0x91,
        ["pause"] = 0x13,
    };

    public static HotkeyCombo? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint modifiers = 0;
        uint? key = null;

        foreach (var rawPart in text.Split('+'))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) return null;
            if (ModifiersByName.TryGetValue(part, out var mod))
            {
                modifiers |= mod;
                continue;
            }
            if (key != null) return null; // second non-modifier key
            key = ParseKey(part);
            if (key == null) return null;
        }

        if (key == null) return null;
        // Without modifiers only "safe" keys are allowed: a lone letter/digit
        // would swallow the character system-wide; F-keys/Pause/Scroll do not
        if (modifiers == 0 && !IsSafeStandaloneKey(key.Value)) return null;
        return new HotkeyCombo(modifiers, key.Value);
    }

    /// <summary>
    /// Keys that can be hotkeys without modifiers: they do not type characters
    /// and are rarely needed on their own, so they do not swallow input.
    /// </summary>
    public static bool IsSafeStandaloneKey(uint vk) =>
        vk is >= 0x70 and <= 0x87   // F1–F24
            or 0x13                 // Pause
            or 0x91;                // Scroll Lock


    private static uint? ParseKey(string part)
    {
        if (part.Length == 1)
        {
            char c = char.ToUpperInvariant(part[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
            return null;
        }
        if (part.Length is 2 or 3 && part[0] is 'F' or 'f' && int.TryParse(part[1..], out var f) && f is >= 1 and <= 24)
            return (uint)(0x70 + f - 1);
        return KeysByName.TryGetValue(part, out var named) ? named : null;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyToString(VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyToString(uint vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x70 + 1}";
        var named = KeysByName.FirstOrDefault(kv => kv.Value == vk).Key;
        return string.IsNullOrEmpty(named) ? $"0x{vk:X2}" : named;
    }

    /// <summary>For the recorder in the settings window: WPF key + modifiers → combo.</summary>
    public static HotkeyCombo? FromWpfKey(ModifierKeys modifiers, Key key)
    {
        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0) return null;
        // Modifier keys themselves (VK_LMENU 0xA4 etc.) cannot be the main key
        if (vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x90 or 0x5B or 0x5C
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return null;
        if (mods == 0 && !IsSafeStandaloneKey((uint)vk)) return null;
        return new HotkeyCombo(mods, (uint)vk);
    }
}
