using AutoCorrectTray.Input;
using Xunit;

namespace AutoCorrectTray.Tests;

public class TypedTextBufferTests
{
    [Fact]
    public void CurrentWord_ReturnsTrailingWord()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет, как дела");
        Assert.Equal("дела", buffer.CurrentWord);
    }

    [Fact]
    public void CurrentWord_EmptyAfterSpace()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет ");
        Assert.Equal(string.Empty, buffer.CurrentWord);
    }

    [Fact]
    public void Backspace_RemovesOneChar()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет");
        buffer.Backspace();
        Assert.Equal("приве", buffer.Text);
    }

    [Fact]
    public void Backspace_RemovesSurrogatePair()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("a😀");
        buffer.Backspace();
        Assert.Equal("a", buffer.Text);
    }

    [Fact]
    public void ReplaceLastWord()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет и");
        buffer.ReplaceLastWord("и");
        Assert.Equal("привет и", buffer.Text); // нетTail-совпадений
    }

    [Fact]
    public void ReplaceLastWord_Works()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет хелп");
        buffer.ReplaceLastWord("help");
        Assert.Equal("привет help", buffer.Text);
    }

    [Fact]
    public void CurrentSentence_AfterTerminator()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("Первая фраза. Вторая фраза");
        Assert.Equal("Вторая фраза", buffer.CurrentSentence);
    }

    [Fact]
    public void CommitSentence_StoresLastSentence()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("Привет мир");
        buffer.CommitSentence();
        Assert.Equal("Привет мир", buffer.LastSentence);
        Assert.Equal(string.Empty, buffer.Text);
    }

    [Fact]
    public void PendingSentence_FallsBackToLast()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("Первое предложение. Оно тут");
        buffer.CommitSentence();
        // Последнее предложение — то, что после последнего разделителя
        Assert.Equal("Оно тут", buffer.LastSentence);
        Assert.Equal("Оно тут", buffer.PendingSentence);
    }

    [Fact]
    public void ApplyPlan_ReplacesComma()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет,");
        buffer.ApplyPlan(1, ".");
        Assert.Equal("привет.", buffer.Text);
    }

    [Fact]
    public void TrimEnd_RemovesSuffix()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("привет мир");
        buffer.TrimEnd(4); // " мир"
        Assert.Equal("привет", buffer.Text);
    }
    [Fact]
    public void FullText_KeptAfterSentenceTerminator()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("First sentence. Second sentence.");
        // A terminator does not erase previously typed text: the buffer keeps it all
        Assert.Equal("First sentence. Second sentence.", buffer.Text);
    }
}
