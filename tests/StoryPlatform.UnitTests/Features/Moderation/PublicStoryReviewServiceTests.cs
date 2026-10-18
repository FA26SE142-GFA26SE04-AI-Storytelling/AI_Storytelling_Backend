using System.Reflection;
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
    public async Task GetPackageAsync_ClaimedByActor_ReturnsFullSubmittedVersion()
    {
        _h.AddScene(1, 1);
        _h.Quizzes.Add(new QuizItem
        {
            Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId,
            Type = QuizType.MultipleChoice, Question = "Ai thắng?", CorrectAnswer = "Rùa",
            Choices = "[\"Rùa\",\"Thỏ\"]"
        });
        _h.Vocabulary.Add(new StoryVocabulary
        {
            Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId,
            Term = "kiên nhẫn", Definition = "bền bỉ chờ đợi"
        });
        _h.Discussions.Add(new DiscussionQuestion
        {
            Id = 1, StoryVersionId = PublicStoryReviewHarness.VersionId,
            Question = "Vì sao rùa thắng?", IsMoralLesson = true
        });
        _h.ContentCategories.Add(new ContentCategory
            { Id = 5, Code = "friendship", DisplayName = "Tình bạn" });
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 1, StoryId = PublicStoryReviewHarness.StoryId,
            ContentCategoryId = 5, Source = TagSource.Ai
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
        Assert.True(package.DiscussionQuestions.Single().IsMoralLesson);
        Assert.Equal("friendship", package.Story.Labels.Single().Code);
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
        _h.Versions[0].GrayZoneFlags =
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
        Assert.Equal(actor, _h.Versions[0].GrayZoneClearedByUserId);
        Assert.NotNull(_h.Versions[0].GrayZoneClearedAt);
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
            AgeFit = true, Education = true, Language = true, Copyright = true,
            Image = true, Audio = true, Interaction = true
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
    public async Task SetContentLabelsAsync_ReplacesLabelsAndMarksNewSource()
    {
        GivenCategories();
        var before = _h.Items[0].RowVersion;

        var labels = await _h.Sut.SetContentLabelsAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new SetContentLabelsRequestDto { ContentCategoryIds = new List<int> { 6, 6 } });

        Assert.Single(labels);
        Assert.Equal(6, labels[0].ContentCategoryId);
        Assert.Equal("Moderator", labels[0].Source);
        Assert.Single(_h.StoryCategories);
        Assert.Equal(before + 1, _h.Items[0].RowVersion);
        _h.Audit.Verify(a => a.LogAsync(
            PublicStoryReviewHarness.ModeratorId, "PublicStoryReviewLabelsChanged", nameof(ModerationItem),
            PublicStoryReviewHarness.ItemId, It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(999)]
    public async Task SetContentLabelsAsync_InactiveOrUnknown_IsBadRequest(int categoryId)
    {
        GivenCategories();

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.SetContentLabelsAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new SetContentLabelsRequestDto { ContentCategoryIds = new List<int> { categoryId } }));
    }

    [Fact]
    public async Task SetContentLabelsAsync_EmptySet_IsBadRequest()
    {
        GivenCategories();

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Sut.SetContentLabelsAsync(
            PublicStoryReviewHarness.ModeratorId, PublicStoryReviewHarness.ItemId,
            new SetContentLabelsRequestDto()));
    }

    [Fact]
    public void Controller_IsRestrictedAndExposesFiveEndpoints()
    {
        var authorize = typeof(PublicStoryReviewController).GetCustomAttribute<AuthorizeAttribute>();
        var methods = typeof(PublicStoryReviewController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(new[] { "GetPackage", "MarkViewed", "ResolveFlag", "SaveChecklist", "SetLabels" }, methods);
    }

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

    private void GivenTwoFlags()
    {
        _h.Versions[0].GrayZoneFlags =
            "{\"sections\":[\"development\",\"ending\"],\"reason\":\"safety_review\"}";
    }

    private void GivenCategories()
    {
        _h.ContentCategories.Add(new ContentCategory
            { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory
            { Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true });
        _h.ContentCategories.Add(new ContentCategory
            { Id = 7, Code = "old", DisplayName = "Cũ", IsActive = false });
        _h.StoryCategories.Add(new StoryCategory
        {
            Id = 1, StoryId = PublicStoryReviewHarness.StoryId,
            ContentCategoryId = 5, Source = TagSource.Ai
        });
    }
}
