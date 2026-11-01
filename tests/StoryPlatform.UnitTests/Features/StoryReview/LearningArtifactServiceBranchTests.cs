using Moq;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using Store = StoryPlatform.UnitTests.OutlineServiceTests.FakeUnitOfWork;
using AiQuizItem = StoryPlatform.Contracts.AI.Models.QuizItemDto;

namespace StoryPlatform.UnitTests.Features.StoryReview;

/// <summary>Guard and branch coverage for LearningArtifactService (Report 5, Việc 3.2).</summary>
public sealed class LearningArtifactServiceBranchTests
{
    private readonly Mock<IAIStoryGenerationClient> _ai = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();

    public LearningArtifactServiceBranchTests()
    {
        ReturnVocabulary(new GeneratedVocabularyItemDto("chăm chỉ", "cố gắng liên tục"));
        ReturnQuiz(new AiQuizItem { Type = "multiple_choice", Question = "Mèo thích gì?", Options = ["Học", "Chơi"], CorrectAnswer = "Học" });
    }

    // ---- GenerateArtifactsAsync ----

    [Fact]
    public async Task GenerateArtifactsAsync_ReadingMediaOnlyStory_ThrowsConflict()
    {
        var store = Seed();
        store.Items<Story>().Single().OutputMode = StoryOutputMode.ReadingMediaOnly;

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(store).GenerateArtifactsAsync(1, 1));

        Assert.Equal("LEARNING_ARTIFACT_NOT_ENABLED", error.Message);
        _ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateArtifactsAsync_NullRequest_GeneratesVocabularyAndQuizForCurrentVersion()
    {
        var store = Seed();

        var result = await Service(store).GenerateArtifactsAsync(1, 1);

        Assert.True(result.Success);
        Assert.Equal(2, result.StoryVersionId);
        Assert.Equal((1, 1), (result.VocabularyCount, result.QuizCount));
    }

    [Fact]
    public async Task GenerateArtifactsAsync_AllFlagsFalse_CallsNoAiEndpoint()
    {
        var store = Seed();

        var result = await Service(store).GenerateArtifactsAsync(1, 1, new GenerateArtifactsRequestDto
        {
            IncludeVocabulary = false, IncludeQuiz = false
        });

        Assert.True(result.Success);
        Assert.Equal((0, 0), (result.VocabularyCount, result.QuizCount));
        _ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateArtifactsAsync_VersionOfAnotherStory_ThrowsBadRequest()
    {
        var store = Seed();
        store.Seed(new StoryVersion { Id = 9, StoryId = 5, VersionNo = 1, IsCurrent = true, Content = "Khác" });

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            Service(store).GenerateArtifactsAsync(1, 1, new GenerateArtifactsRequestDto { StoryVersionId = 9 }));

        Assert.Equal("StoryVersion không thuộc Story.", error.Message);
    }

    [Fact]
    public async Task GenerateArtifactsAsync_UnknownVersionId_ThrowsBadRequest()
    {
        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            Service(Seed()).GenerateArtifactsAsync(1, 1, new GenerateArtifactsRequestDto { StoryVersionId = 404 }));

