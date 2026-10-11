using AutoCorrectTray.Hotkeys;
using Xunit;

namespace AutoCorrectTray.Tests;

public class HotkeyComboTests
{
    [Fact]
    public void Parse_CtrlAltF()
    {
        var combo = HotkeyCombo.Parse("Ctrl+Alt+F");
        Assert.NotNull(combo);
        Assert.Equal('F', combo!.Value.VirtualKey);
        Assert.NotEqual(0u, combo.Value.Modifiers & 0x3u); // Ctrl | Alt
    }

    [Fact]
    public void Parse_FunctionKeys()
    {
        var combo = HotkeyCombo.Parse("Ctrl+F12");
        Assert.NotNull(combo);
        Assert.Equal((uint)(0x70 + 12 - 1), combo!.Value.VirtualKey);
    }

    [Fact]
    public void Parse_SingleFunctionKey_NoModifiers()
    {
        var combo = HotkeyCombo.Parse("F8");
        Assert.NotNull(combo);
        Assert.Equal((uint)(0x70 + 8 - 1), combo!.Value.VirtualKey);
        Assert.Equal(0u, combo!.Value.Modifiers);
        Assert.Equal("F8", combo.Value.ToString());
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F24")]
    [InlineData("Pause")]
    [InlineData("Scroll")]
    public void Parse_SafeStandaloneKeys_Accepted(string text)
    {
        Assert.NotNull(HotkeyCombo.Parse(text));
    }

    [Theory]
    [InlineData("Ctrl")]           // нет клавиши
    [InlineData("T")]              // одиночная буква «съест» ввод во всей системе
    [InlineData("5")]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Ctrl+Alt+X+Y")]   // две клавиши
    [InlineData("Ctrl+ГГ")]        // мусор
    [InlineData("")]
    [InlineData(null)]
    public void Parse_RejectsInvalid(string? text)
    {
        Assert.Null(HotkeyCombo.Parse(text));
    }

    [Fact]
    public void ToString_RoundTrip()
    {
        var combo = HotkeyCombo.Parse("Ctrl+Alt+T");
        Assert.NotNull(combo);
        Assert.Equal("Ctrl+Alt+T", combo!.Value.ToString());
    }

    [Fact]
    public void FromWpfKey_BuildsCombo()
    {
        var combo = HotkeyCombo.FromWpfKey(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt,
            System.Windows.Input.Key.P);
        Assert.NotNull(combo);
        Assert.Equal('P', combo!.Value.VirtualKey);
        Assert.Equal("Ctrl+Alt+P", combo.Value.ToString());
    }

    [Fact]
    public void FromWpfKey_SingleFunctionKey_NoModifiers()
    {
        var combo = HotkeyCombo.FromWpfKey(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.F8);
        Assert.NotNull(combo);
        Assert.Equal("F8", combo!.Value.ToString());
    }

    [Fact]
    public void FromWpfKey_RejectsSingleLetter()
    {
        var combo = HotkeyCombo.FromWpfKey(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.T);
        Assert.Null(combo);
    }
}
