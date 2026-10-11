using AutoCorrectTray.Punctuation;
using Xunit;

namespace AutoCorrectTray.Tests;

public class PunctuationEngineTests
{
    private static PunctuationSettings AllOn => new()
    {
        AutoPeriod = true,
        AutoCapitalize = true,
        SpaceAfterPunct = true,
    };

    // ---------- PeriodOnEnter ----------

    [Fact]
    public void PeriodOnEnter_AppendsDot()
    {
        var plan = PunctuationEngine.PeriodOnEnter("привет");
        Assert.NotNull(plan);
        Assert.Equal(0, plan!.Value.Backspaces);
        Assert.Equal(".", plan.Value.Insert);
    }

    [Fact]
    public void PeriodOnEnter_ReplacesCommaWithDot()
    {
        var plan = PunctuationEngine.PeriodOnEnter("привет,");
        Assert.NotNull(plan);
        Assert.Equal(1, plan!.Value.Backspaces);
        Assert.Equal(".", plan.Value.Insert);
    }

    [Theory]
    [InlineData("привет.")]
    [InlineData("привет!")]
    [InlineData("привет?")]
    [InlineData("привет…")]
    public void PeriodOnEnter_SkipsWhenAlreadyTerminated(string text)
    {
        Assert.Null(PunctuationEngine.PeriodOnEnter(text));
    }

    [Fact]
    public void PeriodOnEnter_ConsumesTrailingSpaces()
    {
        var plan = PunctuationEngine.PeriodOnEnter("привет  ");
        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Value.Backspaces);
        Assert.Equal(".", plan.Value.Insert);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("привет —")]
    [InlineData("внимание:")]
    public void PeriodOnEnter_DoesNothingForAmbiguousText(string text)
    {
        Assert.Null(PunctuationEngine.PeriodOnEnter(text));
    }

    [Fact]
    public void PeriodOnEnter_AppendsAfterClosingQuote()
    {
        var plan = PunctuationEngine.PeriodOnEnter("«привет»");
        Assert.NotNull(plan);
        Assert.Equal(0, plan!.Value.Backspaces);
        Assert.Equal(".", plan.Value.Insert);
    }

    // ---------- PlanIncoming: капитализация ----------

    [Fact]
    public void PlanIncoming_CapitalizesAfterSentenceEnd()
    {
        var action = PunctuationEngine.PlanIncoming("Он сказал. ", 'п', AllOn);
        Assert.Equal(IncomingAction.Capitalize, action);
    }

    [Fact]
    public void PlanIncoming_DoesNotCapitalizeShortAbbreviation()
    {
        // «т.», «и.» — сокращения, не конец предложения
        var action = PunctuationEngine.PlanIncoming("т. ", 'д', AllOn);
        Assert.Equal(IncomingAction.Type, action);
    }

    [Theory]
    [InlineData("привет ")]
    [InlineData("привет")]      // без знака + пробела капитализация не включается
    [InlineData("привет.и")]    // «точка+буква» без пробела — сокращение
    public void PlanIncoming_TypesNormallyInsideSentence(string text)
    {
        var action = PunctuationEngine.PlanIncoming(text, 'д', AllOn);
        Assert.Equal(IncomingAction.Type, action);
    }

    [Fact]
    public void PlanIncoming_DoesNotCapitalizeAlreadyUpper()
    {
        var action = PunctuationEngine.PlanIncoming("Он сказал. ", 'П', AllOn);
        Assert.Equal(IncomingAction.Type, action);
    }

    [Fact]
    public void PlanIncoming_NoCapitalizeWhenDisabled()
    {
        var settings = new PunctuationSettings { AutoCapitalize = false };
        var action = PunctuationEngine.PlanIncoming("Он сказал. ", 'п', settings);
        Assert.Equal(IncomingAction.Type, action);
    }

    [Fact]
    public void PlanIncoming_DoesNotCapitalizeAtUnknownStart()
    {
        // Буфер пуст: позиция каретки неизвестна — не вмешиваемся
        var action = PunctuationEngine.PlanIncoming("", 'п', AllOn);
        Assert.Equal(IncomingAction.Type, action);
    }

    // ---------- PlanIncoming: пробел после знака ----------

    [Fact]
    public void PlanIncoming_InsertsSpaceAfterComma()
    {
        var action = PunctuationEngine.PlanIncoming("который,", 'а', AllOn);
        Assert.Equal(IncomingAction.SpaceAndType, action);
    }

    [Fact]
    public void PlanIncoming_NoSpaceInsideNumber()
    {
        var action = PunctuationEngine.PlanIncoming("3.", '5', AllOn);
        Assert.Equal(IncomingAction.Type, action);
    }

    [Fact]
    public void PlanIncoming_NoSpaceAfterSpace()
    {
        var action = PunctuationEngine.PlanIncoming("который, ", 'а', AllOn);
        Assert.Equal(IncomingAction.Type, action);
    }
}
