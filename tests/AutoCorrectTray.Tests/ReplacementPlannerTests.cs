using AutoCorrectTray.Input;
using Xunit;

namespace AutoCorrectTray.Tests;

public class ReplacementPlannerTests
{
    [Fact]
    public void Plan_WithSpaceSeparator_DeletesAndRestoresSeparator()
    {
        // «слово » — каретка после пробела: удалить слово+пробел, вернуть «правильно »
        var plan = ReplacementPlanner.Plan("слово", "правильно", " ", "Привет слово ");
        Assert.NotNull(plan);
        Assert.Equal(6, plan!.Value.Backspaces);
        Assert.Equal("правильно ", plan.Value.Insert);
    }

    [Fact]
    public void Plan_WithPunctuationSeparator()
    {
        var plan = ReplacementPlanner.Plan("слово", "правильно", ",", "Привет слово,");
        Assert.NotNull(plan);
        Assert.Equal(6, plan!.Value.Backspaces);
        Assert.Equal("правильно,", plan.Value.Insert);
    }

    [Fact]
    public void Plan_StaleBuffer_ReturnsNull()
    {
        // Пользователь уже успел набрать другое — замена отменилась
        Assert.Null(ReplacementPlanner.Plan("слово", "правильно", " ", "Привет другое "));
        Assert.Null(ReplacementPlanner.Plan("слово", "правильно", " ", ""));
    }

    [Fact]
    public void Plan_WithoutSeparator_CountsWordOnly()
    {
        var plan = ReplacementPlanner.Plan("слово", "правильно", null, "что угодно");
        Assert.NotNull(plan);
        Assert.Equal(5, plan!.Value.Backspaces);
        Assert.Equal("правильно", plan.Value.Insert);
    }
}
