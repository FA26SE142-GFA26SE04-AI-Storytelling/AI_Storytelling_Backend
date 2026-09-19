using System.Linq.Expressions;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryReview.DTOs;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

// Type aliases to avoid ambiguity with AI.Contracts models
using AppVocabularyItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.VocabularyItemDto;
using AppQuizItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.QuizItemDto;
using AppDiscussionItemDto = StoryPlatform.Application.Features.StoryReview.DTOs.DiscussionItemDto;

namespace StoryPlatform.UnitTests;

public sealed class StoryReviewServiceTests
{
    #region Get Review Package Tests

    [Fact]
    public async Task GetReviewPackage_ReturnsCorrectPackage()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.GetReviewPackageAsync(1, 1);

        Assert.Equal(1, result.StoryId);
        Assert.Equal(StoryStatus.ContentReview.ToString(), result.StoryStatus);
        Assert.True(result.CanEdit);
        Assert.True(result.CanApprove);
    }

    [Fact]
    public async Task GetReviewPackage_ThrowsNotFound_WhenStoryNotExists()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetReviewPackageAsync(1, 999));
    }

    #endregion

    #region Story Review Tests

    [Fact]
    public async Task UpdateStory_Success()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = 1,
            Title = "Tiêu đề mới",
            Content = "Nội dung mới",
            Lesson = "Bài học mới"
        });

        Assert.Equal("Tiêu đề mới", result.Title);
        Assert.Equal("Nội dung mới", result.Content);
        Assert.Equal("Bài học mới", result.Lesson);
    }

    [Fact]
    public async Task UpdateStory_SetsEditorAndEditType()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = 1,
            Title = "New Title",
            Content = "New Content",
            Lesson = "New Lesson"
        });

        // Sau khi update: phải có version mới (id khác 1) với EditorUserId=1 và EditType=HumanEdited.
        var newVersion = unitOfWork.Items<StoryVersion>().Single(version => version.Id == result.VersionId);
        Assert.Equal(1, newVersion.EditorUserId);
        Assert.Equal(VersionEditType.HumanEdited, newVersion.EditType);
        Assert.True(newVersion.IsCurrent);
        Assert.False(unitOfWork.Items<StoryVersion>().Single(version => version.Id == 1).IsCurrent);
    }

    [Fact]
    public async Task UpdateStory_KeepsEditedVersionAsCurrent()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = 1,
            Title = "Current title",
            Content = "Current content",
            Lesson = "Current lesson"
        });

        var current = unitOfWork.Items<StoryVersion>().Single(version => version.Id == result.VersionId);
        Assert.True(current.IsCurrent);
        Assert.Equal(2, current.VersionNo); // Version mới phải là v2 (v1 cũ bị IsCurrent=false)
        Assert.Single(unitOfWork.Items<StoryVersion>(), version => version.IsCurrent);
    }

    [Fact]
    public async Task UpdateStory_RejectsNonCurrentVersion()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Seed(new StoryVersion
        {
            Id = 2,
            StoryId = 1,
            VersionNo = 2,
            Title = "Old title",
            Content = "Old content",
            Lesson = "Old lesson",
            IsCurrent = false
        });
        var service = Service(unitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = 2,
            Title = "Should not change",
            Content = "Should not change",
            Lesson = "Should not change"
        }));

        Assert.Equal("Old content", unitOfWork.Items<StoryVersion>().Single(version => version.Id == 2).Content);
    }

    [Fact]
    public async Task UpdateStory_ThrowsNotFound_WhenVersionNotExists()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = 999,
            Title = "Test",
            Content = "Test",
            Lesson = "Test"
        }));
    }

    [Fact]
    public async Task UpdateStory_ThrowsBadRequest_WhenRequiredContentIsBlank()
    {
        var service = Service(SeedContentReview());

        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = 1,
            Title = " ",
            Content = "Valid content",
            Lesson = "Valid lesson"
        }));
    }

    #endregion

    #region Vocabulary Review Tests

    [Fact]
    public async Task GetVocabulary_ReturnsItems()
    {
        var unitOfWork = SeedContentReviewWithVocabulary();
        var service = Service(unitOfWork);

        var result = await service.GetVocabularyForReviewAsync(1, 1);

        Assert.Equal(3, result.Items.Count);
        Assert.Contains(result.Items, i => i.Term == "rừng");
        Assert.Contains(result.Items, i => i.Term == "phiêu lưu");
    }

    [Fact]
    public async Task UpdateVocabulary_AddNewItem()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.UpdateVocabularyAsync(1, 1, new UpdateVocabularyRequestDto
        {
            VersionId = 1,
            Items = new List<AppVocabularyItemDto>
            {
                new() { Term = "mới", Definition = "Từ mới" }
            }
        });

        Assert.Single(result.Items);
        Assert.Equal("mới", result.Items[0].Term);
    }

    [Fact]
    public async Task UpdateVocabulary_UpdateExistingItem()
    {
        var unitOfWork = SeedContentReviewWithVocabulary();
        var service = Service(unitOfWork);

        var result = await service.UpdateVocabularyAsync(1, 1, new UpdateVocabularyRequestDto
        {
            VersionId = 1,
            Items = new List<AppVocabularyItemDto>
            {
                new() { Id = 1, Term = "rừng", Definition = "Nơi có nhiều cây xanh" }
            }
        });

        var updated = result.Items.First(i => i.Id == 1);
        Assert.Equal("Nơi có nhiều cây xanh", updated.Definition);
    }

    [Fact]
    public async Task UpdateVocabulary_DeleteRemovedItem()
    {
        var unitOfWork = SeedContentReviewWithVocabulary();
        var service = Service(unitOfWork);

        var result = await service.UpdateVocabularyAsync(1, 1, new UpdateVocabularyRequestDto
        {
            VersionId = 1,
            Items = new List<AppVocabularyItemDto>() // Empty - delete all
        });

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task UpdateVocabulary_ThrowsBadRequest_OnDuplicateTerms()
    {
        var service = Service(SeedContentReview());

        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateVocabularyAsync(1, 1, new UpdateVocabularyRequestDto
        {
            VersionId = 1,
            Items =
            [
                new() { Term = "rừng", Definition = "Nơi có nhiều cây" },
                new() { Term = "RỪNG", Definition = "Khu vực nhiều cây" }
            ]
        }));
    }

    #endregion

    #region Quiz Review Tests

    [Fact]
    public async Task GetQuiz_ReturnsItems()
    {
        var unitOfWork = SeedContentReviewWithQuiz();
        var service = Service(unitOfWork);

        var result = await service.GetQuizForReviewAsync(1, 1);

        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task UpdateQuiz_AddNewQuestion()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.UpdateQuizAsync(1, 1, new UpdateQuizRequestDto
        {
            VersionId = 1,
            Items = new List<AppQuizItemDto>
            {
                new()
                {
                    Type = "MultipleChoice",
                    Question = "Con gì?",
                    CorrectAnswer = "Mèo",
                    Choices = new List<string> { "Mèo", "Chó", "Heo" }
                }
            }
        });

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task UpdateQuiz_ThrowsOnInvalidType()
    {
        var service = Service(SeedContentReview());

        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateQuizAsync(1, 1, new UpdateQuizRequestDto
        {
            VersionId = 1,
            Items = new List<AppQuizItemDto>
            {
                new() { Type = "Unsupported", Question = "Test?", CorrectAnswer = "A" }
            }
        }));
    }

    [Fact]
    public async Task UpdateQuiz_ThrowsBadRequest_WhenCorrectAnswerIsNotAChoice()
    {
        var service = Service(SeedContentReview());

        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateQuizAsync(1, 1, new UpdateQuizRequestDto
        {
            VersionId = 1,
            Items =
            [
                new()
                {
                    Type = "MultipleChoice",
                    Question = "Con gì?",
                    CorrectAnswer = "Cá",
                    Choices = ["Mèo", "Chó"]
                }
            ]
        }));
    }

    #endregion

    #region Discussion Review Tests

    [Fact]
    public async Task GetDiscussion_ReturnsItems()
    {
        var unitOfWork = SeedContentReviewWithDiscussion();
        var service = Service(unitOfWork);

        var result = await service.GetDiscussionForReviewAsync(1, 1);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task UpdateDiscussion_AddNewQuestion()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.UpdateDiscussionAsync(1, 1, new UpdateDiscussionRequestDto
        {
            VersionId = 1,
            Items = new List<AppDiscussionItemDto>
            {
                new() { Question = "Bạn nghĩ sao?", IsMoralLesson = true },
                new() { Question = "Bạn sẽ làm gì?", IsMoralLesson = false }
            }
        });

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task UpdateDiscussion_ThrowsBadRequest_WhenFewerThanTwoQuestions()
    {
        var service = Service(SeedContentReview());

        await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateDiscussionAsync(1, 1, new UpdateDiscussionRequestDto
        {
            VersionId = 1,
            Items = [new() { Question = "Chỉ một câu hỏi", IsMoralLesson = true }]
        }));
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task Validate_ReturnsCanApprove_WhenAllValid()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        var service = Service(unitOfWork);

        var result = await service.ValidateAsync(1, 1);

        Assert.True(result.CanApprove);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task Validate_ReturnsCanApproveFalse_WhenVocabularyInsufficient()
    {
        var unitOfWork = SeedContentReviewWithQuizAndDiscussion();
        var service = Service(unitOfWork);

        var result = await service.ValidateAsync(1, 1);

        Assert.False(result.CanApprove);
        Assert.Contains(result.Issues, i => i.Contains("Vocabulary"));
    }

    [Fact]
    public async Task Validate_ReturnsCanApproveFalse_WhenQuizInsufficient()
    {
        var unitOfWork = SeedContentReviewWithVocabulary();
        var service = Service(unitOfWork);

        var result = await service.ValidateAsync(1, 1);

        Assert.False(result.CanApprove);
        Assert.Contains(result.Issues, i => i.Contains("Quiz"));
    }

    [Fact]
    public async Task Validate_ReturnsCanApproveFalse_WhenDiscussionInsufficient()
    {
        var unitOfWork = SeedContentReviewWithVocabularyAndQuiz();
        var service = Service(unitOfWork);

        var result = await service.ValidateAsync(1, 1);

        Assert.False(result.CanApprove);
        Assert.Contains(result.Issues, i => i.Contains("Discussion"));
    }

    #endregion

    #region Approval Tests

    [Fact]
    public async Task Approve_SetsStatusToApproved()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        var service = Service(unitOfWork);

        var result = await service.ApproveAsync(1, 1);

        Assert.True(result.Success);
        Assert.Equal("Approved", result.Status);

        var story = unitOfWork.Items<Story>().First();
        Assert.Equal(StoryStatus.Approved, story.Status);
        var mediaJob = Assert.Single(unitOfWork.Items<StoryGenerationJob>(), job =>
            job.Operation == GenerationJobOperation.GenerateMediaPackage);
        Assert.Equal(1, mediaJob.StoryVersionId);
        Assert.Equal("p5:1:1", mediaJob.OperationKey);
        Assert.Equal(JobStage.MediaPending, mediaJob.Stage);
    }

    [Fact]
    public async Task Approve_Throws_WhenValidationFails()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        await Assert.ThrowsAsync<BadRequestException>(() => service.ApproveAsync(1, 1));
    }

    [Fact]
    public async Task Approve_WithoutStoryPermission_ThrowsForbidden()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        unitOfWork.Items<Story>().Single().AuthorUserId = 99;

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(unitOfWork).ApproveAsync(2, 1));
        Assert.Empty(unitOfWork.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task Approve_SupervisorWithApproveStoryPermission_Succeeds()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        unitOfWork.Items<Story>().Single().AuthorUserId = 99;
        unitOfWork.Seed(new SupervisionRelationship
        {
            Id = 2, ChildProfileId = 1, SupervisorUserId = 2, SupervisorRole = SupervisorRole.AdditionalSupervisor
        });
        unitOfWork.Seed(new SupervisionPermission
        {
            Id = 2, SupervisionRelationshipId = 2, Permission = Permission.ApproveStory
        });

        var result = await Service(unitOfWork).ApproveAsync(2, 1);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Approve_WhenMediaJobAlreadyExists_ThrowsConflictWithoutDuplicate()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        unitOfWork.Seed(new StoryGenerationJob
        {
            Id = 10, StoryId = 1, StoryVersionId = 1,
            Operation = GenerationJobOperation.GenerateMediaPackage,
            Status = GenerationJobStatus.Pending
        });

        var exception = await Assert.ThrowsAsync<ConflictException>(() => Service(unitOfWork).ApproveAsync(1, 1));

        Assert.Equal("MEDIA_JOB_ALREADY_EXISTS", exception.Message);
        Assert.Single(unitOfWork.Items<StoryGenerationJob>());
        Assert.Equal(StoryStatus.ContentReview, unitOfWork.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task Archive_SetsStatusToArchived()
    {
        var unitOfWork = SeedContentReview();
        var service = Service(unitOfWork);

        var result = await service.ArchiveAsync(1, 1, new ArchiveRequestDto { Reason = "Test" });

        Assert.True(result.Success);
        Assert.Equal("Archived", result.Status);

        var story = unitOfWork.Items<Story>().First();
        Assert.Equal(StoryStatus.Archived, story.Status);
    }

    [Fact]
    public async Task Archive_WithoutStoryPermission_ThrowsForbidden()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Items<Story>().Single().AuthorUserId = 99;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(unitOfWork).ArchiveAsync(2, 1, new ArchiveRequestDto { Reason = "No access" }));

        Assert.Equal(StoryStatus.ContentReview, unitOfWork.Items<Story>().Single().Status);
    }

    #endregion

    #region Proposal Tests

    [Fact]
    public async Task CreateProposal_StoresInCache()
    {
        var unitOfWork = SeedContentReview();
        var cache = new InMemoryProposalCache();
        var service = new StoryReviewService(unitOfWork, new FakeAIClient(), cache);

        var result = await service.CreateProposalAsync(1, 1, new PartialEditRequestDto
        {
            VersionId = 1,
            Selection = new TextSelectionDto { Start = 0, EndExclusive = 3, Text = "Đây" },
            Instruction = "Make simpler"
        });

        Assert.False(string.IsNullOrWhiteSpace(result.ProposalId));
        var proposal = await cache.GetAsync(result.ProposalId);
        Assert.NotNull(proposal);
        Assert.Equal(1, proposal.RequestedByUserId);
    }

    [Fact]
    public async Task ApplyStoryProposal_ReplacesOnlySelection_AndSetsEditor()
    {
        var unitOfWork = SeedContentReview();
        var cache = new InMemoryProposalCache();
        var reviewService = new StoryReviewService(unitOfWork, new FakeAIClient(), cache);
        var created = await reviewService.CreateProposalAsync(1, 1, new PartialEditRequestDto
        {
            VersionId = 1,
            Selection = new TextSelectionDto { Start = 0, EndExclusive = 3, Text = "Đây" },
            Instruction = "Make simpler"
        });

        var proposalService = new ProposalService(cache, unitOfWork);
        await proposalService.ApplyProposalAsync(1, 1, created.ProposalId);

        var version = unitOfWork.Items<StoryVersion>().Single();
        Assert.StartsWith("Refined content...", version.Content);
        Assert.Contains(" là nội dung truyện", version.Content);
        Assert.Equal(1, version.EditorUserId);
        Assert.Equal(VersionEditType.AiRefined, version.EditType);
    }

    [Fact]
    public async Task DiscardStoryProposal_DoesNotChangeStory()
    {
        var unitOfWork = SeedContentReview();
        var original = unitOfWork.Items<StoryVersion>().Single().Content;
        var cache = new InMemoryProposalCache();
        var reviewService = new StoryReviewService(unitOfWork, new FakeAIClient(), cache);
        var created = await reviewService.CreateProposalAsync(1, 1, new PartialEditRequestDto
        {
            VersionId = 1,
            Selection = new TextSelectionDto { Start = 0, EndExclusive = 3, Text = "Đây" },
            Instruction = "Make simpler"
        });

        var proposalService = new ProposalService(cache, unitOfWork);
        await proposalService.DiscardProposalAsync(1, 1, created.ProposalId);

        Assert.Equal(original, unitOfWork.Items<StoryVersion>().Single().Content);
    }

    #endregion

    #region Helper Methods

    private static StoryReviewService Service(
        FakeUnitOfWork unitOfWork,
        IAIStoryGenerationClient? aiClient = null,
        IProposalCache? cache = null)
    {
        return new StoryReviewService(
            unitOfWork,
            aiClient ?? new FakeAIClient(),
            cache ?? new InMemoryProposalCache());
    }

    internal static FakeUnitOfWork SeedContentReview()
    {
        var unitOfWork = new FakeUnitOfWork();

        // Seed User
        unitOfWork.Seed(new UserAccount { Id = 1, Role = UserRole.Parent, Status = AccountStatus.LoggedIn });

        // Seed Story in ContentReview status
        unitOfWork.Seed(new Story
        {
            Id = 1,
            ChildProfileId = 1,
            AuthorUserId = 1,
            Source = StorySource.Ai,
            Status = StoryStatus.ContentReview
        });

        // Seed Supervision
        unitOfWork.Seed(new SupervisionRelationship
        {
            Id = 1,
            ChildProfileId = 1,
            SupervisorUserId = 1,
            SupervisorRole = SupervisorRole.Owner
        });

        // Seed StoryVersion
        unitOfWork.Seed(new StoryVersion
        {
            Id = 1,
            StoryId = 1,
            VersionNo = 1,
            Title = "Cáo nhỏ tốt bụng",
            Content = "Đây là nội dung truyện mới trong rừng về chuyến phiêu lưu cùng bạn bè.",
            Lesson = "Biết chia sẻ với bạn bè",
            IsCurrent = true,
            EditType = VersionEditType.Initial
        });

        return unitOfWork;
    }

    internal static FakeUnitOfWork SeedContentReviewWithVocabulary()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Seed(new StoryVocabulary { Id = 1, StoryVersionId = 1, Term = "rừng", Definition = "Nơi có nhiều cây" });
        unitOfWork.Seed(new StoryVocabulary { Id = 2, StoryVersionId = 1, Term = "phiêu lưu", Definition = "Đi chơi xa" });
        unitOfWork.Seed(new StoryVocabulary { Id = 3, StoryVersionId = 1, Term = "bạn bè", Definition = "Người cùng chơi" });
        return unitOfWork;
    }

    internal static FakeUnitOfWork SeedContentReviewWithQuiz()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Seed(new QuizItem { Id = 1, StoryVersionId = 1, Type = QuizType.MultipleChoice, Question = "Câu 1?" });
        unitOfWork.Seed(new QuizItem { Id = 2, StoryVersionId = 1, Type = QuizType.TrueFalse, Question = "Câu 2?" });
        unitOfWork.Seed(new QuizItem { Id = 3, StoryVersionId = 1, Type = QuizType.ShortAnswer, Question = "Câu 3?" });
        return unitOfWork;
    }

    internal static FakeUnitOfWork SeedContentReviewWithDiscussion()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Seed(new DiscussionQuestion { Id = 1, StoryVersionId = 1, Question = "Bạn nghĩ sao?" });
        unitOfWork.Seed(new DiscussionQuestion { Id = 2, StoryVersionId = 1, Question = "Bạn sẽ làm gì?" });
        return unitOfWork;
    }

    internal static FakeUnitOfWork SeedContentReviewWithAllArtifacts()
    {
        var unitOfWork = SeedContentReview();
        // Vocabulary (5 valid, unique terms that occur in story content)
        unitOfWork.Seed(new StoryVocabulary { Id = 1, StoryVersionId = 1, Term = "rừng", Definition = "Nơi có nhiều cây" });
        unitOfWork.Seed(new StoryVocabulary { Id = 2, StoryVersionId = 1, Term = "phiêu lưu", Definition = "Đi chơi xa" });
        unitOfWork.Seed(new StoryVocabulary { Id = 3, StoryVersionId = 1, Term = "bạn bè", Definition = "Người cùng chơi" });
        unitOfWork.Seed(new StoryVocabulary { Id = 4, StoryVersionId = 1, Term = "nội dung", Definition = "Điều được kể" });
        unitOfWork.Seed(new StoryVocabulary { Id = 5, StoryVersionId = 1, Term = "chuyến", Definition = "Một lần đi" });
        // Quiz (3 items, all types)
        unitOfWork.Seed(new QuizItem { Id = 1, StoryVersionId = 1, Type = QuizType.MultipleChoice, Question = "Câu 1?", Choices = "[\"A\",\"B\"]", CorrectAnswer = "A" });
        unitOfWork.Seed(new QuizItem { Id = 2, StoryVersionId = 1, Type = QuizType.TrueFalse, Question = "Câu 2?", CorrectAnswer = "true" });
        unitOfWork.Seed(new QuizItem { Id = 3, StoryVersionId = 1, Type = QuizType.ShortAnswer, Question = "Câu 3?", CorrectAnswer = "Bài học" });
        // Discussion (2 items)
        unitOfWork.Seed(new DiscussionQuestion { Id = 1, StoryVersionId = 1, Question = "Bạn nghĩ sao?", IsMoralLesson = true });
        unitOfWork.Seed(new DiscussionQuestion { Id = 2, StoryVersionId = 1, Question = "Bạn sẽ làm gì?" });
        return unitOfWork;
    }

    internal static FakeUnitOfWork SeedContentReviewWithQuizAndDiscussion()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Seed(new QuizItem { Id = 1, StoryVersionId = 1, Type = QuizType.MultipleChoice, Question = "Câu 1?" });
        unitOfWork.Seed(new DiscussionQuestion { Id = 1, StoryVersionId = 1, Question = "Bạn nghĩ sao?" });
        return unitOfWork;
    }

    internal static FakeUnitOfWork SeedContentReviewWithVocabularyAndQuiz()
    {
        var unitOfWork = SeedContentReview();
        unitOfWork.Seed(new StoryVocabulary { Id = 1, StoryVersionId = 1, Term = "rừng", Definition = "Nơi có nhiều cây" });
        unitOfWork.Seed(new StoryVocabulary { Id = 2, StoryVersionId = 1, Term = "phiêu lưu", Definition = "Đi chơi xa" });
        unitOfWork.Seed(new StoryVocabulary { Id = 3, StoryVersionId = 1, Term = "bạn bè", Definition = "Người cùng chơi" });
        unitOfWork.Seed(new QuizItem { Id = 1, StoryVersionId = 1, Type = QuizType.MultipleChoice, Question = "Câu 1?" });
        unitOfWork.Seed(new DiscussionQuestion { Id = 1, StoryVersionId = 1, Question = "Bạn nghĩ sao?" });
        return unitOfWork;
    }

    private sealed class FakeAIClient : IAIStoryGenerationClient
    {
        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GenerateStoryContentResponse> GenerateStoryContentAsync(GenerateStoryContentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RefineStoryContentResponse> RefineStoryContentAsync(RefineStoryContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RefineStoryContentResponse
            {
                RequestId = request.RequestId,
                GenerationId = "gen-1",
                Story = new StoryContentDto
                {
                    Title = request.Story.Title,
                    Lesson = request.Story.Lesson,
                    StorySections = new List<StorySectionDto>
                    {
                        new(1, "", "Refined content...")
                    }
                },
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
            });

        public Task<GenerateVocabularyResponse> GenerateVocabularyAsync(GenerateVocabularyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateVocabularyResponse
            {
                RequestId = request.RequestId,
                Items = new List<GeneratedVocabularyItemDto>
                {
                    new("mới", "Từ mới")
                },
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
            });

        public Task<GenerateQuizResponse> GenerateQuizAsync(GenerateQuizRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateQuizResponse
            {
                RequestId = request.RequestId,
                Items = new List<Contracts.AI.Models.QuizItemDto>
                {
                    new() { Type = "MultipleChoice", Question = "New Question?", Options = new[] { "A", "B", "C" } }
                },
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
            });

        public Task<GenerateDiscussionResponse> GenerateDiscussionAsync(GenerateDiscussionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GenerateDiscussionResponse
            {
                RequestId = request.RequestId,
                Items = new List<Contracts.AI.Models.DiscussionQuestionDto>
                {
                    new("New Discussion?")
                },
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
            });

        public Task<EvaluateContentSafetyResponse> EvaluateContentSafetyAsync(EvaluateContentSafetyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EvaluateContentSafetyResponse
            {
                RequestId = request.RequestId,
                IsAllowed = true,
                CanRefine = true,
                Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
            });
    }

    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        private readonly Dictionary<Type, object> _repositories = [];
        public int LockCount { get; private set; }
        public int? FailCommitNumber { get; set; }
        public int CommitCount { get; private set; }
        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)(_repositories.TryGetValue(typeof(T), out var repository)
                ? repository
                : _repositories[typeof(T)] = new FakeRepository<T>());

        public void Seed<T>(T item) where T : class => ((FakeRepository<T>)Repository<T>()).Items.Add(item);
        public IReadOnlyList<T> Items<T>() where T : class => ((FakeRepository<T>)Repository<T>()).Items;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireTransactionLockAsync(int resourceId, CancellationToken cancellationToken = default)
        {
            LockCount++;
            return Task.CompletedTask;
        }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRepository<T> : IGenericRepository<T> where T : class
    {
        public List<T> Items { get; } = [];
        public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item is BaseEntity entity && entity.Id == Convert.ToInt32(id)));
        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items);
        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items.Where(predicate.Compile()).ToArray());
        public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(predicate.Compile()));
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetPagedAsync(int pageIndex, int pageSize, Expression<Func<T, bool>>? filter = null, Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null, string? includeProperties = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity is BaseEntity baseEntity && baseEntity.Id == 0)
            {
                baseEntity.Id = Items.OfType<BaseEntity>().Select(item => item.Id).DefaultIfEmpty().Max() + 1;
            }
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default) { foreach (var entity in entities) await AddAsync(entity, cancellationToken); }
        public void Update(T entity) { }
        public void Delete(T entity) => Items.Remove(entity);
        public void DeleteRange(IEnumerable<T> entities) { foreach (var entity in entities.ToArray()) Items.Remove(entity); }
        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(predicate is null ? Items.Count : Items.Count(predicate.Compile()));
        public Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(predicate.Compile()));
        public IQueryable<T> Query() => Items.AsQueryable();
    }

    #endregion
}
