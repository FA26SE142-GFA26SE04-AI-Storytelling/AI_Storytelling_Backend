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
    public async Task ListAsync_PracticeResults_AreSeparateAndLimitedToTheirSession()
    {
        var practiced = Done(DateTime.UtcNow);
        var other = Done(DateTime.UtcNow.AddMinutes(-1));
        _h.Telemetry.Add(new TelemetryLog
        {
            Id = 1, ReadingSessionId = practiced.Id, EventType = TelemetryEventType.AsrWordResult,
            EventPayload = "{\"interactionId\":70,\"total\":8,\"good\":6,\"retry\":2,\"skipped\":0}"
        });

        var page = await List();

        var result = page.Items.Single(x => x.SessionId == practiced.Id);
        Assert.Equal(1, result.ReadAloudPractice!.Attempts);
        Assert.Equal(75m, result.ReadAloudPractice.LatestGoodPercent);
        Assert.Null(result.ReadAloud);
        Assert.Null(page.Items.Single(x => x.SessionId == other.Id).ReadAloudPractice);
    }

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
    private void Interaction(int id, CheckpointKind? kind, ListeningInteractionType type = ListeningInteractionType.Checkpoint,
        int? sceneId = null) =>
        _h.ListeningInteractions.Add(new ListeningInteraction
        {
            Id = id, StoryVersionId = ReadingHarness.Version, Type = type, Kind = kind,
            Prompt = $"Câu hỏi {id}", StorySceneId = sceneId
        });

    private void Choice(int optionId, int interactionId, string text) =>
        _h.ListeningOptions.Add(new ListeningInteractionOption
        {
            Id = optionId, ListeningInteractionId = interactionId, SortOrder = 1, Text = text
        });

    private void Answered(int sessionId, int interactionId, int optionId, DateTime at) =>
        _h.ListeningAttempts.Add(new ListeningInteractionAttempt
        {
            Id = _h.ListeningAttempts.Count + 1, ReadingSessionId = sessionId, ChildProfileId = ReadingHarness.Child,
            ListeningInteractionId = interactionId, SelectedOptionId = optionId, AttemptedAt = at
        });

    [Fact]
    public async Task ListAsync_ShowsEmotionAndOpinionAnswersInOrder_WithSceneIndex()
    {
        var scene = _h.AddScene(2, "Scene 2");
        var session = Done(DateTime.UtcNow);
        Interaction(1, CheckpointKind.Emotion, sceneId: scene.Id);
        Interaction(2, CheckpointKind.Opinion);
        Choice(10, 1, "Thỏ đang sợ");
        Choice(20, 2, "Con thích bác Gấu");
        var start = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
        Answered(session.Id, 2, 20, start.AddMinutes(2));
        Answered(session.Id, 1, 10, start);

        var answers = (await List()).Items.Single().ConversationAnswers;

        Assert.Equal(new[] { "Câu hỏi 1", "Câu hỏi 2" }, answers.Select(x => x.Question));
        Assert.Equal(new[] { "Emotion", "Opinion" }, answers.Select(x => x.Kind));
        Assert.Equal(new[] { "Thỏ đang sợ", "Con thích bác Gấu" }, answers.Select(x => x.AnswerText));
        Assert.Equal(new int?[] { 2, null }, answers.Select(x => x.SceneIndex));
        Assert.Equal(start, answers[0].AnsweredAt);
    }

    [Fact]
    public async Task ListAsync_HidesPredictionPictureAndOtherTypes_AndKeepsSessionsApart()
    {
        var mine = Done(DateTime.UtcNow);
        var other = Done(DateTime.UtcNow.AddDays(-1));
        Interaction(1, CheckpointKind.Prediction);
        Interaction(2, null, ListeningInteractionType.PicturePrediction);
        Interaction(3, CheckpointKind.Emotion);
        Choice(10, 1, "Bác Gấu");
        Choice(20, 2, "Mẹ Thỏ");
        Choice(30, 3, "Vui");
        Answered(mine.Id, 1, 10, DateTime.UtcNow);
        Answered(mine.Id, 2, 20, DateTime.UtcNow);
        Answered(other.Id, 3, 30, DateTime.UtcNow);

        var page = await List();

        Assert.Empty(page.Items.Single(x => x.SessionId == mine.Id).ConversationAnswers);
        Assert.Equal("Vui", page.Items.Single(x => x.SessionId == other.Id).ConversationAnswers.Single().AnswerText);
    }

    [Fact]
    public async Task ListAsync_WithoutAnswers_ReturnsEmptyList()
    {
        Done(DateTime.UtcNow);

        Assert.Empty((await List()).Items.Single().ConversationAnswers);
    }

    [Fact]
    public async Task ListAsync_Row_HasStatusStartTimePagesAndTotalPages()
    {
        _h.AddScene(0, "a"); _h.AddScene(1, "b"); _h.AddScene(2, "c");
        var session = Done(DateTime.UtcNow);
        session.TimeSpentSeconds = 125;
        session.PagesCompleted = 3;

        var item = (await List()).Items.Single();

        Assert.Equal("Completed", item.Status);
        Assert.Equal(session.StartedAt, item.StartedAt);
        Assert.Equal(125, item.TimeSpentSeconds);
        Assert.Equal(3, item.PagesCompleted);
        Assert.Equal(3, item.TotalPages);
    }

    [Fact]
    public async Task ListAsync_DateRange_FiltersCompletedAt_FromInclusiveToExclusive()
    {
        var day = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        Done(day.AddSeconds(-1));
        var first = Done(day);
        var last = Done(day.AddDays(1).AddSeconds(-1));
        Done(day.AddDays(1));

        var page = await List(new ParentReadingFeedbackQueryDto { From = day, To = day.AddDays(1) });

        Assert.Equal(new[] { last.Id, first.Id }, page.Items.Select(i => i.SessionId));
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_FromNotBeforeTo_IsBadRequest()
    {
        var at = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            List(new ParentReadingFeedbackQueryDto { From = at, To = at }));

        Assert.Equal(ParentReadingFeedbackFilter.InvalidRangeMessage, error.Message);
    }

    [Fact]
    public async Task ListAsync_UnknownStatus_IsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            List(new ParentReadingFeedbackQueryDto { Status = "Deleted" }));

        Assert.Equal(ParentReadingFeedbackFilter.InvalidStatusMessage, error.Message);
    }

    [Fact]
    public async Task ListAsync_Unfinished_ShowsEveryNotCompletedSession_NewestStartFirst()
    {
        var start = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        var started = _h.AddSession(SessionStatus.Started); started.StartedAt = start;
        var reading = _h.AddSession(SessionStatus.Reading); reading.StartedAt = start.AddMinutes(1);
        var activity = _h.AddSession(SessionStatus.Activity); activity.StartedAt = start.AddMinutes(2);
        var abandoned = _h.AddSession(SessionStatus.Abandoned); abandoned.StartedAt = start.AddMinutes(3);
        var forced = _h.AddSession(SessionStatus.ForceExited); forced.StartedAt = start.AddMinutes(4);
        Done(DateTime.UtcNow);
        _h.AddSession(SessionStatus.Reading, childId: ReadingHarness.OtherChild);

        var page = await List(new ParentReadingFeedbackQueryDto { Status = "unfinished" });

        Assert.Equal(new[] { forced.Id, abandoned.Id, activity.Id, reading.Id, started.Id },
            page.Items.Select(i => i.SessionId));
        Assert.Equal("ForceExited", page.Items[0].Status);
        Assert.Null(page.Items[0].CompletedAt);
    }

    [Fact]
    public async Task ListAsync_Unfinished_FiltersByStartedAt()
    {
        var day = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var inside = _h.AddSession(SessionStatus.Reading); inside.StartedAt = day.AddHours(5);
        var outside = _h.AddSession(SessionStatus.Reading); outside.StartedAt = day.AddDays(-1);

        var page = await List(new ParentReadingFeedbackQueryDto
            { Status = "Unfinished", From = day, To = day.AddDays(1) });

        Assert.Equal(new[] { inside.Id }, page.Items.Select(i => i.SessionId));
    }

    [Fact]
    public async Task ListAsync_ReadCountAndOrdinal_CountEveryCompletedReadOfTheStory()
    {
        Done(new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc));                       // lần 1, ngoài bộ lọc
        var second = Done(new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc));
        var third = Done(new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc));

        var page = await List(new ParentReadingFeedbackQueryDto
            { From = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc) });

        Assert.Equal(new[] { third.Id, second.Id }, page.Items.Select(i => i.SessionId));
        Assert.All(page.Items, item => Assert.Equal(3, item.ReadCount));
        Assert.Equal(new int?[] { 3, 2 }, page.Items.Select(i => i.ReadOrdinal));
        Assert.All(page.Items, item => Assert.False(item.LaterCompleted));
    }

    [Fact]
    public async Task ListAsync_Unfinished_FlagsSessionsFinishedLater_AndHasNoOrdinal()
    {
        var dropped = _h.AddSession(SessionStatus.Reading);
        dropped.StartedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var stillOpen = _h.AddSession(SessionStatus.Reading);
        stillOpen.StartedAt = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);
        Done(new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc));

        var items = (await List(new ParentReadingFeedbackQueryDto { Status = "Unfinished" })).Items;

        var droppedRow = items.Single(i => i.SessionId == dropped.Id);
        var openRow = items.Single(i => i.SessionId == stillOpen.Id);
        Assert.True(droppedRow.LaterCompleted);
        Assert.False(openRow.LaterCompleted);
        Assert.Null(droppedRow.ReadOrdinal);
        Assert.Equal(1, droppedRow.ReadCount);
    }

    private void Answer(int sessionId, int itemId, string answer, bool correct) =>
        _h.Attempts.Add(new QuizAttempt
        {
            Id = _h.Attempts.Count + 1, ReadingSessionId = sessionId, QuizItemId = itemId,
            AnswerGiven = answer, IsCorrect = correct, AnsweredAt = DateTime.UtcNow
        });

    [Fact]
    public async Task ListAsync_QuizRunCountAndLatestPercent_FollowEveryRunOfTheStory()
    {
        Quiz(1); Quiz(2);
        var first = Done(DateTime.UtcNow.AddDays(-2));
        var second = Done(DateTime.UtcNow.AddDays(-1));
        Attempt(first.Id, 1, true); Attempt(first.Id, 2, true);     // lần 1: 100%
        Attempt(second.Id, 1, true); Attempt(second.Id, 2, false);  // lần 2: 50%

        var item = (await List()).Items.First();

        Assert.Equal(2, item.QuizRunCount);
        Assert.Equal(50m, item.QuizLatestPercent);
        Assert.Equal(100m, item.QuizBestPercent);
    }

    [Fact]
    public async Task ListAsync_QuizNotTaken_HasZeroRunsAndNullLatest()
    {
        Quiz(1);
        Done(DateTime.UtcNow);

        var item = (await List()).Items.Single();

        Assert.Equal(0, item.QuizRunCount);
        Assert.Null(item.QuizLatestPercent);
    }

    [Fact]
    public async Task GetQuizAsync_ListsRunsAndEachQuestionWithTheChildsLatestAnswer()
    {
        var q1 = Quiz(1);
        Quiz(2);
        q1.Choices = "[\"a\",\"b\",\"c\"]";
        q1.Explanation = "Vì a đúng";
        var first = Done(DateTime.UtcNow.AddDays(-1));
        var second = Done(DateTime.UtcNow);
        Answer(first.Id, 1, "b", false); Answer(first.Id, 2, "a", true);   // lần 1: 50%
        Answer(second.Id, 1, "a", true);                                    // lần 2: 1/2 = 50%

        var detail = await _sut.GetQuizAsync(Parent, ReadingHarness.Child, second.Id);

        _guard.Verify(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(second.Id, detail.SessionId);
        Assert.Equal(ReadingHarness.Story, detail.StoryId);
        Assert.Equal("Truyện 10", detail.Title);
        Assert.Equal(2, detail.TotalQuestions);
        Assert.Equal(new[] { 1, 2 }, detail.Runs.Select(r => r.RunNo));
        Assert.Equal(new[] { 2, 1 }, detail.Runs.Select(r => r.Answered));
        Assert.Equal(new[] { 50m, 50m }, detail.Runs.Select(r => r.Percent));

        var question1 = detail.Questions.Single(q => q.QuizItemId == 1);
        Assert.Equal("MultipleChoice", question1.Type);
        Assert.Equal("Câu 1", question1.Question);
        Assert.Equal(new[] { "a", "b", "c" }, question1.Choices);
        Assert.Equal("a", question1.CorrectAnswer);
        Assert.Equal("Vì a đúng", question1.Explanation);
        Assert.Equal("a", question1.LatestAnswer);
        Assert.True(question1.LatestIsCorrect);
        Assert.Equal(2, question1.TimesAnswered);
        Assert.Equal(1, question1.TimesCorrect);

        var question2 = detail.Questions.Single(q => q.QuizItemId == 2);
        Assert.Equal(1, question2.TimesAnswered);
        Assert.Equal(1, question2.TimesCorrect);
    }

    [Fact]
    public async Task GetQuizAsync_StoryWithoutQuiz_ReturnsEmptyDetail()
    {
        var session = Done(DateTime.UtcNow);

        var detail = await _sut.GetQuizAsync(Parent, ReadingHarness.Child, session.Id);

        Assert.Equal(0, detail.TotalQuestions);
        Assert.Empty(detail.Runs);
        Assert.Empty(detail.Questions);
    }

    [Fact]
    public async Task GetQuizAsync_SessionOfAnotherChildOrMissing_IsNotFound()
    {
        var other = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetQuizAsync(Parent, ReadingHarness.Child, other.Id));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetQuizAsync(Parent, ReadingHarness.Child, 999));
    }

    [Fact]
    public async Task GetQuizAsync_WithoutPermission_IsForbidden()
    {
        var session = Done(DateTime.UtcNow);
        _guard.Setup(g => g.EnsureOwnerAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("không đủ quyền"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetQuizAsync(Parent, ReadingHarness.Child, session.Id));
    }

    private void Telemetry(int sessionId, TelemetryEventType type, string payload, DateTime at) =>
        _h.Telemetry.Add(new TelemetryLog
        {
            Id = _h.Telemetry.Count + 1, ReadingSessionId = sessionId, EventType = type,
            EventPayload = payload, OccurredAt = at
        });

    [Fact]
    public async Task ListAsync_ReadAloud_UsesLatestTryPerSegment_AndSkipsBrokenPayload()
    {
        var session = Done(DateTime.UtcNow);
        var other = Done(DateTime.UtcNow.AddDays(-1));
        var at = DateTime.UtcNow.AddMinutes(-10);
        Telemetry(session.Id, TelemetryEventType.AsrWordResult,
            "{\"segmentId\":1,\"total\":10,\"good\":4,\"retry\":5,\"skipped\":1}", at);
        Telemetry(session.Id, TelemetryEventType.AsrWordResult,
            "{\"segmentId\":1,\"total\":10,\"good\":9,\"retry\":1,\"skipped\":0}", at.AddMinutes(1));
        Telemetry(session.Id, TelemetryEventType.AsrWordResult,
            "{\"segmentId\":2,\"total\":10,\"good\":7,\"retry\":2,\"skipped\":1}", at.AddMinutes(2));
        Telemetry(session.Id, TelemetryEventType.AsrWordResult, "không phải json", at.AddMinutes(3));
        Telemetry(other.Id, TelemetryEventType.AsrWordResult,
            "{\"segmentId\":1,\"total\":10,\"good\":0,\"retry\":10,\"skipped\":0}", at);

        var page = await List();
        var readAloud = page.Items.Single(i => i.SessionId == session.Id).ReadAloud!;

        Assert.Equal(3, readAloud.Attempts);
        Assert.Equal(2, readAloud.SegmentsPracticed);
        Assert.Equal(16, readAloud.GoodWords);
        Assert.Equal(3, readAloud.RetryWords);
        Assert.Equal(20, readAloud.TotalWords);
        Assert.Equal(80m, readAloud.GoodPercent);
        Assert.Equal(0, page.Items.Single(i => i.SessionId == other.Id).ReadAloud!.GoodWords);
    }

    [Fact]
    public async Task ListAsync_NeverReadAloud_HasNullReadAloud()
    {
        Done(DateTime.UtcNow);

        Assert.Null((await List()).Items.Single().ReadAloud);
    }

    [Fact]
    public async Task ListAsync_SumsExpAndListsWordsCollectedInThatSession()
    {
        var session = Done(DateTime.UtcNow);
        var other = Done(DateTime.UtcNow.AddDays(-1));
        _h.AddVocabulary(31, "rực rỡ");
        _h.AddVocabulary(32, "lấp lánh");
        var at = DateTime.UtcNow;
        Telemetry(session.Id, TelemetryEventType.RewardGranted, "{\"exp\":20,\"reason\":\"StoryCompleted\"}", at);
        Telemetry(session.Id, TelemetryEventType.RewardGranted, "{\"exp\":5,\"reason\":\"QuizAnswer\"}", at.AddSeconds(1));
        Telemetry(session.Id, TelemetryEventType.VocabularyCollected, "{\"vocabularyId\":32}", at.AddSeconds(2));
        Telemetry(session.Id, TelemetryEventType.VocabularyCollected, "{\"vocabularyId\":31}", at.AddSeconds(3));
        Telemetry(session.Id, TelemetryEventType.VocabularyCollected, "{\"vocabularyId\":999}", at.AddSeconds(4));
        Telemetry(other.Id, TelemetryEventType.RewardGranted, "{\"exp\":99,\"reason\":\"StoryCompleted\"}", at);

        var page = await List();
        var row = page.Items.Single(i => i.SessionId == session.Id);

        Assert.Equal(25, row.ExpEarned);
        Assert.Equal(new[] { 32, 31 }, row.WordsCollected.Select(w => w.VocabularyId));
        Assert.Equal(new[] { "lấp lánh", "rực rỡ" }, row.WordsCollected.Select(w => w.Term));
        Assert.Equal(99, page.Items.Single(i => i.SessionId == other.Id).ExpEarned);
    }

    [Fact]
    public async Task ListAsync_NoRewardsOrWords_HasZeroExpAndEmptyWords()
    {
        Done(DateTime.UtcNow);

        var row = (await List()).Items.Single();

        Assert.Equal(0, row.ExpEarned);
        Assert.Empty(row.WordsCollected);
    }

    [Fact]
    public async Task ListAsync_ShowsTheReadingModeOfEachSession()
    {
        var listened = Done(DateTime.UtcNow);
        listened.ReadingMode = ReadingMode.ListenFirst;
        var unknown = Done(DateTime.UtcNow.AddDays(-1));

        var page = await List();

        Assert.Equal("ListenFirst", page.Items.Single(i => i.SessionId == listened.Id).ReadingMode);
        Assert.Null(page.Items.Single(i => i.SessionId == unknown.Id).ReadingMode);
    }

    [Fact]
    public async Task ListAsync_LinksTheRetellOfThatSession()
    {
        var session = Done(DateTime.UtcNow);
        var other = Done(DateTime.UtcNow.AddDays(-1));
        var createdAt = DateTime.UtcNow.AddMinutes(-1);
        _h.Summaries.Add(new StorySummary
        {
            Id = 1, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child,
            StoryId = ReadingHarness.Story, AudioStoragePath = "summaries/1.webm", CreatedAt = createdAt
        });
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = 1, StorySceneId = 5 });
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 2, StorySummaryId = 1, StorySceneId = 6, AnsweredAt = DateTime.UtcNow });
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 3, StorySummaryId = 1, StorySceneId = 7, IsDeleted = true });

        var page = await List();

        var retell = page.Items.Single(i => i.SessionId == session.Id).Retell!;
        Assert.Equal(1, retell.SummaryId);
        Assert.Equal(createdAt, retell.CreatedAt);
        Assert.True(retell.HasRecording);
        Assert.Equal(2, retell.FlagCount);
        Assert.Equal(1, retell.UnansweredCount);
        Assert.Null(page.Items.Single(i => i.SessionId == other.Id).Retell);
    }

    [Fact]
    public async Task ListAsync_DeletedRetellOrNoRecording_IsHandled()
    {
        var deleted = Done(DateTime.UtcNow);
        var flagsOnly = Done(DateTime.UtcNow.AddDays(-1));
        _h.Summaries.Add(new StorySummary
        {
            Id = 1, ReadingSessionId = deleted.Id, ChildProfileId = ReadingHarness.Child,
            StoryId = ReadingHarness.Story, IsDeleted = true
        });
        _h.Summaries.Add(new StorySummary
        {
            Id = 2, ReadingSessionId = flagsOnly.Id, ChildProfileId = ReadingHarness.Child,
            StoryId = ReadingHarness.Story, AudioStoragePath = null
        });

        var page = await List();

        Assert.Null(page.Items.Single(i => i.SessionId == deleted.Id).Retell);
        var retell = page.Items.Single(i => i.SessionId == flagsOnly.Id).Retell!;
        Assert.False(retell.HasRecording);
        Assert.Equal(0, retell.FlagCount);
        Assert.Equal(0, retell.UnansweredCount);
    }

    [Fact]
    public async Task GetOverviewAsync_SummarisesTheRange()
    {
        Quiz(1); Quiz(2);
        _h.AddVocabulary(31, "rực rỡ");
        var day = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var a = Done(day.AddHours(8), StoryFeeling.VeryLike); a.TimeSpentSeconds = 300;
        var b = Done(day.AddHours(9), StoryFeeling.Like); b.TimeSpentSeconds = 200;
        var c = Done(day.AddHours(10)); c.TimeSpentSeconds = 100;
        Done(day.AddDays(-3), StoryFeeling.Dislike);                       // ngoài khoảng
        var open = _h.AddSession(SessionStatus.Reading);
        open.StartedAt = day.AddHours(11); open.TimeSpentSeconds = 50;
        Attempt(a.Id, 1, true); Attempt(a.Id, 2, false);                   // 50%
        Attempt(b.Id, 1, true); Attempt(b.Id, 2, true);                    // 100% → tốt nhất
        Telemetry(a.Id, TelemetryEventType.RewardGranted, "{\"exp\":20,\"reason\":\"StoryCompleted\"}", day.AddHours(8));
        Telemetry(b.Id, TelemetryEventType.VocabularyCollected, "{\"vocabularyId\":31}", day.AddHours(9));
        Telemetry(c.Id, TelemetryEventType.AsrWordResult,
            "{\"segmentId\":1,\"total\":10,\"good\":8,\"retry\":2,\"skipped\":0}", day.AddHours(10));
        _h.Summaries.Add(new StorySummary
        {
            Id = 1, ReadingSessionId = a.Id, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story
        });

        var overview = await _sut.GetOverviewAsync(Parent, ReadingHarness.Child,
            new ParentReadingOverviewQueryDto { From = day, To = day.AddDays(1) });

        _guard.Verify(g => g.EnsureOwnerAsync(ReadingHarness.Child, Parent, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(day, overview.From);
        Assert.Equal(3, overview.CompletedSessions);
        Assert.Equal(1, overview.DistinctStoriesCompleted);
        Assert.Equal(1, overview.UnfinishedStories);
        Assert.Equal(650, overview.TotalTimeSpentSeconds);
        Assert.Equal(1, overview.QuizStoriesTaken);
        Assert.Equal(100m, overview.AverageQuizBestPercent);
        Assert.Equal(1, overview.FeelingCounts["VeryLike"]);
        Assert.Equal(1, overview.FeelingCounts["Like"]);
        Assert.Equal(0, overview.FeelingCounts["Neutral"]);
        Assert.Equal(0, overview.FeelingCounts["Dislike"]);
        Assert.Equal(1, overview.FeelingCounts["None"]);
        Assert.Equal(20, overview.ExpEarned);
        Assert.Equal(1, overview.WordsCollected);
        Assert.Equal(80m, overview.ReadAloudGoodPercent);
        Assert.Equal(1, overview.RetellCount);
    }

    [Fact]
    public async Task GetOverviewAsync_Empty_ReturnsZerosAndNulls()
    {
        var overview = await _sut.GetOverviewAsync(Parent, ReadingHarness.Child, new ParentReadingOverviewQueryDto());

        Assert.Equal(0, overview.CompletedSessions);
        Assert.Equal(0, overview.UnfinishedStories);
        Assert.Equal(0, overview.TotalTimeSpentSeconds);
        Assert.Null(overview.AverageQuizBestPercent);
        Assert.Null(overview.ReadAloudGoodPercent);
        Assert.Equal(5, overview.FeelingCounts.Count);
        Assert.All(overview.FeelingCounts.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task GetOverviewAsync_UnfinishedStoryFinishedLater_IsNotCounted()
    {
        var dropped = _h.AddSession(SessionStatus.Reading);
        dropped.StartedAt = DateTime.UtcNow.AddDays(-2);
        Done(DateTime.UtcNow.AddDays(-1));

        var overview = await _sut.GetOverviewAsync(Parent, ReadingHarness.Child, new ParentReadingOverviewQueryDto());

        Assert.Equal(0, overview.UnfinishedStories);
    }

    [Fact]
    public async Task GetOverviewAsync_FromNotBeforeTo_IsBadRequest()
    {
        var at = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<BadRequestException>(() => _sut.GetOverviewAsync(
            Parent, ReadingHarness.Child, new ParentReadingOverviewQueryDto { From = at, To = at.AddDays(-1) }));

        Assert.Equal(ParentReadingFeedbackFilter.InvalidRangeMessage, error.Message);
    }

    [Fact]
    public async Task NewEndpoints_RejectUnauthorizedParentBeforeAnyRepositoryAccess()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Kh?ng ?? quy?n."));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetQuizAsync(Parent, ReadingHarness.Child, 999));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetOverviewAsync(Parent, ReadingHarness.Child,
            new ParentReadingOverviewQueryDto { From = DateTime.UtcNow, To = DateTime.MinValue }));
        await Assert.ThrowsAsync<ForbiddenException>(() => List(new ParentReadingFeedbackQueryDto { Status = "invalid" }));

        _h.UnitOfWork.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"segmentId\":1,\"total\":10,\"good\":8}")]
    [InlineData("{\"segmentId\":1,\"total\":\"ten\",\"good\":8,\"retry\":2}")]
    [InlineData("{\"segmentId\":1,\"total\":2147483648,\"good\":8,\"retry\":2}")]
    public async Task ListAsync_MalformedTelemetry_IsIgnored(string payload)
    {
        var session = Done(DateTime.UtcNow);
        foreach (var type in new[] { TelemetryEventType.AsrWordResult, TelemetryEventType.RewardGranted,
                     TelemetryEventType.VocabularyCollected })
            Telemetry(session.Id, type, payload, DateTime.UtcNow);

        var row = (await List()).Items.Single();

        Assert.Null(row.ReadAloud);
        Assert.Equal(0, row.ExpEarned);
        Assert.Empty(row.WordsCollected);
    }

    [Fact]
    public async Task GetOverviewAsync_IsolatesChildrenAndUsesAllTimeQuizHistory()
    {
        Quiz(1);
        var day = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var older = Done(day.AddDays(-1));
        Attempt(older.Id, 1, true);
        var included = Done(day);
        included.TimeSpentSeconds = 10;
        Done(day.AddDays(1));
        var other = Done(day.AddHours(1), childId: ReadingHarness.OtherChild);
        other.TimeSpentSeconds = 999;
        Telemetry(other.Id, TelemetryEventType.RewardGranted, "{\"exp\":999}", day);
        var deleted = Done(day.AddHours(2));
        deleted.IsDeleted = true;
        var open = _h.AddSession(SessionStatus.Reading);
        open.StartedAt = day.AddDays(1);

        var result = await _sut.GetOverviewAsync(Parent, ReadingHarness.Child,
            new ParentReadingOverviewQueryDto { From = day, To = day.AddDays(1) });

        Assert.Equal(1, result.CompletedSessions);
        Assert.Equal(10, result.TotalTimeSpentSeconds);
        Assert.Equal(1, result.QuizStoriesTaken);
        Assert.Equal(100m, result.AverageQuizBestPercent);
        Assert.Equal(0, result.UnfinishedStories);
        Assert.Equal(0, result.ExpEarned);
    }

    [Fact]
    public async Task GetQuizAsync_IgnoresDeletedSessionsAndOtherChildsAnswers()
    {
        Quiz(1);
        var mine = Done(DateTime.UtcNow);
        var other = Done(DateTime.UtcNow, childId: ReadingHarness.OtherChild);
        Answer(other.Id, 1, "a", true);
        var deleted = Done(DateTime.UtcNow);
        deleted.IsDeleted = true;
        Answer(deleted.Id, 1, "a", true);

        var detail = await _sut.GetQuizAsync(Parent, ReadingHarness.Child, mine.Id);

        Assert.Empty(detail.Runs);
        Assert.Null(detail.Questions.Single().LatestAnswer);
        Assert.Equal(0, detail.Questions.Single().TimesAnswered);
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetQuizAsync(Parent, ReadingHarness.Child, deleted.Id));
    }
    [Fact]
    public async Task ListAsync_CountsLiveReadAloudRecordingsPerSession()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        session.CompletedAt = DateTime.UtcNow;
        _h.ReadAloudRecordings.Add(new ReadAloudRecording { Id = 1, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child, StorySegmentId = 1, AudioStoragePath = "a" });
        _h.ReadAloudRecordings.Add(new ReadAloudRecording { Id = 2, ReadingSessionId = session.Id, ChildProfileId = ReadingHarness.Child, StorySegmentId = 2, AudioStoragePath = "b", IsDeleted = true });

        var page = await _sut.ListAsync(7, ReadingHarness.Child, new ParentReadingFeedbackQueryDto());

        Assert.Equal(1, Assert.Single(page.Items).ReadAloudRecordingCount);
    }
}
