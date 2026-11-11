using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class PublicStoryReviewServiceTests
{
    private readonly PublicStoryReviewHarness _h = new();

    [Fact]
    public async Task Uploaded_audio_requires_every_segment_heard_even_with_word_timings()
    {
        var first=_h.MakeApprovable();
        var second=_h.AddScene(2,2,withWordTimings:true);
        _h.Assets.Single(x=>x.StorySegmentId==second && x.Type==MediaType.TtsAudio).Source=MediaAssetSource.UserUpload;
        _h.Items[0].ReviewProgress=$"{{\"viewed\":{{\"scene\":[1,2],\"audio\":[{first}]}}}}";
        var package=await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId,PublicStoryReviewHarness.ItemId);
        Assert.False(package.CanApprove); Assert.Contains(second,package.Progress.MissingWarningSegmentIds);
        Assert.True(package.Scenes.Single(x=>x.SceneId==2).Segments.Single().RequiresFullAudioReview);
        await _h.Sut.MarkViewedAsync(PublicStoryReviewHarness.ModeratorId,PublicStoryReviewHarness.ItemId,
            new MarkPartViewedRequestDto {PartType="audio",PartId=second});
        package=await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId,PublicStoryReviewHarness.ItemId);
        Assert.True(package.CanApprove);
    }

    [Fact]
    public async Task GetPackageAsync_ShowsListeningInteractionsWithOptionsAndMedia()
    {
        _h.AddScene(1, 1);
        var checkpoint = _h.AddListening(1, PublicStoryReviewHarness.VersionId, ListeningInteractionType.Checkpoint, 10, "Thỏ thấy gì?", "Sợ", "Vui");
        checkpoint.StorySceneId = 1;
        _h.AddListening(2, PublicStoryReviewHarness.VersionId, ListeningInteractionType.EventOrdering, 30, null, "Thỏ lạc", "Gấu giúp");
        _h.AddListening(3, PublicStoryReviewHarness.VersionId, ListeningInteractionType.RolePlay, 20, null).IsDeleted = true;
        _h.Assets.Add(new MediaAsset
        {
            Id = 900, StoryVersionId = PublicStoryReviewHarness.VersionId, ListeningInteractionId = 1,
            Type = MediaType.ListeningAudio, AudioPartKey = "prompt", Status = MediaStatus.Ready,
            Url = "https://cdn.test/listen1.mp3", IsCurrent = true
        });
        _h.Assets.Add(new MediaAsset
        {
            Id = 901, StoryVersionId = PublicStoryReviewHarness.VersionId, ListeningInteractionOptionId = 11,
            Type = MediaType.ListeningImage, Status = MediaStatus.Ready, Url = "https://cdn.test/opt11.png", IsCurrent = true
        });

        var package = await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);
        Assert.Equal(new[] { 1, 2 }, package.ListeningInteractions.Select(x => x.Id));
        var first = package.ListeningInteractions[0];
        Assert.Equal("Checkpoint", first.Type);
        Assert.Equal(1, first.StorySceneId);
        Assert.Equal("Thỏ thấy gì?", first.Prompt);
        Assert.Equal(new[] { "Sợ", "Vui" }, first.Options.Select(x => x.Text));
        Assert.True(first.Options[0].IsMatch);
        Assert.Equal("https://cdn.test/opt11.png", first.Options[0].ImageUrl);
        Assert.Equal("https://cdn.test/listen1.mp3", first.Audio.Single().Url);
        Assert.Equal(new int?[] { 1, 2 }, package.ListeningInteractions[1].Options.Select(x => x.CorrectOrder));
    }

    [Fact]
    public async Task GetPackageAsync_ListeningAudioCarriesOptionIdInOptionOrder()
    {
        _h.AddScene(1, 1);
        _h.AddListening(1, PublicStoryReviewHarness.VersionId, ListeningInteractionType.Checkpoint, 10, "Thỏ thấy gì?", "Sợ", "Vui");
        _h.ListeningOptions.Add(new ListeningInteractionOption { Id = 13, ListeningInteractionId = 1, SortOrder = 3, Text = "Xoá", IsDeleted = true });
        void Audio(int id, int? optionId, string key) => _h.Assets.Add(new MediaAsset
        {
            Id = id, StoryVersionId = PublicStoryReviewHarness.VersionId, ListeningInteractionId = 1,
            ListeningInteractionOptionId = optionId, Type = MediaType.ListeningAudio, AudioPartKey = key,
            Status = MediaStatus.Ready, Url = $"https://cdn.test/{id}.mp3", IsCurrent = true
        });
        Audio(910, 12, "text");
        Audio(911, 11, "text");
        Audio(912, null, "prompt");
        Audio(913, 11, "feedback");
        Audio(914, 13, "text");

        var package = await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal(new (int?, string?)[] { (null, "prompt"), (11, "feedback"), (11, "text"), (12, "text") },
            package.ListeningInteractions.Single().Audio.Select(x => (x.OptionId, x.PartKey)));
    }

    [Fact]
    public async Task RequestChangesAsync_AcceptsListeningPartOfThisVersionOnly()
    {
        _h.AddScene(1, 1);
        _h.AddListening(1, PublicStoryReviewHarness.VersionId, ListeningInteractionType.Checkpoint, 10, "Thỏ thấy gì?", "Sợ", "Vui");
        _h.AddListening(2, PublicStoryReviewHarness.VersionId, ListeningInteractionType.RolePlay, 20, null).IsDeleted = true;
        RequestChangesDto Request(int id) => new()
        {
            GeneralNote = "Sửa câu hỏi tương tác.",
            Parts = new List<ChangeRequestPartDto> { new() { PartType = "listening", PartId = id, Note = "Câu hỏi khó hiểu." } }
        };

        await Assert.ThrowsAsync<BadRequestException>(() => RequestChangesDecisionAsync(Request(2)));
        await Assert.ThrowsAsync<BadRequestException>(() => RequestChangesDecisionAsync(Request(999)));
        var result = await RequestChangesDecisionAsync(Request(1));
        Assert.Equal("ChangesRequested", result.Decision);
        using var feedback = JsonDocument.Parse(_h.Versions[0].Moderation.ReviewFeedback!);
        var part = feedback.RootElement.GetProperty("parts")[0];
        Assert.Equal("listening", part.GetProperty("partType").GetString());
        Assert.Equal(1, part.GetProperty("partId").GetInt32());
    }

    [Fact]
    public async Task GetPackageAsync_ClaimedByActor_ReturnsFullSubmittedVersion()
    {
        _h.AddScene(1, 1);
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1,
            StoryVersionId = PublicStoryReviewHarness.VersionId,
            Type = QuizType.MultipleChoice,
            Question = "Ai thắng?",
            CorrectAnswer = "Rùa",
            Choices = "[\"Rùa\",\"Thỏ\"]"
        });
        _h.Vocabulary.Add(new StoryVocabulary
        {
            Id = 1,
            StoryVersionId = PublicStoryReviewHarness.VersionId,
            Term = "kiên nhẫn",
            Definition = "bền bỉ chờ đợi"
        });
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 1,
            StoryId = PublicStoryReviewHarness.StoryId,
            ContentCategoryId = 5,
            Source = TagSource.Ai
        });

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal(PublicStoryReviewHarness.VersionId, package.StoryVersionId);
        Assert.Equal("Cậu bé và chú rùa", package.Story.Title);
        Assert.Equal("Kiên nhẫn sẽ thắng", package.Version.Lesson);
        Assert.Equal("https://cdn.test/img1.png", package.Scenes.Single().Illustrations.Single().Url);
        Assert.Equal("https://cdn.test/audio1.mp3", package.Scenes.Single().Segments.Single().AudioUrl);
        Assert.Equal("Rùa", package.Quizzes.Single().CorrectAnswer);
        Assert.Equal("kiên nhẫn", package.Vocabulary.Single().Term);
        Assert.Equal("friendship", package.Story.Labels.Single().Code);
    }

    [Fact]
    public async Task GetPackageAsync_IncludesSubmitterIdentityWithoutEmail_SubmittedAt_AndSubmittedVersionTitle()
    {
        var creator = _h.Users.Single(x => x.Id == PublicStoryReviewHarness.CreatorId);
        creator.FullName = "Nguyễn Văn A";
        creator.Email = "creator.private@test.local";
        var submittedAt = new DateTime(2026, 11, 10, 7, 15, 0, DateTimeKind.Utc);
        _h.Items[0].CreatedByUserId = PublicStoryReviewHarness.CreatorId;
        _h.Items[0].CreatedAt = submittedAt;
        _h.Stories[0].Title = "Tên truyện mới sau đó";
        _h.Versions[0].Title = "Mái Nhà Ấm Áp Sau Cơn Mưa";

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal(PublicStoryReviewHarness.CreatorId, package.CreatedByUserId);
        Assert.Equal("Nguyễn Văn A", package.CreatedByName);
        Assert.DoesNotContain("@", package.CreatedByName ?? string.Empty);
        Assert.Equal(submittedAt, package.SubmittedAt);
        Assert.Equal("Mái Nhà Ấm Áp Sau Cơn Mưa", package.Version.Title);
    }

    [Fact]
    public async Task GetPackageAsync_SubmitterFallsBackToUsername_AndHandlesMissingSubmitter()
    {
        var creator = _h.Users.Single(x => x.Id == PublicStoryReviewHarness.CreatorId);
        creator.FullName = "  ";
        creator.Username = "creator_handle";

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);
        Assert.Equal(PublicStoryReviewHarness.CreatorId, package.CreatedByUserId);
        Assert.Equal("creator_handle", package.CreatedByName);

        _h.Items[0].CreatedByUserId = 99999;
        var missingPackage = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);
        Assert.Equal(99999, missingPackage.CreatedByUserId);
        Assert.Null(missingPackage.CreatedByName);
    }

    [Fact]
    public async Task GetPackageAsync_Opened_WritesAudit()
    {
        await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        _h.Audit.Verify(a => a.LogAsync(
            PublicStoryReviewHarness.ModeratorId, "PublicStoryReviewPackageOpened", nameof(ModerationItem),
            PublicStoryReviewHarness.ItemId, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPackageAsync_ItemClaimedByOtherModerator_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.OtherModeratorId, PublicStoryReviewHarness.ItemId));
    }

    [Fact]
    public async Task GetPackageAsync_ItemNotClaimed_IsForbidden()
    {
        _h.Items[0].Status = ModerationItemStatus.Pending;
        _h.Items[0].ClaimedByUserId = null;

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId));
    }

    [Fact]
    public async Task GetPackageAsync_ExpiredClaim_IsConflict()
    {
        _h.Items[0].ClaimedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(-1);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId));

        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task GetPackageAsync_WrongItemType_IsBadRequest()
    {
        _h.Items[0].ItemType = ModerationItemType.ContentReport;

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId));
    }

    [Fact]
    public async Task GetPackageAsync_NonStaffActor_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.CreatorId, PublicStoryReviewHarness.ItemId));
    }

    [Fact]
    public async Task GetPackageAsync_VersionFromAnotherStory_IsBadRequest()
    {
        _h.Versions[0].StoryId = 999;

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId));
    }

    [Fact]
    public async Task GetPackageAsync_ReportsMissingViewsWarningsAndFlags()
    {
        _h.AddScene(1, 1);
        var warningSegment = _h.AddScene(2, 2, withWordTimings: false);
        _h.Versions[0].Moderation.GrayZoneFlags =
            "{\"sections\":[\"development\"],\"artifacts\":[\"quiz\"],\"reason\":\"age_band_review\"}";

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal(new[] { 1, 2 }, package.Progress.MissingSceneIds);
        Assert.Equal(new[] { warningSegment }, package.Progress.MissingWarningSegmentIds);
        Assert.True(package.Progress.NeedsAtLeastOneAudio);
        Assert.False(package.Progress.RequiredViewsComplete);
        Assert.Equal(new[] { "section:development", "artifact:quiz" },
            package.Flags.Select(x => x.FlagId));
        Assert.All(package.Flags, flag => Assert.False(flag.Resolved));
        Assert.False(package.AllFlagsResolved);
        Assert.Equal("GrayZone", package.SafetyScan.Status);
    }

    [Fact]
    public async Task GetPackageAsync_SafeAssessment_ExposesSafetyScanStatusAndLowRiskAdvisoryFindingsWithoutBlockingApprove()
    {
        _h.MakeApprovable();
        _h.Versions[0].Content = "Mở đầu. Cậu mang cháo lên bàn và thắp sáng ngọn đèn dầu nhỏ. Kết thúc.";
        _h.Versions[0].Moderation.ModerationZone = ModerationZone.Safe;
        _h.Versions[0].Moderation.GrayZoneFlags = """
            {
              "flags": [],
              "assessment": {
                "zone": 1,
                "phase": "phase3",
                "provider": "Vertex",
                "model": "gemini-3.8-flash",
                "riskScore": 25,
                "findings": [
                  {
                    "kind": "paragraph",
                    "name": "paragraph:5",
                    "category": "GZ02",
                    "reason": "Nhân vật tự thắp đèn dầu trong nhà.",
                    "evidenceQuote": "Cậu mang cháo lên bàn và thắp sáng ngọn đèn dầu nhỏ.",
                    "contextNote": "Bà nội đang ngồi ngay gần đó.",
                    "startOffset": 8,
                    "endOffset": 61
                  }
                ]
              }
            }
            """;

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Empty(package.Flags);
        Assert.True(package.AllFlagsResolved);
        Assert.True(package.CanApprove);
        Assert.Equal("Safe", package.SafetyScan.Status);
        Assert.Equal("Safe", package.SafetyScan.Zone);
        Assert.Equal(25m, package.SafetyScan.RiskScore);
        Assert.Equal("phase3", package.SafetyScan.Phase);
        Assert.Equal("Vertex", package.SafetyScan.Provider);
        Assert.Equal("gemini-3.8-flash", package.SafetyScan.Model);
        var advisory = Assert.Single(package.SafetyScan.AdvisoryFindings);
        Assert.Equal("GZ02", advisory.CriterionCode);
        Assert.Equal("Cậu mang cháo lên bàn và thắp sáng ngọn đèn dầu nhỏ.", advisory.EvidenceQuote);
        Assert.Equal("Bà nội đang ngồi ngay gần đó.", advisory.ContextNote);
        Assert.Equal(8, advisory.StartOffset);
        Assert.Equal(61, advisory.EndOffset);
        Assert.True(advisory.Resolved);
    }

    [Fact]
    public async Task GetPackageAsync_WithoutAiAssessment_ReportsNotChecked_AndGrayZoneDoesNotDuplicateAdvisoryFindings()
    {
        _h.Versions[0].Moderation.ModerationZone = null;
        _h.Versions[0].Moderation.GrayZoneFlags = null;

        var uncheckedPackage = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal("NotChecked", uncheckedPackage.SafetyScan.Status);
        Assert.Null(uncheckedPackage.SafetyScan.Zone);
        Assert.Null(uncheckedPackage.SafetyScan.RiskScore);
        Assert.Empty(uncheckedPackage.SafetyScan.AdvisoryFindings);

        _h.Versions[0].Moderation.ModerationZone = ModerationZone.GrayZone;
        _h.Versions[0].Moderation.GrayZoneFlags = """
            {
              "flags": [
                {
                  "id": "abc:0",
                  "kind": "paragraph",
                  "name": "paragraph:1",
                  "reason": "Cần kiểm tra ngữ cảnh",
                  "category": "GZ02",
                  "evidenceQuote": "Đoạn A",
                  "contextNote": "Ngữ cảnh A",
                  "startOffset": 0,
                  "endOffset": 6
                }
              ],
              "assessment": {
                "zone": 2,
                "phase": "phase3",
                "provider": "Vertex",
                "model": "gemini-3.8-flash",
                "riskScore": 55,
                "findings": [
                  {
                    "kind": "paragraph",
                    "name": "paragraph:1",
                    "category": "GZ02",
                    "reason": "Cần kiểm tra ngữ cảnh",
                    "evidenceQuote": "Đoạn A",
                    "contextNote": "Ngữ cảnh A",
                    "startOffset": 0,
                    "endOffset": 6
                  }
                ]
              }
            }
            """;

        var grayPackage = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal("GrayZone", grayPackage.SafetyScan.Status);
        Assert.Equal(55m, grayPackage.SafetyScan.RiskScore);
        Assert.Single(grayPackage.Flags);
        Assert.Empty(grayPackage.SafetyScan.AdvisoryFindings);
    }

    [Fact]
    public async Task GetPackageAsync_ReflectsSavedProgressAndChecklist()
    {
        var segmentId = _h.AddScene(1, 1);
        _h.Items[0].ReviewProgress = $"{{\"viewed\":{{\"scene\":[1],\"audio\":[{segmentId}]}}}}";
        _h.Items[0].Checklist = "{\"safety\":true,\"image\":false}";

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.True(package.Progress.RequiredViewsComplete);
        Assert.True(package.Scenes[0].Viewed);
        Assert.True(package.Scenes[0].Segments[0].Heard);
        Assert.True(package.Checklist.Safety);
        Assert.False(package.Checklist.Image);
        Assert.Null(package.Checklist.Language);
    }

    [Fact]
    public async Task MarkViewedAsync_Scene_StoresOnceAndIncreasesRowVersion()
    {
        _h.AddScene(1, 1);
        _h.AddScene(2, 2);
        var before = _h.Items[0].RowVersion;
        var request = new MarkPartViewedRequestDto { PartType = "scene", PartId = 1 };

        var progress = await _h.Sut.MarkViewedAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, request);
        await _h.Sut.MarkViewedAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, request);

        Assert.Equal(new[] { 2 }, progress.MissingSceneIds);
        Assert.Equal(new HashSet<int> { 1 }, PublicReviewState.GetViewed(
            _h.Items[0].ReviewProgress, PublicReviewRequirements.ScenePart));
        Assert.Equal(before + 2, _h.Items[0].RowVersion);
    }

    [Fact]
    public async Task MarkViewedAsync_AllScenesAndWarningAudio_CompletesRequirements()
    {
        _h.AddScene(1, 1);
        var warningSegment = _h.AddScene(2, 2, withWordTimings: false);
        var id = PublicStoryReviewHarness.ItemId;
        var actor = PublicStoryReviewHarness.ModeratorId;

        await _h.Sut.MarkViewedAsync(actor, id, new MarkPartViewedRequestDto { PartType = "scene", PartId = 1 });
        await _h.Sut.MarkViewedAsync(actor, id, new MarkPartViewedRequestDto { PartType = "scene", PartId = 2 });
        var result = await _h.Sut.MarkViewedAsync(actor, id,
            new MarkPartViewedRequestDto { PartType = "audio", PartId = warningSegment });

        Assert.True(result.RequiredViewsComplete);
        Assert.False(result.NeedsAtLeastOneAudio);
        Assert.Empty(result.MissingWarningSegmentIds);
    }

    [Fact]
    public async Task MarkViewedAsync_OtherAudio_DoesNotSatisfyWarningRequirement()
    {
        var plainSegment = _h.AddScene(1, 1);
        _h.AddScene(2, 2, withWordTimings: false);
        var actor = PublicStoryReviewHarness.ModeratorId;
        var id = PublicStoryReviewHarness.ItemId;
        await _h.Sut.MarkViewedAsync(actor, id, new MarkPartViewedRequestDto { PartType = "scene", PartId = 1 });
        await _h.Sut.MarkViewedAsync(actor, id, new MarkPartViewedRequestDto { PartType = "scene", PartId = 2 });

        var result = await _h.Sut.MarkViewedAsync(actor, id,
            new MarkPartViewedRequestDto { PartType = "audio", PartId = plainSegment });

        Assert.False(result.NeedsAtLeastOneAudio);
        Assert.Single(result.MissingWarningSegmentIds);
        Assert.False(result.RequiredViewsComplete);
    }

    [Theory]
    [InlineData("scene", 99)]
    [InlineData("ending", 1)]
    public async Task MarkViewedAsync_InvalidPart_IsBadRequest(string partType, int partId)
    {
        _h.AddScene(1, 1);

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.MarkViewedAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new MarkPartViewedRequestDto { PartType = partType, PartId = partId }));
    }

    [Fact]
    public async Task MarkViewedAsync_AudioWithoutFile_IsBadRequest()
    {
        var segmentId = _h.AddScene(1, 1, withAudio: false);

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.MarkViewedAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new MarkPartViewedRequestDto { PartType = "audio", PartId = segmentId }));
    }

    [Fact]
    public async Task ResolveFlagAsync_LastFlag_ClearsVersionAndWritesAudit()
    {
        GivenTwoFlags();
        var actor = PublicStoryReviewHarness.ModeratorId;
        var id = PublicStoryReviewHarness.ItemId;
        await _h.Sut.ResolveFlagAsync(actor, id,
            new ResolveFlagRequestDto { FlagId = "section:development", Reason = "ổn" });

        var result = await _h.Sut.ResolveFlagAsync(actor, id,
            new ResolveFlagRequestDto { FlagId = "section:ending", Reason = "ổn" });

        Assert.True(result.AllFlagsResolved);
        Assert.True(result.VersionCleared);
        Assert.Equal(actor, _h.Versions[0].Moderation.GrayZoneClearedByUserId);
        Assert.NotNull(_h.Versions[0].Moderation.GrayZoneClearedAt);
        _h.Audit.Verify(a => a.LogAsync(actor, "PublicStoryReviewFlagResolved", nameof(ModerationItem), id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveFlagAsync_SameFlagTwice_IsIdempotent()
    {
        GivenTwoFlags();
        var request = new ResolveFlagRequestDto { FlagId = "section:development", Reason = "ổn" };

        await _h.Sut.ResolveFlagAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, request);
        await _h.Sut.ResolveFlagAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, request);

        Assert.Single(PublicReviewState.GetResolvedFlags(_h.Items[0].Flags));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveFlagAsync_EmptyReason_IsBadRequest(string reason)
    {
        GivenTwoFlags();

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.ResolveFlagAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new ResolveFlagRequestDto { FlagId = "section:development", Reason = reason }));
    }

    [Fact]
    public async Task ResolveFlagAsync_TooLongOrUnknown_IsBadRequest()
    {
        GivenTwoFlags();

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.ResolveFlagAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new ResolveFlagRequestDto { FlagId = "section:development", Reason = new string('a', 1001) }));
        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.ResolveFlagAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new ResolveFlagRequestDto { FlagId = "section:opening", Reason = "ổn" }));
    }

    [Fact]
    public async Task SaveChecklistAsync_PartialThenMerge_CanCompleteAllEight()
    {
        var actor = PublicStoryReviewHarness.ModeratorId;
        var id = PublicStoryReviewHarness.ItemId;
        var first = await _h.Sut.SaveChecklistAsync(actor, id,
            new PublicReviewChecklistDto { Safety = true, Image = false });

        var second = await _h.Sut.SaveChecklistAsync(actor, id, new PublicReviewChecklistDto
        {
            AgeFit = true,
            Education = true,
            Language = true,
            Copyright = true,
            Image = true,
            Audio = true,
            Interaction = true
        });

        Assert.True(first.Safety);
        Assert.False(first.Image);
        Assert.Null(first.Language);
        Assert.True(second.AllPassed);
        Assert.Equal(8, PublicReviewState.GetChecklist(_h.Items[0].Checklist).Count);
        _h.Audit.Verify(a => a.LogAsync(actor, "PublicStoryReviewChecklistSaved", nameof(ModerationItem), id,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SaveChecklistAsync_NoCriterion_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.SaveChecklistAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new PublicReviewChecklistDto()));
    }

    [Fact]
    public void Controller_IsRestrictedAndExposesEightEndpoints()
    {
        var authorize = typeof(PublicStoryReviewController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(PublicStoryReviewController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(
            new[]
            {
                "Approve", "Compare", "GetPackage", "MarkViewed", "Reject", "RequestChanges", "ResolveFlag",
                "SaveChecklist"
            },
            methods);
    }

    [Fact]
    public async Task GetPackageAsync_EverythingDone_CanApprove()
    {
        _h.MakeApprovable();

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.True(package.CanApprove);
        Assert.Empty(package.ApproveBlockers);
    }

    [Fact]
    public async Task GetPackageAsync_IncompleteWork_ListsStableApproveBlockers()
    {
        _h.AddScene(1, 1);
        _h.Versions[0].Moderation.GrayZoneFlags = "{\"sections\":[\"development\"]}";

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.False(package.CanApprove);
        Assert.Equal(
            new[] { "views_incomplete", "flags_unresolved", "checklist_incomplete" },
            package.ApproveBlockers.Select(blocker => blocker.Code));
        Assert.Contains("An toàn", package.ApproveBlockers.Single(
            blocker => blocker.Code == ApproveBlockerCodes.ChecklistIncomplete).Message);
    }

    [Theory]
    [InlineData(StoryStatus.Rejected, "story_archived")]
    [InlineData(StoryStatus.MediaProcessing, "story_not_ready")]
    public async Task GetPackageAsync_InvalidStoryState_BlocksApprove(
        StoryStatus status, string expectedCode)
    {
        _h.MakeApprovable();
        _h.Stories[0].Status = status;

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal(new[] { expectedCode }, package.ApproveBlockers.Select(blocker => blocker.Code));
    }

    [Fact]
    public async Task GetPackageAsync_OwnContent_BlocksApprove()
    {
        _h.MakeApprovable();
        _h.Items[0].CreatedByUserId = PublicStoryReviewHarness.ModeratorId;

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal(new[] { "own_content" }, package.ApproveBlockers.Select(blocker => blocker.Code));
    }

    [Fact]
    public async Task ApproveAsync_AllConditionsMet_PublishesAndRecordsDecision()
    {
        _h.MakeApprovable();
        var before = DateTime.UtcNow;

        var result = await ApproveDecisionAsync();

        Assert.Equal("Approved", result.Decision);
        Assert.True(result.StoryPublished);
        Assert.Equal(ModerationItemStatus.Approved, _h.Items[0].Status);
        Assert.Equal(PublicStoryReviewHarness.ModeratorId, _h.Items[0].DecidedByUserId);
        Assert.Equal(VersionReviewStatus.Approved, _h.Versions[0].Moderation.ReviewStatus);
        Assert.True(_h.Stories[0].IsPublished);
        Assert.True(_h.Stories[0].PublishedAt >= before);
        Assert.True(_h.Stories[0].ChildVisibleAt >= before);
        Assert.Equal(StoryStatus.Ready, _h.Stories[0].Status);
        Assert.Equal(TagSource.Creator, Assert.Single(_h.StoryCategories).Source);
        Assert.Equal(1, _h.Items[0].RowVersion);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPackageAsync_MissingOrInactiveProposedLabels_BlocksApproval()
    {
        _h.MakeApprovable();
        _h.Items[0].ProposedContentCategoryIds = null;
        var missing = await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);
        Assert.Contains(missing.ApproveBlockers, x => x.Code == "no_labels");
        await Assert.ThrowsAsync<BadRequestException>(ApproveDecisionAsync);

        _h.Items[0].ProposedContentCategoryIds = "[5]";
        _h.ContentCategories[0].IsActive = false;
        var inactive = await _h.Sut.GetPackageAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);
        Assert.Contains(inactive.ApproveBlockers, x => x.Code == "labels_inactive");
    }

    [Fact]
    public async Task ApproveAsync_ReplacesLabelsButKeepsMatchingLink()
    {
        _h.MakeApprovable();
        var kept = new StoryCategory { Id = 1, StoryId = PublicStoryReviewHarness.StoryId, ContentCategoryId = 5, Source = TagSource.Ai };
        _h.StoryCategories.Add(kept);
        _h.StoryCategories.Add(new StoryCategory { Id = 2, StoryId = PublicStoryReviewHarness.StoryId, ContentCategoryId = 7, Source = TagSource.Moderator });
        _h.ContentCategories.Add(new ContentCategory { Id = 6, Code = "animals", IsActive = true });
        _h.Items[0].ProposedContentCategoryIds = "[5,6]";

        await ApproveDecisionAsync();

        Assert.Contains(kept, _h.StoryCategories);
        Assert.DoesNotContain(_h.StoryCategories, x => x.ContentCategoryId == 7);
        Assert.Equal(TagSource.Creator, _h.StoryCategories.Single(x => x.ContentCategoryId == 6).Source);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyPublished_KeepsOriginalDates()
    {
        _h.MakeApprovable();
        var original = DateTime.UtcNow.AddDays(-30);
        _h.Stories[0].IsPublished = true;
        _h.Stories[0].PublishedAt = original;
        _h.Stories[0].ChildVisibleAt = original;

        await ApproveDecisionAsync();

        Assert.Equal(original, _h.Stories[0].PublishedAt);
        Assert.Equal(original, _h.Stories[0].ChildVisibleAt);
    }

    [Fact]
    public async Task ApproveAsync_IncompleteViews_IsBadRequestBeforeTransaction()
    {
        _h.MakeApprovable();
        _h.Items[0].ReviewProgress = null;

        var exception = await Assert.ThrowsAsync<BadRequestException>(ApproveDecisionAsync);

        Assert.Contains("Chưa xem đủ phần bắt buộc", exception.Message);
        Assert.Equal(ModerationItemStatus.Claimed, _h.Items[0].Status);
        _h.UnitOfWork.Verify(unit => unit.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_OwnContent_IsForbidden()
    {
        _h.MakeApprovable();
        _h.Stories[0].AuthorUserId = PublicStoryReviewHarness.ModeratorId;

        await Assert.ThrowsAsync<ForbiddenException>(ApproveDecisionAsync);
    }

    [Fact]
    public async Task ApproveAsync_NotifiesSubmitterAndWritesAudit()
    {
        _h.MakeApprovable();

        await ApproveDecisionAsync();

        _h.Notifications.Verify(notification => notification.CreateAsync(
            PublicStoryReviewHarness.CreatorId,
            NotificationType.ContentModerationResult,
            It.Is<string?>(payload => payload != null && payload.Contains("\"decision\":\"approved\"")),
            It.IsAny<CancellationToken>()), Times.Once);
        _h.Audit.Verify(audit => audit.LogAsync(
            PublicStoryReviewHarness.ModeratorId,
            "PublicStoryReviewApproved",
            nameof(ModerationItem),
            PublicStoryReviewHarness.ItemId,
            It.IsAny<object?>(),
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_InactiveSubmitter_ApprovesWithoutNotification()
    {
        _h.MakeApprovable();
        _h.Users.Single(user => user.Id == PublicStoryReviewHarness.CreatorId).Status = AccountStatus.Suspended;

        await ApproveDecisionAsync();

        Assert.Equal(ModerationItemStatus.Approved, _h.Items[0].Status);
        _h.Notifications.Verify(notification => notification.CreateAsync(
            It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_NotificationFailure_RollsBack()
    {
        _h.MakeApprovable();
        _h.Notifications.Setup(notification => notification.CreateAsync(
                It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(ApproveDecisionAsync);

        _h.UnitOfWork.Verify(unit => unit.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(unit => unit.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RequestChangesAsync_ValidStructuredFeedback_RecordsDecision()
    {
        _h.AddScene(1, 1);

        var result = await RequestChangesDecisionAsync(ValidChanges());

        Assert.Equal("ChangesRequested", result.Decision);
        Assert.False(result.StoryPublished);
        Assert.Equal(ModerationItemStatus.ChangesRequested, _h.Items[0].Status);
        Assert.Equal(VersionReviewStatus.ChangesRequested, _h.Versions[0].Moderation.ReviewStatus);
        using var feedback = JsonDocument.Parse(_h.Versions[0].Moderation.ReviewFeedback!);
        Assert.Equal("Cần sửa phần mở đầu.", feedback.RootElement.GetProperty("generalNote").GetString());
        Assert.Equal("scene_text", feedback.RootElement.GetProperty("parts")[0]
            .GetProperty("partType").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestChangesAsync_WithoutGeneralNote_IsBadRequest(string note)
    {
        _h.AddScene(1, 1);
        var request = ValidChanges();
        request.GeneralNote = note;

        await Assert.ThrowsAsync<BadRequestException>(() => RequestChangesDecisionAsync(request));
    }

    [Fact]
    public async Task RequestChangesAsync_NoPartsOrTooManyParts_IsBadRequest()
    {
        _h.AddScene(1, 1);
        var noParts = ValidChanges();
        noParts.Parts.Clear();
        var tooMany = ValidChanges();
        tooMany.Parts = Enumerable.Range(0, 51)
            .Select(_ => new ChangeRequestPartDto { PartType = "metadata", Note = "x" }).ToList();

        await Assert.ThrowsAsync<BadRequestException>(() => RequestChangesDecisionAsync(noParts));
        await Assert.ThrowsAsync<BadRequestException>(() => RequestChangesDecisionAsync(tooMany));
    }

    [Theory]
    [InlineData("stop_point", 1)]
    [InlineData("character_card", 1)]
    [InlineData("summary", 1)]
    [InlineData("banana", 1)]
    [InlineData("scene_text", 999)]
    [InlineData("metadata", 5)]
    public async Task RequestChangesAsync_UnsupportedTypeOrInvalidPosition_IsBadRequest(
        string partType, int? partId)
    {
        _h.AddScene(1, 1);
        var request = ValidChanges();
        request.Parts[0] = new ChangeRequestPartDto
        { PartType = partType, PartId = partId, Note = "x" };

        await Assert.ThrowsAsync<BadRequestException>(() => RequestChangesDecisionAsync(request));
    }

    [Fact]
    public async Task RequestChangesAsync_AcceptsAllSupportedPositionTypes()
    {
        _h.AddScene(1, 1);
        _h.Quizzes.Add(new QuizItem
        { Id = 3, StoryVersionId = PublicStoryReviewHarness.VersionId, Question = "?" });
        _h.Vocabulary.Add(new StoryVocabulary
        {
            Id = 4,
            StoryVersionId = PublicStoryReviewHarness.VersionId,
            Term = "a",
            Definition = "b"
        });
        var request = new RequestChangesDto
        {
            GeneralNote = "Sửa nhiều phần.",
            Parts = new List<ChangeRequestPartDto>
            {
                new() { PartType = "scene_image", PartId = 1, Note = "ảnh" },
                new() { PartType = "scene_audio", PartId = 1, Note = "audio" },
                new() { PartType = "quiz", PartId = 3, Note = "quiz" },
                new() { PartType = "vocabulary", PartId = 4, Note = "từ" },
                new() { PartType = "metadata", Note = "metadata" }
            }
        };

        var result = await RequestChangesDecisionAsync(request);

        Assert.Equal("ChangesRequested", result.Decision);
    }

    [Fact]
    public async Task RejectAsync_RefundsTheCreatorsSubmissionCredit()
    {
        _h.AddScene(1, 1);
        var lot = _h.GivenSubmissionCreditPaidBy(PublicStoryReviewHarness.ItemId);

        await RejectDecisionAsync("Nội dung chưa phù hợp với độ tuổi.");

        Assert.Equal(5, lot.Remaining);
        Assert.NotNull(Assert.Single(_h.CreditUsages).RefundedAt);
    }

    [Fact]
    public async Task RequestChangesAsync_KeepsTheSubmissionCreditSpent()
    {
        _h.AddScene(1, 1);
        var lot = _h.GivenSubmissionCreditPaidBy(PublicStoryReviewHarness.ItemId);

        await RequestChangesDecisionAsync(ValidChanges());

        Assert.Equal(4, lot.Remaining);
        Assert.Null(Assert.Single(_h.CreditUsages).RefundedAt);
    }

    [Fact]
    public async Task RejectAsync_WithReason_RecordsDecisionWithoutUnpublishingStory()
    {
        _h.AddScene(1, 1);
        _h.Stories[0].IsPublished = true;
        _h.Stories[0].PublishedAt = DateTime.UtcNow.AddDays(-5);

        var result = await RejectDecisionAsync("Bản mới vi phạm bản quyền.");

        Assert.Equal("Rejected", result.Decision);
        Assert.True(result.StoryPublished);
        Assert.Equal(ModerationItemStatus.Rejected, _h.Items[0].Status);
        Assert.Equal(VersionReviewStatus.Rejected, _h.Versions[0].Moderation.ReviewStatus);
        Assert.Equal(StoryStatus.Ready, _h.Stories[0].Status);
        using var feedback = JsonDocument.Parse(_h.Versions[0].Moderation.ReviewFeedback!);
        Assert.Equal("Bản mới vi phạm bản quyền.", feedback.RootElement.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectAsync_WithoutReason_IsBadRequest(string reason)
    {
        _h.AddScene(1, 1);

        await Assert.ThrowsAsync<BadRequestException>(() => RejectDecisionAsync(reason));
        Assert.Equal(ModerationItemStatus.Claimed, _h.Items[0].Status);
    }

    [Fact]
    public async Task RejectAsync_TooLongReason_IsBadRequest()
    {
        _h.AddScene(1, 1);

        await Assert.ThrowsAsync<BadRequestException>(() => RejectDecisionAsync(new string('a', 1001)));
    }

    private Task<PublicReviewDecisionResultDto> ApproveDecisionAsync() =>
        _h.Sut.ApproveAsync(PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

    private Task<PublicReviewDecisionResultDto> RequestChangesDecisionAsync(RequestChangesDto request) =>
        _h.Sut.RequestChangesAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, request);

    private Task<PublicReviewDecisionResultDto> RejectDecisionAsync(string reason) =>
        _h.Sut.RejectAsync(
            PublicStoryReviewHarness.ModeratorId,
            PublicStoryReviewHarness.ItemId,
            new RejectRequestDto { Reason = reason });

    private static RequestChangesDto ValidChanges() => new()
    {
        GeneralNote = "Cần sửa phần mở đầu.",
        Parts = new List<ChangeRequestPartDto>
        {
            new() { PartType = "scene_text", PartId = 1, Note = "Viết rõ hơn." }
        }
    };

    [Fact]
    public void StateUpdates_PreserveUnknownJsonProperties()
    {
        var progress = PublicReviewState.AddViewed("{\"future\":1}", "scene", 2);
        var flags = PublicReviewState.AddResolvedFlag("{\"future\":1}",
            new ResolvedFlag("section:x", "ổn", 11, DateTime.UtcNow));
        var checklist = PublicReviewState.MergeChecklist("{\"future\":1}",
            new Dictionary<string, bool> { ["safety"] = true });

        Assert.Contains("\"future\":1", progress);
        Assert.Contains("\"future\":1", flags);
        Assert.Contains("\"future\":1", checklist);
    }

    [Fact]
    public async Task GetPackageAsync_Resubmission_ReturnsPreviousModeratorAndReviewHistory()
    {
        var previousVersion = _h.CloneVersion(PublicStoryReviewHarness.VersionId, 49);
        var previous = new ModerationItem
        {
            Id = 90,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.ChangesRequested,
            StoryId = PublicStoryReviewHarness.StoryId,
            StoryVersionId = previousVersion.Id,
            CreatedByUserId = PublicStoryReviewHarness.CreatorId,
            DecidedByUserId = PublicStoryReviewHarness.ModeratorId,
            DecidedAt = DateTime.UtcNow.AddDays(-1),
            DecisionReason = "Cần sửa phần kết.",
            Feedback = "{\"parts\":[]}"
        };
        _h.Items.Add(previous);
        _h.Items[0].IsResubmission = true;
        _h.Items[0].PreviousItemId = previous.Id;

        var package = await _h.Sut.GetPackageAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId);

        Assert.Equal("Nguyen Thi Lan", package.PreviousModeratorName);
        var history = Assert.Single(package.ReviewHistory);
        Assert.Equal(previous.Id, history.ItemId);
        Assert.Equal("ChangesRequested", history.Decision);
        Assert.Equal("Nguyen Thi Lan", history.DecidedByName);
        Assert.Equal("Cần sửa phần kết.", history.Reason);
    }

    [Fact]
    public async Task CompareAsync_Previous_ReturnsDiffAndWritesAudit()
    {
        var previousVersion = _h.CloneVersion(PublicStoryReviewHarness.VersionId, 49);
        previousVersion.Content = "Mở đầu cũ. Kết thúc.";
        var previous = new ModerationItem
        {
            Id = 90,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Rejected,
            StoryId = PublicStoryReviewHarness.StoryId,
            StoryVersionId = previousVersion.Id,
            CreatedByUserId = PublicStoryReviewHarness.CreatorId,
            DecidedByUserId = PublicStoryReviewHarness.ModeratorId,
            DecidedAt = DateTime.UtcNow.AddHours(-2)
        };
        _h.Items.Add(previous);
        _h.Items[0].IsResubmission = true;
        _h.Items[0].PreviousItemId = previous.Id;

        var result = await _h.Sut.CompareAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, "previous");

        Assert.Equal("previous", result.Against);
        Assert.Equal(previousVersion.Id, result.BaseVersionId);
        Assert.False(result.IsIdentical);
        _h.Audit.Verify(audit => audit.LogAsync(
            PublicStoryReviewHarness.ModeratorId, "PublicStoryReviewCompared", nameof(ModerationItem),
            PublicStoryReviewHarness.ItemId, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompareAsync_Public_UsesLatestApprovedVersion()
    {
        var older = _h.CloneVersion(PublicStoryReviewHarness.VersionId, 48);
        older.Moderation.ReviewStatus = VersionReviewStatus.Approved;
        older.Moderation.ReviewedAt = DateTime.UtcNow.AddDays(-2);
        var latest = _h.CloneVersion(PublicStoryReviewHarness.VersionId, 49);
        latest.Moderation.ReviewStatus = VersionReviewStatus.Approved;
        latest.Moderation.ReviewedAt = DateTime.UtcNow.AddDays(-1);

        var result = await _h.Sut.CompareAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, "public");

        Assert.Equal(latest.Id, result.BaseVersionId);
        Assert.Equal("public", result.Against);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("other")]
    public async Task CompareAsync_InvalidTarget_IsBadRequest(string? against)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.CompareAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, against));
    }

    [Fact]
    public async Task CompareAsync_PreviousWithoutPreviousItem_IsBadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.CompareAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId, "previous"));
    }

    private void GivenTwoFlags()
    {
        _h.Versions[0].Moderation.GrayZoneFlags =
            "{\"sections\":[\"development\",\"ending\"],\"reason\":\"safety_review\"}";
    }

    private void GivenCategories()
    {
        _h.ContentCategories.Clear();
        _h.ContentCategories.Add(new ContentCategory
        { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory
        { Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory
        { Id = 7, Code = "old", DisplayName = "Cũ", IsActive = false });
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 1,
            StoryId = PublicStoryReviewHarness.StoryId,
            ContentCategoryId = 5,
            Source = TagSource.Ai
        });
    }
}
