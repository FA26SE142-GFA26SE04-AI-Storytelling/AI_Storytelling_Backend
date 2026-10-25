using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class QuizRunCalculatorTests
{
    private static QuizAttempt A(int item, bool? correct) => new() { QuizItemId = item, IsCorrect = correct };

    [Fact]
    public void Split_NoAttempts_HasNoRuns()
    {
        Assert.Empty(QuizRunCalculator.Split(Array.Empty<QuizAttempt>()));
    }

    [Fact]
    public void Split_AnsweringEachQuestionOnce_IsOneRun()
    {
        var runs = QuizRunCalculator.Split(new[] { A(1, true), A(2, false), A(3, true) });

        Assert.Equal(new[] { new QuizRun(3, 2) }, runs);
    }

    [Fact]
    public void Split_AnsweringAQuestionAgain_StartsANewRun()
    {
        var runs = QuizRunCalculator.Split(new[] { A(1, false), A(2, true), A(1, true), A(2, true) });

        Assert.Equal(new[] { new QuizRun(2, 1), new QuizRun(2, 2) }, runs);
    }

    [Fact]
    public void Split_SkippedQuestionsSimplyAreNotCounted()
    {
        var runs = QuizRunCalculator.Split(new[] { A(1, true), A(3, true) });

        Assert.Equal(new[] { new QuizRun(2, 2) }, runs);
    }

    [Fact]
    public void Split_NullCorrectnessIsNotCounted()
    {
        var runs = QuizRunCalculator.Split(new[] { A(1, null), A(2, true) });

        Assert.Equal(new[] { new QuizRun(2, 1) }, runs);
    }

    [Theory]
    [InlineData(0, 4, 0)]
    [InlineData(1, 4, 25)]
    [InlineData(3, 4, 75)]
    [InlineData(1, 3, 33.3)]
    [InlineData(4, 4, 100)]
    [InlineData(2, 0, 0)]
    public void Percent_IsRoundedToOneDecimal_AndZeroWhenNoQuestions(int correct, int total, double expected)
    {
        Assert.Equal((decimal)expected, QuizRunCalculator.Percent(correct, total));
    }

    [Fact]
    public void BestPercent_PicksTheBestRun_AndIsNullWithoutRuns()
    {
        var runs = new[] { new QuizRun(4, 1), new QuizRun(4, 4), new QuizRun(4, 0) };

        Assert.Equal(100m, QuizRunCalculator.BestPercent(runs, 4));
        Assert.Null(QuizRunCalculator.BestPercent(Array.Empty<QuizRun>(), 4));
    }
}
