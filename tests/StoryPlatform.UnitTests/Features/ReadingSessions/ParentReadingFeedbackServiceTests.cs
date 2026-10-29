using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ParentReadingFeedbackServiceTests
{
    private const int Parent = 7;

    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly ParentReadingFeedbackService _sut;

    public ParentReadingFeedbackServiceTests()
    {
        _sut = new ParentReadingFeedbackService(_h.UnitOfWork.Object, _guard.Object);
    }

    private ReadingSession Done(DateTime completedAt, StoryFeeling? feeling = null, int childId = ReadingHarness.Child)
    {
        var session = _h.AddSession(SessionStatus.Completed, childId: childId);
        session.CompletedAt = completedAt;
        session.Feeling = feeling;
        return session;
    }

    private QuizItem Quiz(int id)
    {
        var item = new QuizItem
        {
            Id = id, StoryVersionId = ReadingHarness.Version, Type = QuizType.MultipleChoice,
            Question = $"Câu {id}", CorrectAnswer = "a"
        };
        _h.Quizzes.Add(item);
        return item;
    }

    private void Attempt(int sessionId, int itemId, bool correct) =>
        _h.Attempts.Add(new QuizAttempt
        {
            Id = _h.Attempts.Count + 1, ReadingSessionId = sessionId, QuizItemId = itemId, IsCorrect = correct
        });

    private Task<StoryPlatform.Application.Common.Models.PagedResult<ParentReadingFeedbackItemDto>> List(
        ParentReadingFeedbackQueryDto? query = null) =>
        _sut.ListAsync(Parent, ReadingHarness.Child, query ?? new ParentReadingFeedbackQueryDto());

    [Fact]
    public async Task ListAsync_ChecksTheViewProgressPermissionForThisParentAndChild()
    {
        await List();

        _guard.Verify(g => g.EnsureOwnerAsync(
            ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_WithoutPermission_IsForbiddenAndReadsNothing()
    {
        Done(DateTime.UtcNow, StoryFeeling.Like);
        _guard.Setup(g => g.EnsureOwnerAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("không đủ quyền"));

        await Assert.ThrowsAsync<ForbiddenException>(() => List());
    }

    [Fact]
    public async Task ListAsync_ShowsFinishedSessionsNewestFirst_WithTitleDateAndFeeling()
    {
        var older = Done(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), StoryFeeling.Like);
        var newer = Done(new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc), StoryFeeling.VeryLike);

        var page = await List();

        Assert.Equal(new[] { newer.Id, older.Id }, page.Items.Select(i => i.SessionId));
        var first = page.Items[0];
        Assert.Equal(ReadingHarness.Story, first.StoryId);
        Assert.Equal("Truyện 10", first.Title);
        Assert.Equal(newer.CompletedAt, first.CompletedAt);
        Assert.Equal("VeryLike", first.Feeling);
        Assert.Equal("Rất thích", first.FeelingLabel);
    }

    [Fact]
    public async Task ListAsync_NoFeelingChosen_HasNullFeelingAndLabel()
    {
        Done(DateTime.UtcNow);

        var item = (await List()).Items.Single();

        Assert.Null(item.Feeling);
        Assert.Null(item.FeelingLabel);
    }

    [Fact]
    public async Task ListAsync_IgnoresUnfinishedSessionsAndOtherChildren()
    {
        var mine = Done(DateTime.UtcNow);
        _h.AddSession(SessionStatus.Reading);
        _h.AddSession(SessionStatus.Abandoned);
        _h.AddSession(SessionStatus.ForceExited);
        Done(DateTime.UtcNow, childId: ReadingHarness.OtherChild);

        var page = await List();

        Assert.Equal(new[] { mine.Id }, page.Items.Select(i => i.SessionId));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_QuizBestPercent_ComesFromTheBestRunAcrossSessions()
    {
        Quiz(1); Quiz(2);
        var first = Done(DateTime.UtcNow.AddDays(-2));
        var second = Done(DateTime.UtcNow.AddDays(-1));
        Attempt(first.Id, 1, true); Attempt(first.Id, 2, false);     // lần 1: 50%
        Attempt(second.Id, 1, true); Attempt(second.Id, 2, true);    // lần 2: 100%

        var page = await List();

        Assert.All(page.Items, item =>
        {
            Assert.Equal(2, item.QuizTotalQuestions);
            Assert.Equal(100m, item.QuizBestPercent);
        });
    }

    [Fact]
    public async Task ListAsync_QuizNeverTaken_HasNullBestPercent_AndStoryWithoutQuizHasZeroQuestions()
    {
        Quiz(1);
        Done(DateTime.UtcNow);

        var withQuiz = (await List()).Items.Single();
        Assert.Equal(1, withQuiz.QuizTotalQuestions);
        Assert.Null(withQuiz.QuizBestPercent);

        _h.Quizzes.Clear();
        var withoutQuiz = (await List()).Items.Single();
        Assert.Equal(0, withoutQuiz.QuizTotalQuestions);
        Assert.Null(withoutQuiz.QuizBestPercent);
    }

    [Fact]
    public async Task ListAsync_DoesNotMixInAnotherChildsQuizAttempts()
    {
        Quiz(1);
        var mine = Done(DateTime.UtcNow);
        var other = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);
        Attempt(other.Id, 1, true);

        var item = (await List()).Items.Single(i => i.SessionId == mine.Id);

        Assert.Null(item.QuizBestPercent);
    }

    [Fact]
    public async Task ListAsync_Paging_ClampsSizeAndReportsTotal()
    {
        for (var i = 0; i < 5; i++)
        {
            Done(DateTime.UtcNow.AddMinutes(-i));
        }

        var page = await List(new ParentReadingFeedbackQueryDto { PageIndex = 2, PageSize = 2 });
        var huge = await List(new ParentReadingFeedbackQueryDto { PageIndex = 0, PageSize = 999 });

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.PageIndex);
        Assert.Equal(1, huge.PageIndex);
        Assert.Equal(50, huge.PageSize);
        Assert.Equal(5, huge.Items.Count);
    }
}
