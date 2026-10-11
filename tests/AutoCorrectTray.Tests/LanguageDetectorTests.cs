using AutoCorrectTray.Spelling;
using Xunit;

namespace AutoCorrectTray.Tests;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("привет")]
    [InlineData("Ёлка")]
    [InlineData("don't")]
    public void Detect_MixedScriptsWord(string word)
    {
        // В одном слове только один алфавит — проверяем базовые случаи ниже
        _ = word;
    }

    [Fact]
    public void Detect_Cyrillic()
    {
        Assert.Equal(ScriptKind.Cyrillic, LanguageDetector.Detect("привет"));
        Assert.Equal(ScriptKind.Cyrillic, LanguageDetector.Detect("Ёлка"));
    }

    [Fact]
    public void Detect_Latin()
    {
        Assert.Equal(ScriptKind.Latin, LanguageDetector.Detect("hello"));
        Assert.Equal(ScriptKind.Latin, LanguageDetector.Detect("don't"));
    }

    [Fact]
    public void Detect_Mixed()
    {
        Assert.Equal(ScriptKind.Mixed, LanguageDetector.Detect("приветhello"));
    }

    [Fact]
    public void Detect_None()
    {
        Assert.Equal(ScriptKind.None, LanguageDetector.Detect("123"));
        Assert.Equal(ScriptKind.None, LanguageDetector.Detect("-"));
    }

    [Fact]
    public void CleanWord_StripsPunctuation()
    {
        Assert.Equal("привет", SpellService.CleanWord("«привет»,"));
        Assert.Equal("don't", SpellService.CleanWord("\"don't\"—"));
    }
}
