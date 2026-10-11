using AutoCorrectTray.Translation;
using Xunit;

namespace AutoCorrectTray.Tests;

public class GoogleTranslateParserTests
{
    [Fact]
    public void Parse_ChunksConcatenated()
    {
        const string json = """[[["Привет ","Hello",null,null,10],["мир","world",null,null,10]],null,"en"]""";
        var parsed = GoogleTranslateProvider.ParseResponse(json);
        Assert.NotNull(parsed);
        Assert.Equal("Привет мир", parsed!.Value.text);
        Assert.Equal("en", parsed.Value.detected);
    }

    [Fact]
    public void Parse_HtmlEntitiesDecoded()
    {
        const string json = """[[["Tom &amp; Jerry","Tom & Jerry",null,null,10]],null,"en"]""";
        var parsed = GoogleTranslateProvider.ParseResponse(json);
        Assert.NotNull(parsed);
        Assert.Equal("Tom & Jerry", parsed!.Value.text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""["str","str2","ru"]""")]
    public void Parse_RejectsInvalid(string json)
    {
        Assert.Null(GoogleTranslateProvider.ParseResponse(json));
    }
}
