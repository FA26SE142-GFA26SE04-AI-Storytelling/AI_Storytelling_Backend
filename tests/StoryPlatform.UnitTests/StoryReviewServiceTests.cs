using System.Linq.Expressions;
using Moq;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ContentGeneration.Quality;
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
    [Theory]
    [InlineData("valid")]
    [InlineData("other-story")]
    [InlineData("old-version")]
    [InlineData("old-revision")]
    public async Task AI_review_validates_assistance_provenance_before_creating_version(string scenario)
    {
        var store = SeedContentReview(); var story = store.Items<Story>().Single();
        story.Scope = StoryScope.Parent; story.Language = "vi"; story.AgeBand = "6-8";
        store.Seed(new ChildProfile { Id = 1, OwnerUserId = 1, Status = ChildProfileStatus.Active, Language = "vi", AgeBand = AgeBand.Age_6_8 });
        store.Seed(new SafetyPolicy { Id = 1, ChildProfileId = 1, MaxStoryLength = 2000, ConsentRecorded = true,
            ConsentRecordedAt = DateTime.UtcNow, ConsentPolicyVersion = StoryPlatform.Application.Features.ChildProfiles.Safety.ConsentPolicy.CurrentVersion });
        var policy = new StoryPlatform.Application.Features.ParentAuthoring.ParentAssistanceAccessPolicy(store,
            new StoryPlatform.Application.Features.ChildProfiles.Profiles.Services.ChildProfileAccessGuard(store));
        var snapshot = await policy.CaptureContextAsync(story, default);
        var token = story.AuthoringRevisionToken;
        store.Seed(new StoryAssistanceTask { Id = 5, OwnerUserId = 1, StoryId = scenario == "other-story" ? 99 : 1,
            SourceVersionId = scenario == "old-version" ? 99 : 1, SourceRevisionToken = scenario == "old-revision" ? "old" : token,
            AudienceRevision = story.AudienceRevision, SnapshotHash = "snapshot", ContextJson =
            StoryPlatform.Application.Features.CreatorAssistance.Services.AssistanceContextStorage.Store(new(), snapshot) });
        store.Seed(new StoryAssistanceStep { Id = 8, TaskId = 5, Kind = "Trial", Status = "Succeeded", OutputJson = "{}", Provider = "fixture" });
        var service = Service(store);
        Assert.Equal(token, (await service.GetReviewPackageAsync(1, 1)).AssistanceRevisionToken);
        var request = new UpdateStoryRequestDto { VersionId = 1, Title = "Mi và mẹ", Content = "Mi rủ mẹ cùng sửa hộp.", Lesson = "Hợp tác",
            AssistanceReferences = [new(5, 8, "TrialUsed")] };
        if (scenario != "valid")
        {
            var error = await Assert.ThrowsAsync<StoryPlatform.Application.Features.CreatorAssistance.AssistanceException>(()=>service.UpdateStoryAsync(1, 1, request));
            Assert.Equal(scenario == "other-story" ? "INVALID_ASSISTANCE_REFERENCES" : "EDITOR_CONTEXT_STALE", error.Code);
            Assert.Single(store.Items<StoryVersion>()); Assert.Equal(token, story.AuthoringRevisionToken);
            return;
        }
        await service.UpdateStoryAsync(1, 1, request);
        Assert.Equal(2, store.Items<StoryVersion>().Count);
        Assert.NotEqual(token, story.AuthoringRevisionToken);
        Assert.Contains("TrialUsed", store.Items<StoryVersion>().Single(v=>v.IsCurrent).AuthoringMetadata.AssistanceProvenanceJson);
        Assert.Null(store.Items<StoryVersion>().Single(v=>!v.IsCurrent).AuthoringMetadata.AssistanceProvenanceJson);
    }
    [Theory]
    [InlineData(99, true)]
    [InlineData(20, false)]
    public async Task Creator_EditsRecheckSafetyAndPreserveVersionHistory(int score, bool accepted)
    {
        var store = SeedContentReviewWithAllArtifacts();
        store.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var story = store.Items<Story>().Single();
        var request = new StoryGenerationRequest
        {
            Id = 1,
            StoryId = 1,
            Status = GenerationInputStatus.InputAccepted,
            ContextSnapshotJson = JsonSerializer.Serialize(new StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot(
                1, "6-8", 2, "level_2", "vi", 1000, "always_manual", [], [], [], [], SafetyScoreThreshold: 90m),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        CreatorGenerationTestContext.Configure(story, request);
        store.Seed(request);
        var ai = new Moq.Mock<IAIStoryGenerationClient>(Moq.MockBehavior.Strict);
        ai.Setup(x => x.EvaluateContentSafetyAsync(Moq.It.IsAny<EvaluateContentSafetyRequest>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvaluateContentSafetyResponse { IsAllowed = true, SafetyScore = score });
        var service = Service(store, ai.Object);
        var before = store.Items<StoryVersion>().Single();
        var update = new UpdateStoryRequestDto
        {
            VersionId = before.Id,
            Title = before.Title,
            Content = "Lan gặp Minh trong rừng. Hai bạn cùng đọc sách.",
            Lesson = before.Lesson!
        };
        if (!accepted)
        {
            await Assert.ThrowsAsync<BadRequestException>(() => service.UpdateStoryAsync(1, 1, update));
            Assert.Single(store.Items<StoryVersion>());
            Assert.True(before.IsCurrent);
        }
        else
        {
            await service.UpdateStoryAsync(1, 1, update);
            Assert.Equal(2, store.Items<StoryVersion>().Count);
            Assert.False(before.IsCurrent);
            Assert.Equal((decimal)score, store.Items<StoryVersion>().Single(x => x.IsCurrent).SafetyScore);
            story.Status = StoryStatus.Ready;
            await Assert.ThrowsAsync<ConflictException>(() => service.UpdateStoryAsync(1, 1, update));
        }
    }

    [Fact]
    public async Task Creator_ConfirmsVersionIntoMediaQueueButCannotApproveOrPublish()
    {
        var store = SeedContentReviewWithAllArtifacts();
        store.Items<UserAccount>().Single().Role = UserRole.ContentCreator;
        var story = store.Items<Story>().Single();
        story.Scope = StoryScope.Creator; story.ChildProfileId = null;
        var context = new StoryPlatform.Application.Features.AIStoryInput.Models.AIStoryInputContextSnapshot(
            null, "6-8", 2, "level_2", "vi", 1000, "always_manual", [], [], [], [],
            SafetyScoreThreshold: 90m, AudienceScope: "Creator", CreatorUserId: 1);
        store.Seed(new StoryGenerationRequest
        {
            Id = 1,
            StoryId = 1,
            SubmittedByUserId = 1,
            Status = GenerationInputStatus.InputAccepted,
            ContextSnapshotJson = JsonSerializer.Serialize(context,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        store.Items<StoryVersion>().Single().SafetyScore = 99m;
        var service = Service(store);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetReviewPackageAsync(99, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetVocabularyForReviewAsync(99, 1));
        Assert.Null(await service.EvaluateAndApplyAutoPublishAsync(1));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveAsync(1, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ConfirmCreatorContentAsync(99, 1,
            new ConfirmCreatorContentRequestDto { VersionId = store.Items<StoryVersion>().Single().Id }));
        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmCreatorContentAsync(1, 1,
            new ConfirmCreatorContentRequestDto { VersionId = 999 }));
        Assert.Empty(store.Items<StoryGenerationJob>());
        var version = store.Items<StoryVersion>().Single();
        var reviewStatus = version.Moderation.ReviewStatus;
        var validation = await service.ValidateAsync(1, 1);
        Assert.False(validation.CanApprove);
        Assert.True(validation.CanConfirmContent);
        var package = await service.GetReviewPackageAsync(1, 1);
        Assert.False(package.CanApprove);
        Assert.True(package.CanConfirmContent);
        // A previous release's completed media job must not block this version's new media package.
        store.Seed(new StoryGenerationJob
        {
            Id = 99,
            StoryId = 1,
            StoryVersionId = 999,
            Operation = GenerationJobOperation.GenerateMediaPackage,
            Status = GenerationJobStatus.Completed
        });
        var result = await service.ConfirmCreatorContentAsync(1, 1,
            new ConfirmCreatorContentRequestDto { VersionId = version.Id });
        Assert.Equal(version.Id, result.VersionId);
        Assert.Equal("MediaProcessing", result.Status);
        Assert.Equal(StoryStatus.MediaProcessing, story.Status);
        Assert.Equal(reviewStatus, version.Moderation.ReviewStatus);
        Assert.False(story.IsPublished);
        await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmCreatorContentAsync(1, 1,
            new ConfirmCreatorContentRequestDto { VersionId = version.Id }));
        var job = Assert.Single(store.Items<StoryGenerationJob>().Where(x => x.StoryVersionId == version.Id));
        Assert.Equal(GenerationJobOperation.GenerateMediaPackage, job.Operation);
        Assert.Equal(version.Id, job.StoryVersionId);
        Assert.Equal(version.Id, job.BaseStoryVersionId);
        Assert.False((await service.ValidateAsync(1, 1)).CanConfirmContent);
    }

    [Fact]
    public async Task Parent_CannotUseCreatorContentConfirmation()
    {
        var store = SeedContentReviewWithAllArtifacts();
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(store).ConfirmCreatorContentAsync(1, 1,
            new ConfirmCreatorContentRequestDto { VersionId = store.Items<StoryVersion>().Single().Id }));
        Assert.Empty(store.Items<StoryGenerationJob>());
        Assert.Equal(StoryStatus.ContentReview, store.Items<Story>().Single().Status);
    }

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
    public async Task UpdateStory_RecalculatesVietnameseReadabilityAndReturnsMetrics()
    {
        var unitOfWork = SeedContentReview();
        var oldVersion = unitOfWork.Items<StoryVersion>().Single();
        oldVersion.ReadabilityFkgl = 99m;
        oldVersion.ReadabilityFre = 1m;
        const string updatedContent = "Lan gặp Minh trong rừng. Hai bạn cùng đọc sách và giúp đỡ nhau.";

        var result = await Service(unitOfWork).UpdateStoryAsync(1, 1, new UpdateStoryRequestDto
        {
            VersionId = oldVersion.Id,
            Title = oldVersion.Title,
            Content = updatedContent,
            Lesson = oldVersion.Lesson!
        });

        var expected = ReadabilityCalculator.Calculate(updatedContent, "vi");
        var current = unitOfWork.Items<StoryVersion>().Single(version => version.IsCurrent);
        Assert.Equal("VI_READABILITY_V1", result.ReadabilityAlgorithm);
        Assert.Equal(expected.Fkgl, current.ReadabilityFkgl);
        Assert.Equal(expected.Fre, current.ReadabilityFre);
        Assert.Equal(current.ReadabilityFkgl, result.ReadabilityFkgl);
        Assert.Equal(current.ReadabilityFre, result.ReadabilityFre);
        Assert.NotEqual(99m, current.ReadabilityFkgl);
        Assert.NotEqual(1m, current.ReadabilityFre);
    }

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
    public async Task Approve_ChildOwner_Succeeds()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        unitOfWork.Items<Story>().Single().AuthorUserId = 99;
        unitOfWork.Items<ChildProfile>().Single().OwnerUserId = 2;

        var result = await Service(unitOfWork).ApproveAsync(2, 1);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Approve_WhenMediaJobAlreadyExists_ThrowsConflictWithoutDuplicate()
    {
        var unitOfWork = SeedContentReviewWithAllArtifacts();
        unitOfWork.Seed(new StoryGenerationJob
        {
            Id = 10,
            StoryId = 1,
            StoryVersionId = 1,
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


    [Theory]
    [InlineData("vocabulary")]
    [InlineData("quiz")]
    [InlineData("discussion")]
    public async Task ApplyArtifactProposal_AcceptsLegacyPascalCaseItems(string artifact)
    {
        var store = SeedContentReview();
        var cache = new InMemoryProposalCache();
        object items = artifact switch
        {
            "vocabulary" => new[] { new { Term = "friend", Definition = "A companion" } },
            "quiz" => new[] { new { Type = "MultipleChoice", Question = "Who helped?", CorrectAnswer = "Lan", Choices = new[] { "Lan", "Minh" } } },
            _ => new[] { new { Question = "How can you help?", IsMoralLesson = true } }
        };
        var id = await cache.CreateAsync(new AIProposalDto
        {
            RequestedByUserId = 1,
            StoryId = 1,
            StoryVersionId = 1,
            ArtifactType = artifact,
            OperationType = "regenerate",
            Status = "pending",
            SuggestedContent = JsonSerializer.SerializeToElement(new { items }),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        var applied = await new ProposalService(cache, store).ApplyProposalAsync(1, 1, id);
        Assert.True(applied.Success);
        Assert.Equal("applied", (await cache.GetAsync(id))!.Status);
        if (artifact == "vocabulary") Assert.Equal("friend", Assert.Single(store.Items<StoryVocabulary>()).Term);
        if (artifact == "quiz")
        {
            var quiz = Assert.Single(store.Items<QuizItem>());
            Assert.Equal("Who helped?", quiz.Question);
            Assert.Equal("Lan", quiz.CorrectAnswer);
            Assert.Contains("Minh", quiz.Choices!);
        }
        if (artifact == "discussion") Assert.True(Assert.Single(store.Items<DiscussionQuestion>()).IsMoralLesson);
    }

    [Theory]
    [InlineData("vocabulary", "term")]
    [InlineData("quiz", "question")]
    [InlineData("discussion", "question")]
    public async Task GeneratedArtifactProposal_IsCamelCase_AndCanApply(string artifact, string field)
    {
        var store = SeedContentReview();
        var cache = new InMemoryProposalCache();
        var review = Service(store, cache: cache);
        var created = artifact switch
        {
            "vocabulary" => await review.CreateVocabularyProposalAsync(1, 1),
            "quiz" => await review.CreateQuizProposalAsync(1, 1),
            _ => await review.CreateDiscussionProposalAsync(1, 1)
        };
        var proposal = (await cache.GetAsync(created.ProposalId))!;
        var suggested = Assert.IsType<JsonElement>(proposal.SuggestedContent);
        Assert.True(suggested.GetProperty("items")[0].TryGetProperty(field, out _));
        Assert.True((await new ProposalService(cache, store).ApplyProposalAsync(1, 1, created.ProposalId)).Success);
    }

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
        unitOfWork.Seed(new UserAccount { Id = 1, Role = UserRole.User, Status = AccountStatus.Active });
        unitOfWork.Seed(new ChildProfile { Id = 1, OwnerUserId = 1, Status = ChildProfileStatus.Active });

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

        // Seed StoryVersion
        unitOfWork.Seed(new StoryVersion
        {
            Id = 1,
            StoryId = 1,
            VersionNo = 1,
            Title = "Cáo nhỏ tốt bụng",
            Content = "Đây là nội dung truyện mới trong rừng. Cáo nhỏ có chuyến phiêu lưu cùng bạn bè.",
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