        Assert.Equal("StoryVersion không thuộc Story.", error.Message);
    }

    [Fact]
    public async Task GenerateArtifactsAsync_NoCurrentVersion_ThrowsNotFound()
    {
        var store = Seed();
        foreach (var version in store.Items<StoryVersion>()) version.IsCurrent = false;

        var error = await Assert.ThrowsAsync<NotFoundException>(() => Service(store).GenerateArtifactsAsync(1, 1));

        Assert.Equal("StoryVersion hiện tại không tồn tại.", error.Message);
    }

    [Fact]
    public async Task GenerateArtifactsAsync_UserNotChildOwner_PropagatesForbidden()
    {
        _guard.Setup(item => item.EnsureOwnerAsync(1, 1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException());

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(Seed()).GenerateArtifactsAsync(1, 1));

        Assert.Equal("Bạn không có quyền truy cập tài nguyên này.", error.Message);
        _ai.VerifyNoOtherCalls();
    }

    // ---- Vocabulary ----

    [Fact]
    public async Task GenerateVocabularyAsync_ExistingRows_ReplacesThemWithTrimmedDistinctTerms()
    {
        var store = Seed();
        store.Seed(new StoryVocabulary { Id = 1, StoryVersionId = 2, Term = "cũ", Definition = "cũ" });
        ReturnVocabulary(
            new GeneratedVocabularyItemDto("  mèo con ", " con mèo nhỏ "),
            new GeneratedVocabularyItemDto("MÈO CON", "trùng lặp"),
            new GeneratedVocabularyItemDto(" ", "thiếu từ"),
            new GeneratedVocabularyItemDto("trống", ""));

        var count = await Service(store).GenerateVocabularyAsync(1, 1);

        Assert.Equal(1, count);
        var row = Assert.Single(store.Items<StoryVocabulary>());
        Assert.Equal(("mèo con", "con mèo nhỏ"), (row.Term, row.Definition));
    }

    [Fact]
    public async Task GenerateVocabularyAsync_AiReturnsOnlyBlankItems_ThrowsInvalidOperation()
    {
        ReturnVocabulary(new GeneratedVocabularyItemDto("", "định nghĩa"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Seed()).GenerateVocabularyAsync(1, 1));

        Assert.Equal("Không thể trích xuất từ vựng phù hợp từ câu chuyện.", error.Message);
    }

    [Fact]
    public async Task GenerateVocabularyAsync_CommitFails_RollsBackAndRethrows()
    {
        var store = Seed();
        store.FailCommitNumber = 1;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store).GenerateVocabularyAsync(1, 1));

        Assert.Equal("simulated commit failure", error.Message);
    }

    [Fact]
    public async Task GenerateVocabularyAsync_ChildWithoutLearningProfile_UsesStoryReadingLevelAndChildAgeBand()
    {
        var store = Seed(withLearningProfile: false);
        store.Items<Story>().Single().ReadingLevel = 3;
        GenerateVocabularyRequest? sent = null;
        _ai.Setup(item => item.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GenerateVocabularyRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new GenerateVocabularyResponse { Items = [new GeneratedVocabularyItemDto("a", "b")] });

        await Service(store).GenerateVocabularyAsync(1, 1);

        Assert.Equal("3", sent!.ReadingLevel);
        Assert.Equal(AgeBand.Age_9_10.ToString(), sent.AgeBand);
    }

    [Fact]
    public async Task GenerateVocabularyAsync_MissingChildProfileRow_FallsBackToDefaultAgeBand()
    {
        var store = Seed(withChild: false, withLearningProfile: false);
        GenerateVocabularyRequest? sent = null;
        _ai.Setup(item => item.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GenerateVocabularyRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new GenerateVocabularyResponse { Items = [new GeneratedVocabularyItemDto("a", "b")] });

        await Service(store).GenerateVocabularyAsync(1, 1);

        Assert.Equal("Age_6_8", sent!.AgeBand);
        Assert.Equal("2", sent.ReadingLevel);
    }

    [Fact]
    public async Task GenerateVocabularyAsync_CreatorStory_UsesStoryAgeBandAndSkipsOwnerGuard()
    {
        var store = SeedCreator();
        GenerateVocabularyRequest? sent = null;
        _ai.Setup(item => item.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GenerateVocabularyRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new GenerateVocabularyResponse { Items = [new GeneratedVocabularyItemDto("a", "b")] });

        Assert.Equal(1, await Service(store).GenerateVocabularyAsync(7, 1));

        Assert.Equal("9-10", sent!.AgeBand);
        _guard.VerifyNoOtherCalls();
    }

    // ---- Quiz ----

    [Fact]
    public async Task GenerateQuizAsync_TypeAliasesAndUnknownType_MapToQuizTypes()
    {
        var store = Seed();
        store.Seed(new QuizItem { Id = 1, StoryVersionId = 2, Question = "cũ", CorrectAnswer = "cũ" });
        ReturnQuiz(
            new AiQuizItem { Type = "MultipleChoice", Question = " Q1 ", Options = ["A", "B"], CorrectAnswer = " A " },
            // JSON payloads may carry null for these non-nullable members.
            new AiQuizItem { Type = "TrueFalse", Question = "Q2", Options = null!, CorrectAnswer = "true" },
            new AiQuizItem { Type = "ShortAnswer", Question = "Q3", CorrectAnswer = "x" },
            new AiQuizItem { Type = "matching", Question = "Q4", CorrectAnswer = "y" },
            new AiQuizItem { Type = null!, Question = "Q5", CorrectAnswer = "z" });

        Assert.Equal(5, await Service(store).GenerateQuizAsync(1, 1));

        var rows = store.Items<QuizItem>().OrderBy(item => item.Question).ToArray();
        Assert.Equal(
            [QuizType.MultipleChoice, QuizType.TrueFalse, QuizType.ShortAnswer, QuizType.MultipleChoice, QuizType.MultipleChoice],
            rows.Select(item => item.Type).ToArray());
        Assert.Equal(("Q1", "A", "[\"A\",\"B\"]"), (rows[0].Question, rows[0].CorrectAnswer, rows[0].Choices));
        Assert.Null(rows[1].Choices);
    }

    [Fact]
    public async Task GenerateQuizAsync_AiReturnsNoItems_ThrowsInvalidOperation()
    {
        ReturnQuiz();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Seed()).GenerateQuizAsync(1, 1));

        Assert.Equal("Không thể tạo câu hỏi trắc nghiệm từ câu chuyện.", error.Message);
    }

    [Fact]
    public async Task GenerateQuizAsync_CommitFails_RollsBackAndRethrows()
    {
        var store = Seed();
        store.FailCommitNumber = 1;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(store).GenerateQuizAsync(1, 1));

        Assert.Equal("simulated commit failure", error.Message);
    }

    [Fact]
    public async Task GenerateQuizAsync_ChildPolicies_AreSentWithStoredVocabulary()
    {
        var store = Seed();
        store.Seed(new StoryVocabulary { Id = 1, StoryVersionId = 2, Term = "mèo", Definition = "con vật" });
        GenerateQuizRequest? sent = null;
        _ai.Setup(item => item.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GenerateQuizRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new GenerateQuizResponse { Items = [new AiQuizItem { Question = "Q", CorrectAnswer = "A" }] });

        await Service(store).GenerateQuizAsync(1, 1);

        Assert.Equal("Nhận biết nhân vật", sent!.ComprehensionGoal);
        Assert.Equal(75m, sent.ComprehensionThresholdPercent);
        Assert.Equal("mèo", Assert.Single(sent.Vocabulary).Term);
    }

    [Fact]
    public async Task GenerateQuizAsync_CreatorStory_SendsNoChildPolicies()
    {
        var store = SeedCreator();
        GenerateQuizRequest? sent = null;
        _ai.Setup(item => item.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GenerateQuizRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(new GenerateQuizResponse { Items = [new AiQuizItem { Question = "Q", CorrectAnswer = "A" }] });

        await Service(store).GenerateQuizAsync(7, 1);

        Assert.Equal("9-10", sent!.AgeBand);
        Assert.Null(sent.ComprehensionGoal);
        Assert.Null(sent.ComprehensionThresholdPercent);
    }

    // ---- helpers ----

    private LearningArtifactService Service(Store store) => new(store, _ai.Object, _guard.Object);

    private void ReturnVocabulary(params GeneratedVocabularyItemDto[] items) =>
        _ai.Setup(item => item.GenerateVocabularyAsync(It.IsAny<GenerateVocabularyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateVocabularyResponse { Items = items });

    private void ReturnQuiz(params AiQuizItem[] items) =>
        _ai.Setup(item => item.GenerateQuizAsync(It.IsAny<GenerateQuizRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateQuizResponse { Items = items });

    private static Store Seed(bool withChild = true, bool withLearningProfile = true)
    {
        var store = new Store();
        store.Seed(new Story { Id = 1, ChildProfileId = 1, AuthorUserId = 1, Status = StoryStatus.ContentReview, Language = "vi" });
        store.Seed(new StoryVersion { Id = 1, StoryId = 1, VersionNo = 1, IsCurrent = false, Title = "Cũ", Content = "Bản cũ." });
        store.Seed(new StoryVersion
        {
            Id = 2, StoryId = 1, VersionNo = 2, IsCurrent = true, Title = "Chú mèo đi học",
            Content = "Ngày xửa ngày xưa có một chú mèo con rất thích đi học.", Lesson = "Chăm chỉ"
        });
        if (withChild)
            store.Seed(new ChildProfile { Id = 1, OwnerUserId = 1, Nickname = "Bi", AgeBand = AgeBand.Age_9_10, Language = "vi" });
        if (withLearningProfile)
            store.Seed(new LearningProfile { Id = 1, ChildProfileId = 1, ReadingLevel = 2, ComprehensionGoal = "Nhận biết nhân vật" });
        store.Seed(new SafetyPolicy { Id = 1, ChildProfileId = 1, ComprehensionThresholdPercent = 75m });
        return store;
    }

    private static Store SeedCreator()
    {
        var store = new Store();
        store.Seed(new UserAccount { Id = 7, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
        store.Seed(new Story
        {
            Id = 1, AuthorUserId = 7, Scope = StoryScope.Creator, Source = StorySource.Ai, AgeBand = "9-10",
            Status = StoryStatus.ContentReview, Language = "vi"
        });
        store.Seed(new StoryVersion { Id = 2, StoryId = 1, VersionNo = 1, IsCurrent = true, Title = "T", Content = "Nội dung." });
        return store;
    }
}
