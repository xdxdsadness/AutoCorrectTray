using AutoCorrectTray.Translation;
using Xunit;

namespace AutoCorrectTray.Tests;

public class ChunkedTranslationProviderTests
{
    [Fact]
    public void Split_ShortText_SingleChunk()
    {
        var chunks = ChunkedTranslationProvider.Split("привет мир", 100);
        Assert.Single(chunks);
        Assert.Equal("привет мир", chunks[0]);
    }

    [Fact]
    public void Split_LongText_RespectsLimit()
    {
        var text = string.Join(" ", Enumerable.Repeat("слово", 500)); // 3000 символов
        var chunks = ChunkedTranslationProvider.Split(text, 500);
        Assert.True(chunks.Count > 1);
        foreach (var c in chunks)
            Assert.True(c.Length <= 500, $"кусок {c.Length} > 500");
        // ничего не потеряли и не продублировали
        Assert.Equal(string.Join(" ", text.Split(' ')), string.Join(" ", chunks.SelectMany(c => c.Split(' '))));
    }

    [Fact]
    public void Split_PrefersParagraphBreaks()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("текст", 100)); // ~600
        var text = paragraph + "\n" + paragraph; // ~1200 с переносом
        var chunks = ChunkedTranslationProvider.Split(text, 800);
        Assert.True(chunks.Count >= 2);
        // первый кусок заканчивается по границе строки — без висячего обрывка слова
        Assert.EndsWith("текст", chunks[0]);
    }

    [Fact]
    public void Split_NoBoundaries_HardCut()
    {
        var text = new string('а', 2500);
        var chunks = ChunkedTranslationProvider.Split(text, 1000);
        Assert.Equal(3, chunks.Count);
        Assert.Equal(2500, chunks.Sum(c => c.Length));
    }
}
