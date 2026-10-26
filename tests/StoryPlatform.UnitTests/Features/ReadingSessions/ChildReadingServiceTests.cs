using Moq;
using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Services;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence.Configurations;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.UnitTests.Features.ChildRoadmaps;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class HoldModeLookupTests
{
    private readonly ReadingHarness _h = new();

    [Fact]
    public async Task FindOpenAsync_NoCase_ReturnsNull()
    {
        Assert.Null(await HoldModeLookup.FindOpenAsync(_h.UnitOfWork.Object, ReadingHarness.Child));
    }

    [Fact]
    public async Task FindOpenAsync_ResolvedCase_ReturnsNull()
    {
        _h.AddHoldMode(ReadingHarness.Child, InterventionStatus.ResolvedUnlocked, triggeringStoryId: 10);

        Assert.Null(await HoldModeLookup.FindOpenAsync(_h.UnitOfWork.Object, ReadingHarness.Child));
    }

    [Fact]
    public async Task FindOpenAsync_OpenCaseOfAnotherChild_ReturnsNull()
    {
        _h.AddHoldMode(ReadingHarness.OtherChild, InterventionStatus.OpenHoldMode, triggeringStoryId: 10);

        Assert.Null(await HoldModeLookup.FindOpenAsync(_h.UnitOfWork.Object, ReadingHarness.Child));
    }

    [Fact]
    public async Task FindOpenAsync_OpenCase_ReturnsIt()
    {
        var added = _h.AddHoldMode(ReadingHarness.Child, InterventionStatus.UnderReview, triggeringStoryId: 10);

        var found = await HoldModeLookup.FindOpenAsync(_h.UnitOfWork.Object, ReadingHarness.Child);

        Assert.Equal(added.Id, found!.Id);
    }

    [Fact]
    public async Task FindOpenAsync_DeletedCase_ReturnsNull()
    {
        _h.AddHoldMode(ReadingHarness.Child).IsDeleted = true;

        Assert.Null(await HoldModeLookup.FindOpenAsync(_h.UnitOfWork.Object, ReadingHarness.Child));
    }
}

public class ChildReadingServiceTests
{
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildRoadmapEligibility> _eligibility = new();
    private readonly Mock<IChildRoadmapProgressRecorder> _recorder = new();
    private readonly Mock<IChildRewardService> _rewards = new();
    private readonly ChildReadingService _sut;

    public ChildReadingServiceTests()
    {
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChildRoadmapSkipReason?)null);
        _rewards.Setup(r => r.OnStoryCompletedAsync(It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewardOutcome.None);
        _sut = new ChildReadingService(_h.UnitOfWork.Object, _eligibility.Object, _recorder.Object, _rewards.Object);
    }

    private static StartReadingRequestDto Start(int storyId = ReadingHarness.Story) =>
        new() { StoryId = storyId };

    // ---- StartAsync ----

    [Fact]
    public async Task StartAsync_CreatesSessionWithEntrySourceFromChildSession()
    {
        var result = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        var session = _h.Sessions.Single();
        Assert.False(result.Resumed);
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal(ReadingHarness.CredentialId, session.ChildAccessCredentialId);
        Assert.Null(session.SupervisorSessionId);
        Assert.Equal(ReadingHarness.Version, session.StoryVersionId);
        Assert.Equal(SessionStatus.Started, session.Status);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.SessionStarted);
        Assert.Equal(1, result.LastPage);
    }

    [Fact]
    public async Task StartAsync_UnfinishedSessionOfSameStory_IsResumed()
    {
        var existing = _h.AddSession(SessionStatus.Reading);
        _h.Progress.Add(new ReadingProgress
            { Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story, LastPageRead = 4 });

        var result = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        Assert.True(result.Resumed);
        Assert.Equal(existing.Id, result.SessionId);
        Assert.Equal(4, result.LastPage);
        Assert.Null(result.LastReadingMode);
        Assert.Single(_h.Sessions);
        Assert.Empty(_h.Telemetry);
    }

    [Fact]
    public async Task StartAsync_SupervisorHandoff_CopiesEntrySourceAndProgressFlags()
    {
        var childSession = _h.ChildSessions.Single();
        childSession.ChildAccessCredentialId = null;
        childSession.SupervisorSessionId = 123;
        childSession.EntryMethod = ChildEntryMethod.ProfileSwitcher;
        _h.Progress.Add(new ReadingProgress
        {
            ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story,
            LastPageRead = 3, IsBookmarked = true, IsFavorited = true
        });

        var result = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        var session = Assert.Single(_h.Sessions);
        Assert.Equal(123, session.SupervisorSessionId);
        Assert.Null(session.ChildAccessCredentialId);
        Assert.Equal(3, result.LastPage);
        Assert.True(result.IsBookmarked);
        Assert.True(result.IsFavorited);
        Assert.Single(_h.Progress);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("suspended")]
    public async Task StartAsync_UnavailableProfile_DoesNotCreateSession(string state)
    {
        var child = _h.Children.Single(c => c.Id == ReadingHarness.Child);
        if (state == "missing") _h.Children.Remove(child);
        if (state == "deleted") child.IsDeleted = true;
        if (state == "suspended") child.Status = ChildProfileStatus.Suspended;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start()));

        Assert.Empty(_h.Sessions);
        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("expired")]
    [InlineData("another-child")]
    [InlineData("no-entry-source")]
    [InlineData("two-entry-sources")]
    public async Task StartAsync_InvalidChildSession_IsForbidden(string state)
    {
        var session = _h.ChildSessions.Single();
        if (state == "deleted") session.IsDeleted = true;
        if (state == "expired") session.LastActivityAt = DateTime.UtcNow - ChildSession.IdleTimeout;
        if (state == "another-child") session.ChildProfileId = ReadingHarness.OtherChild;
        if (state == "no-entry-source") session.ChildAccessCredentialId = null;
        if (state == "two-entry-sources") session.SupervisorSessionId = 123;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start()));

        Assert.Empty(_h.Sessions);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Abandoned)]
    [InlineData(SessionStatus.ForceExited)]
    public async Task StartAsync_TerminalSession_IsNotResumed(SessionStatus status)
    {
        var previous = _h.AddSession(status);

        var result = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        Assert.False(result.Resumed);
        Assert.NotEqual(previous.Id, result.SessionId);
        Assert.Equal(status, previous.Status);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("force-exit-requested")]
    [InlineData("other-version")]
    [InlineData("other-child")]
    public async Task StartAsync_IneligibleOpenSession_IsNotResumed(string state)
    {
        var previous = _h.AddSession();
        if (state == "deleted") previous.IsDeleted = true;
        if (state == "force-exit-requested") previous.ForceExitRequestedAt = DateTime.UtcNow;
        if (state == "other-version") previous.StoryVersionId = 60;
        if (state == "other-child") previous.ChildProfileId = ReadingHarness.OtherChild;

        var result = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        Assert.False(result.Resumed);
        Assert.NotEqual(previous.Id, result.SessionId);
    }

    [Fact]
    public async Task StartAsync_RepeatedRequest_ResumesWithoutDuplicateProgressOrTelemetry()
    {
        var first = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());
        var second = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.True(second.Resumed);
        Assert.Single(_h.Sessions);
        Assert.Single(_h.Progress);
        Assert.Equal(first.SessionId, Assert.Single(_h.Telemetry).ReadingSessionId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("another-story")]
    public async Task StartAsync_InvalidPinnedVersion_IsForbidden(string state)
    {
        _h.ModerationItems.Add(new ModerationItem
        {
            Id = 5, ItemType = ModerationItemType.CommunitySubmission, Status = ModerationItemStatus.Approved,
            StoryId = ReadingHarness.Story, StoryVersionId = ReadingHarness.Version
        });
        if (state == "missing") _h.Versions.Clear();
        if (state == "deleted") _h.Versions.Single().IsDeleted = true;
        if (state == "another-story") _h.Versions.Single().StoryId = 999;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.StartAsync(
            ReadingHarness.Child, ReadingHarness.SessionKey, new StartReadingRequestDto { CommunitySubmissionId = 5 }));

        Assert.Equal(ChildReadingPolicy.StoryUnavailableMessage, error.Message);
        Assert.Empty(_h.Sessions);
    }

    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Rejected)]
    [InlineData(ModerationItemStatus.Removed)]
    public async Task StartAsync_UnapprovedCommunitySubmission_IsForbidden(ModerationItemStatus status)
    {
        _h.ModerationItems.Add(new ModerationItem
        {
            Id = 5, ItemType = ModerationItemType.CommunitySubmission, Status = status,
            StoryId = ReadingHarness.Story, StoryVersionId = ReadingHarness.Version
        });

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.StartAsync(
            ReadingHarness.Child, ReadingHarness.SessionKey, new StartReadingRequestDto { CommunitySubmissionId = 5 }));
        Assert.Empty(_h.Sessions);
    }

    [Theory]
    [InlineData(20, 2, true)]
    [InlineData(2, 20, false)]
    public async Task StartAsync_CommunitySafetyChecksPinnedContent(int pinnedLength, int currentLength, bool denied)
    {
        var story = _h.Stories.Single();
        story.ChildProfileId = ReadingHarness.OtherChild;
        story.Content = string.Join(' ', Enumerable.Repeat("a", currentLength));
        _h.Versions.Single().Content = string.Join(' ', Enumerable.Repeat("b", pinnedLength));
        _h.Policies.Add(new SafetyPolicy { Id = 1, ChildProfileId = ReadingHarness.Child, MaxStoryLength = 10 });
        _h.ModerationItems.Add(new ModerationItem
        {
            Id = 5, ItemType = ModerationItemType.CommunitySubmission, Status = ModerationItemStatus.Approved,
            StoryId = ReadingHarness.Story, StoryVersionId = ReadingHarness.Version
        });
        var service = new ChildReadingService(_h.UnitOfWork.Object,
            new ChildRoadmapEligibility(_h.UnitOfWork.Object), _recorder.Object, _rewards.Object);
        var request = new StartReadingRequestDto { CommunitySubmissionId = 5 };

        if (denied)
        {
            var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, request));
            Assert.Equal(ChildReadingPolicy.StoryUnavailableMessage, error.Message);
            Assert.Empty(_h.Sessions);
        }
        else
        {
            var result = await service.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, request);
            Assert.Equal(ReadingHarness.Story, result.StoryId);
        }

        Assert.Equal(currentLength, StoryPlatform.Contracts.AI.Models.StoryWordCounter.Count(story.Content));
    }

    [Fact]
    public async Task StartAsync_TelemetrySaveFails_RollsBackSessionAndProgressTransaction()
    {
        _h.UnitOfWork.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1).ThrowsAsync(new InvalidOperationException("Save failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start()));

        _h.UnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_AnotherChildsPrivateStory_IsForbidden()
    {
        _h.Stories.Single().ChildProfileId = ReadingHarness.OtherChild;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start()));

        Assert.Equal(ChildReadingPolicy.StoryUnavailableMessage, error.Message);
    }

    [Theory]
    [InlineData(ChildRoadmapItemStatus.Waiting, true)]
    [InlineData(ChildRoadmapItemStatus.Completed, true)]
    [InlineData(ChildRoadmapItemStatus.Removed, false)]
    public async Task StartAsync_ParentAssignedSiblingStoryOfAnotherAge_UsesTheAssignment(
        ChildRoadmapItemStatus status, bool allowed)
    {
        var story = _h.Stories.Single();
        story.ChildProfileId = ReadingHarness.OtherChild; story.AgeBand = "9-10";
        _h.RoadmapItems.Add(new ChildRoadmapItem { Id = 7, ChildProfileId = ReadingHarness.Child,
            StoryId = story.Id, Source = ChildRoadmapItemSource.Manual, Status = status });
        var service = new ChildReadingService(_h.UnitOfWork.Object,
            new ChildRoadmapEligibility(_h.UnitOfWork.Object), _recorder.Object, _rewards.Object);
        if (allowed)
            Assert.Equal(story.Id, (await service.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start())).StoryId);
        else
            await Assert.ThrowsAsync<ForbiddenException>(() => service.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start()));
    }

    [Fact]
    public async Task StartAsync_HoldModeOnAnotherStory_IsLocked_ButTriggeringStoryOpens()
    {
        _h.Stories.Add(new Story
        {
            Id = 11, Title = "Truyện 11", Scope = StoryScope.Parent, AgeBand = "6-8", Status = StoryStatus.Ready,
            AuthorUserId = 7, ChildProfileId = ReadingHarness.Child
        });
        _h.Versions.Add(new StoryVersion { Id = 51, StoryId = 11, VersionNo = 1, IsCurrent = true });
        _h.AddHoldMode(ReadingHarness.Child, triggeringStoryId: 11);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start(ReadingHarness.Story)));
        var allowed = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start(11));

        Assert.Equal(ChildReadingPolicy.LibraryLockedMessage, error.Message);
        Assert.Equal(11, allowed.StoryId);
    }

    [Fact]
    public async Task StartAsync_EligibilityRejects_IsForbiddenWithoutReason()
    {
        _eligibility.Setup(e => e.EvaluateAsync(It.IsAny<ChildProfile>(), It.IsAny<Story?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChildRoadmapSkipReason.BlockedTopic);

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start()));

        Assert.Equal(ChildReadingPolicy.StoryUnavailableMessage, error.Message);
    }

    [Fact]
    public async Task StartAsync_UnknownChildSession_IsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.StartAsync(ReadingHarness.Child, "khong-co", Start()));
    }

    [Fact]
    public async Task StartAsync_CommunitySubmission_UsesPinnedVersionAndHidesNothingElse()
    {
        _h.Stories.Single().Scope = StoryScope.Creator;
        _h.Stories.Single().ChildProfileId = null;
        _h.Versions.Add(new StoryVersion { Id = 60, StoryId = 10, VersionNo = 2, IsCurrent = false });
        _h.ModerationItems.Add(new ModerationItem
        {
            Id = 5, ItemType = ModerationItemType.CommunitySubmission, Status = ModerationItemStatus.Approved,
            StoryId = 10, StoryVersionId = 60
        });

        var result = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey,
            new StartReadingRequestDto { CommunitySubmissionId = 5 });

        Assert.Equal(60, _h.Sessions.Single().StoryVersionId);
        Assert.Equal(10, result.StoryId);
    }

    [Fact]
    public async Task StartAsync_RequestNeedsExactlyOneTarget()
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, new StartReadingRequestDto()));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey,
                new StartReadingRequestDto { StoryId = 10, CommunitySubmissionId = 5 }));
    }

    // ---- GetContentAsync ----

    [Fact]
    public async Task GetContentAsync_ReturnsScenesSegmentsAudioAndIllustrations()
    {
        var session = _h.AddSession();
        var scene1 = _h.AddScene(0, "Thỏ con ra vườn. Thỏ thấy cà rốt.");
        var scene2 = _h.AddScene(1, "Thỏ chia cà rốt.");
        var seg = _h.AddSegment(scene1, 1, "Thỏ con ra vườn.");
        _h.AddSegment(scene1, 2, "Thỏ thấy cà rốt.");
        _h.AddAudio(seg, "https://cdn.test/a1.mp3", "[{\"w\":\"Thỏ\",\"t\":0}]");
        _h.AddIllustration(scene2, "https://cdn.test/i2.png");
        _h.Assets.Add(new MediaAsset
        {
            Id = 99, StoryVersionId = ReadingHarness.Version, StorySegmentId = seg.Id, Type = MediaType.TtsAudio,
            Status = MediaStatus.Failed, Url = "https://cdn.test/failed.mp3"
        });
        _h.AddVocabulary(1, "cà rốt");

        var content = await _sut.GetContentAsync(ReadingHarness.Child, session.Id);

        Assert.Equal("Truyện 10", content.Title);
        Assert.Equal("Biết chia sẻ", content.Lesson);
        Assert.Equal(new[] { 0, 1 }, content.Scenes.Select(s => s.SceneIndex));
        Assert.Equal(2, content.Scenes[0].Segments.Count);
        Assert.Equal("https://cdn.test/a1.mp3", content.Scenes[0].Segments[0].AudioUrl);
        Assert.Equal("[{\"w\":\"Thỏ\",\"t\":0}]", content.Scenes[0].Segments[0].WordTimingsJson);
        Assert.Null(content.Scenes[0].Segments[1].AudioUrl);
        Assert.Equal("https://cdn.test/i2.png", content.Scenes[1].IllustrationUrl);
        Assert.Equal("cà rốt", content.Vocabulary.Single().Term);
    }

    [Fact]
    public async Task GetContentAsync_AnotherChildsSession_IsNotFound()
    {
        var session = _h.AddSession(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetContentAsync(ReadingHarness.Child, session.Id));
    }

    [Fact]
    public async Task GetContentAsync_ForceExitedSession_IsForbidden()
    {
        var session = _h.AddSession(SessionStatus.ForceExited);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetContentAsync(ReadingHarness.Child, session.Id));
    }

    // ---- SaveProgressAsync ----

    [Fact]
    public async Task SaveProgressAsync_StoresPageModeAndTime_AndMovesSessionToReading()
    {
        var session = _h.AddSession(SessionStatus.Started);
        _h.AddScene(0, "a"); _h.AddScene(1, "b"); _h.AddScene(2, "c");

        var result = await _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 3, ReadingMode = "ReadFirst", AddedSeconds = 45 });

        var progress = _h.Progress.Single();
        Assert.Equal(3, progress.LastPageRead);
        Assert.Equal("ReadFirst", progress.LastReadingMode);
        Assert.Equal(SessionStatus.Reading, session.Status);
        Assert.Equal(45, session.TimeSpentSeconds);
        Assert.Equal(2, session.PagesCompleted);
        Assert.Equal(3, result.Page);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.PageViewed);
    }

    [Fact]
    public async Task SaveProgressAsync_ClampsAddedSeconds()
    {
        var session = _h.AddSession();
        _h.AddScene(0, "a");

        await _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1, AddedSeconds = 99999 });
        await _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1, AddedSeconds = -50 });

        Assert.Equal(ChildReadingPolicy.MaxAddedSecondsPerSave, session.TimeSpentSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task SaveProgressAsync_PageOutsideStory_IsBadRequest(int page)
    {
        var session = _h.AddSession();
        _h.AddScene(0, "a"); _h.AddScene(1, "b"); _h.AddScene(2, "c");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SaveProgressAsync(ReadingHarness.Child, session.Id, new SaveProgressRequestDto { Page = page }));
    }

    [Fact]
    public async Task SaveProgressAsync_UnknownReadingMode_IsBadRequest()
    {
        var session = _h.AddSession();
        _h.AddScene(0, "a");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
                new SaveProgressRequestDto { Page = 1, ReadingMode = "Abc" }));
    }

    [Fact]
    public async Task SaveProgressAsync_CompletedSession_IsConflict()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        _h.AddScene(0, "a");

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SaveProgressAsync(ReadingHarness.Child, session.Id, new SaveProgressRequestDto { Page = 1 }));
    }

    [Fact]
    public void ReadingProgress_MapsNullableReadingModeWithMaxLength30()
    {
        var model = new ModelBuilder();
        new ReadingProgressConfiguration().Configure(model.Entity<ReadingProgress>());

        var property = model.Model.FindEntityType(typeof(ReadingProgress))!.FindProperty(nameof(ReadingProgress.LastReadingMode))!;

        Assert.True(property.IsNullable);
        Assert.Equal(30, property.GetMaxLength());
    }

    [Fact]
    public void ReadingSession_MapsNullableFeelingAsStringWithMaxLength20()
    {
        var model = new ModelBuilder();
        new ReadingSessionConfiguration().Configure(model.Entity<ReadingSession>());

        var property = model.Model.FindEntityType(typeof(ReadingSession))!.FindProperty(nameof(ReadingSession.Feeling))!;

        Assert.True(property.IsNullable);
        Assert.Equal(20, property.GetMaxLength());
        Assert.Equal(typeof(string), property.GetProviderClrType());
    }

    [Theory]
    [InlineData(StoryFeeling.VeryLike, "Rất thích")]
    [InlineData(StoryFeeling.Like, "Thích")]
    [InlineData(StoryFeeling.Neutral, "Bình thường")]
    [InlineData(StoryFeeling.Dislike, "Chưa thích")]
    public void StoryFeelingLabels_UseTheDocumentedVietnameseNames(StoryFeeling feeling, string expected)
    {
        Assert.Equal(expected, StoryFeelingLabels.Label(feeling));
    }

    [Theory]
    [InlineData("VeryLike", StoryFeeling.VeryLike)]
    [InlineData("verylike", StoryFeeling.VeryLike)]
    [InlineData(" Like ", StoryFeeling.Like)]
    [InlineData("NEUTRAL", StoryFeeling.Neutral)]
    [InlineData("Dislike", StoryFeeling.Dislike)]
    public void StoryFeelingLabels_TryParse_AcceptsNamesIgnoringCase(string text, StoryFeeling expected)
    {
        Assert.True(StoryFeelingLabels.TryParse(text, out var feeling));
        Assert.Equal(expected, feeling);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Love")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("99")]
    public void StoryFeelingLabels_TryParse_RejectsUnknownTextAndNumbers(string? text)
    {
        Assert.False(StoryFeelingLabels.TryParse(text, out _));
    }

    // ---- SetFeelingAsync ----

    [Fact]
    public async Task SetFeelingAsync_FinishedSession_StoresAndEchoesTheFeeling()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        var result = await _sut.SetFeelingAsync(ReadingHarness.Child, session.Id,
            new SetFeelingRequestDto { Feeling = "VeryLike" });

        Assert.Equal(StoryFeeling.VeryLike, session.Feeling);
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal("VeryLike", result.Feeling);
        Assert.Equal("Rất thích", result.Label);
    }

    [Fact]
    public async Task SetFeelingAsync_ChoosingAgain_ReplacesTheEarlierChoice()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        await _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = "Like" });
        var second = await _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = "dislike" });

        Assert.Equal(StoryFeeling.Dislike, session.Feeling);
        Assert.Equal("Chưa thích", second.Label);
    }

    [Fact]
    public async Task SetFeelingAsync_GivesNoRewardAndTouchesNoRoadmap()
    {
        var session = _h.AddSession(SessionStatus.Completed);

        await _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = "Like" });

        Assert.Empty(_h.Telemetry);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Love")]
    [InlineData("99")]
    public async Task SetFeelingAsync_UnknownFeeling_IsBadRequestAndStoresNothing(string? feeling)
    {
        var session = _h.AddSession(SessionStatus.Completed);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = feeling }));

        Assert.Null(session.Feeling);
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Reading)]
    [InlineData(SessionStatus.Activity)]
    [InlineData(SessionStatus.Abandoned)]
    public async Task SetFeelingAsync_SessionNotFinished_IsConflict(SessionStatus status)
    {
        var session = _h.AddSession(status);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = "Like" }));

        Assert.Equal(ChildReadingPolicy.FeelingAfterReadingMessage, error.Message);
        Assert.Null(session.Feeling);
    }

    [Fact]
    public async Task SetFeelingAsync_AnotherChildsSession_IsNotFound()
    {
        var session = _h.AddSession(SessionStatus.Completed, childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = "Like" }));

        Assert.Null(session.Feeling);
    }

    [Fact]
    public async Task SetFeelingAsync_InterruptedSession_IsForbiddenWithFriendlyMessage()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SetFeelingAsync(ReadingHarness.Child, session.Id, new SetFeelingRequestDto { Feeling = "Like" }));

        Assert.Equal(ReadingSessionInterruptPolicy.ChildMessage, error.Message);
        Assert.Null(session.Feeling);
    }

    [Theory]
    [InlineData("missing-session")]
    [InlineData("deleted-session")]
    [InlineData("other-child")]
    public async Task ContentAndProgress_InaccessibleSession_IsNotFound(string state)
    {
        var session = _h.AddSession();
        if (state == "missing-session") _h.Sessions.Clear();
        if (state == "deleted-session") session.IsDeleted = true;
        if (state == "other-child") session.ChildProfileId = ReadingHarness.OtherChild;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetContentAsync(ReadingHarness.Child, session.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SaveProgressAsync(
            ReadingHarness.Child, session.Id, new SaveProgressRequestDto { Page = 1 }));

        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("deleted-version")]
    [InlineData("no-version")]
    [InlineData("other-story")]
    public async Task ContentAndProgress_InvalidPinnedVersion_IsForbidden(string state)
    {
        var session = _h.AddSession();
        if (state == "missing-version") _h.Versions.Clear();
        if (state == "deleted-version") _h.Versions.Single().IsDeleted = true;
        if (state == "no-version") session.StoryVersionId = null;
        if (state == "other-story") _h.Versions.Single().StoryId = 99;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetContentAsync(ReadingHarness.Child, session.Id));
        Assert.Equal(ChildReadingPolicy.StoryUnavailableMessage, error.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SaveProgressAsync(
            ReadingHarness.Child, session.Id, new SaveProgressRequestDto { Page = 1 }));
        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentAndProgress_InterruptedSession_IsForbiddenWithoutReason(bool alreadyExited)
    {
        var session = _h.AddSession(alreadyExited ? SessionStatus.ForceExited : SessionStatus.Reading);
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var contentError = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GetContentAsync(ReadingHarness.Child, session.Id));
        var progressError = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SaveProgressAsync(
            ReadingHarness.Child, session.Id, new SaveProgressRequestDto { Page = 1, AddedSeconds = 50 }));

        Assert.Equal(ReadingSessionInterruptPolicy.ChildMessage, contentError.Message);
        Assert.Equal(contentError.Message, progressError.Message);
        Assert.Equal(0, session.TimeSpentSeconds);
        Assert.Empty(_h.Telemetry);
    }

    [Fact]
    public async Task GetContentAsync_UsesPinnedVersionAndOrdersOnlyLiveContent()
    {
        var session = _h.AddSession();
        _h.Versions.Single().IsCurrent = false;
        _h.Versions.Add(new StoryVersion { Id = 60, StoryId = ReadingHarness.Story, IsCurrent = true, Title = "Bản mới" });
        var second = _h.AddScene(1, "Cảnh hai");
        var first = _h.AddScene(0, "Cảnh một");
        _h.AddScene(2, "Cảnh đã xóa").IsDeleted = true;
        _h.AddScene(0, "Bản mới", 60);
        _h.AddSegment(first, 2, "Đoạn hai");
        _h.AddSegment(first, 1, "Đoạn một");
        _h.AddSegment(first, 3, "Đoạn đã xóa").IsDeleted = true;
        _h.AddSegment(second, 1, "Cảnh hai");
        _h.AddVocabulary(2, "hai");
        _h.AddVocabulary(1, "một");
        _h.AddVocabulary(3, "đã xóa").IsDeleted = true;
        _h.AddVocabulary(4, "bản mới").StoryVersionId = 60;

        var content = await _sut.GetContentAsync(ReadingHarness.Child, session.Id);

        Assert.Equal("Truyện 10", content.Title);
        Assert.Equal(new[] { "Cảnh một", "Cảnh hai" }, content.Scenes.Select(s => s.Text));
        Assert.Equal(new[] { "Đoạn một", "Đoạn hai" }, content.Scenes[0].Segments.Select(s => s.Text));
        Assert.Equal(new[] { "một", "hai" }, content.Vocabulary.Select(v => v.Term));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("generated")]
    [InlineData("deleted")]
    [InlineData("other-version")]
    public async Task GetContentAsync_HidesUnavailableMediaAndItsWordTimings(string state)
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "a");
        var segment = _h.AddSegment(scene, 1, "a");
        _h.AddAudio(segment, "audio", "[]");
        _h.AddIllustration(scene, "image");
        foreach (var asset in _h.Assets)
        {
            if (state == "failed") asset.Status = MediaStatus.Failed;
            if (state == "generated") asset.Status = MediaStatus.Generated;
            if (state == "deleted") asset.IsDeleted = true;
            if (state == "other-version") asset.StoryVersionId = 60;
        }

        var content = await _sut.GetContentAsync(ReadingHarness.Child, session.Id);

        var result = Assert.Single(content.Scenes);
        Assert.Null(result.IllustrationUrl);
        Assert.Null(Assert.Single(result.Segments).AudioUrl);
        Assert.Null(result.Segments[0].WordTimingsJson);
    }

    [Fact]
    public async Task GetContentAsync_ChoosesLatestReadyAssetWithMatchingTimings()
    {
        var session = _h.AddSession();
        var scene = _h.AddScene(0, "a");
        var segment = _h.AddSegment(scene, 1, "a");
        _h.AddAudio(segment, "old", "old-timings").CompletedAt = DateTime.UtcNow.AddHours(-1);
        _h.AddAudio(segment, "new", "new-timings").CompletedAt = DateTime.UtcNow;
        _h.AddAudio(segment, "failed", "failed-timings").Status = MediaStatus.Failed;

        var content = await _sut.GetContentAsync(ReadingHarness.Child, session.Id);

        Assert.Equal("new", content.Scenes[0].Segments[0].AudioUrl);
        Assert.Equal("new-timings", content.Scenes[0].Segments[0].WordTimingsJson);
    }

    [Fact]
    public async Task SaveProgressAsync_RestoresPageAndModeWhenResumed()
    {
        var session = _h.AddSession(SessionStatus.Started);
        _h.AddScene(0, "a");
        _h.AddScene(1, "b");

        await _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 2, ReadingMode = " listenfirst ", AddedSeconds = 30 });
        var resumed = await _sut.StartAsync(ReadingHarness.Child, ReadingHarness.SessionKey, Start());

        Assert.True(resumed.Resumed);
        Assert.Equal(session.Id, resumed.SessionId);
        Assert.Equal(2, resumed.LastPage);
        Assert.Equal("ListenFirst", resumed.LastReadingMode);
        var telemetry = Assert.Single(_h.Telemetry);
        Assert.Equal(TelemetryEventType.PageViewed, telemetry.EventType);
        Assert.Equal(session.Id, telemetry.ReadingSessionId);
        using var payload = JsonDocument.Parse(telemetry.EventPayload!);
        Assert.Equal(2, payload.RootElement.GetProperty("page").GetInt32());
        Mock.Get(_h.UnitOfWork.Object.Repository<ReadingProgress>())
            .Verify(r => r.Update(It.IsAny<ReadingProgress>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveProgressAsync_OmittedModeAndBackwardPage_PreserveModeFlagsAndCompletedPages(string? mode)
    {
        var session = _h.AddSession();
        session.PagesCompleted = 3;
        session.TimeSpentSeconds = 40;
        _h.AddScene(0, "a");
        var progress = new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story,
            LastPageRead = 4, LastReadingMode = "ReadFirst", IsBookmarked = true, IsFavorited = true
        };
        _h.Progress.Add(progress);

        var result = await _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1, ReadingMode = mode, AddedSeconds = 10 });

        Assert.Equal("ReadFirst", result.ReadingMode);
        Assert.Equal(1, result.Page);
        Assert.Equal(50, result.TimeSpentSeconds);
        Assert.Equal(3, session.PagesCompleted);
        Assert.True(progress.IsBookmarked);
        Assert.True(progress.IsFavorited);
        Assert.Single(_h.Progress);
        Mock.Get(_h.UnitOfWork.Object.Repository<ReadingProgress>()).Verify(r => r.Update(progress), Times.Once);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("ReadFirst, ReadFirst")]
    public async Task SaveProgressAsync_NonNameReadingMode_IsRejectedBeforeMutation(string mode)
    {
        var session = _h.AddSession(SessionStatus.Started);
        _h.AddScene(0, "a");

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1, ReadingMode = mode, AddedSeconds = 10 }));

        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
        Assert.Equal(SessionStatus.Started, session.Status);
        Assert.Equal(0, session.TimeSpentSeconds);
    }

    [Theory]
    [InlineData("no-scenes")]
    [InlineData("deleted-scene")]
    [InlineData("other-version")]
    [InlineData("gap")]
    public async Task SaveProgressAsync_PageMustHaveAnExistingScene(string state)
    {
        var session = _h.AddSession();
        if (state == "deleted-scene") _h.AddScene(0, "a").IsDeleted = true;
        if (state == "other-version") _h.AddScene(0, "a", 60);
        if (state == "gap") _h.AddScene(1, "b");

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1 }));

        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
    }

    [Fact]
    public async Task SaveProgressAsync_AbandonedSession_IsConflict()
    {
        var session = _h.AddSession(SessionStatus.Abandoned);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1 }));
        Assert.Equal(SessionStatus.Abandoned, session.Status);
    }

    [Fact]
    public async Task SaveProgressAsync_ReadsLatestTimeAfterAcquiringLock()
    {
        var latest = _h.AddSession();
        latest.TimeSpentSeconds = 80;
        _h.AddScene(0, "a");
        var repository = Mock.Get(_h.UnitOfWork.Object.Repository<ReadingSession>());
        repository.SetupSequence(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ReadingSession, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadingSession
            {
                Id = latest.Id, StoryId = ReadingHarness.Story, ChildProfileId = ReadingHarness.Child,
                StoryVersionId = ReadingHarness.Version, Status = SessionStatus.Reading, TimeSpentSeconds = 10
            })
            .ReturnsAsync(latest);

        var result = await _sut.SaveProgressAsync(ReadingHarness.Child, latest.Id,
            new SaveProgressRequestDto { Page = 1, AddedSeconds = 5 });

        Assert.Equal(85, result.TimeSpentSeconds);
    }

    [Fact]
    public async Task SaveProgressAsync_SaveFailure_RollsBackTransaction()
    {
        var session = _h.AddSession();
        _h.AddScene(0, "a");
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Save failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SaveProgressAsync(ReadingHarness.Child, session.Id,
            new SaveProgressRequestDto { Page = 1 }));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- CompleteAsync ----

    [Fact]
    public async Task CompleteAsync_MarksCompleted_AndRecordsRoadmapStep()
    {
        var session = _h.AddSession();
        _recorder.Setup(r => r.RecordSessionCompletedAsync(session.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChildRoadmapItem { Id = 1 });

        var result = await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.NotNull(session.CompletedAt);
        Assert.False(result.IsRetake);
        Assert.True(result.RoadmapStepCompleted);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.SessionCompleted);
    }

    [Fact]
    public async Task CompleteAsync_StoryCompletedBefore_IsRetake()
    {
        var first = _h.AddSession(SessionStatus.Completed);
        var second = _h.AddSession(SessionStatus.Reading);

        var result = await _sut.CompleteAsync(ReadingHarness.Child, second.Id);

        Assert.True(result.IsRetake);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(second.Id, true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(SessionStatus.Completed, first.Status);
    }

    [Fact]
    public async Task CompleteAsync_CalledTwice_DoesNotRecordTwice()
    {
        var session = _h.AddSession();

        await _sut.CompleteAsync(ReadingHarness.Child, session.Id);
        var again = await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(session.Id, again.SessionId);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_ForceExitedSession_IsForbidden()
    {
        var session = _h.AddSession(SessionStatus.ForceExited);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));
    }

    // ---- Bookmark / Favorite ----

    [Fact]
    public async Task SetBookmarkAndFavorite_TogglePerStory_AndLogTelemetry()
    {
        var session = _h.AddSession();

        var bookmarked = await _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, true);
        var favorited = await _sut.SetFavoriteAsync(ReadingHarness.Child, session.Id, true);
        var cleared = await _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, false);

        Assert.True(bookmarked.IsBookmarked);
        Assert.True(favorited.IsFavorited);
        Assert.True(favorited.IsBookmarked);
        Assert.False(cleared.IsBookmarked);
        Assert.True(cleared.IsFavorited);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.StoryBookmarked);
        Assert.Contains(_h.Telemetry, t => t.EventType == TelemetryEventType.StoryFavorited);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CompleteAsync_RealRecorder_RespectsRetakesAndHoldMode(bool retake, bool hold)
    {
        var roadmap = new ChildRoadmapHarness();
        var currentStep = roadmap.AddItem(1, ReadingHarness.Child, ReadingHarness.Story, ChildRoadmapItemStatus.Open);
        var nextStep = roadmap.AddItem(2, ReadingHarness.Child, 11, ChildRoadmapItemStatus.Waiting);
        _h.Stories.Add(roadmap.Stories.Single(s => s.Id == 11));
        _h.UnitOfWork.Setup(u => u.Repository<ChildRoadmapItem>())
            .Returns(roadmap.UnitOfWork.Object.Repository<ChildRoadmapItem>());
        if (hold) _h.AddHoldMode(ReadingHarness.Child, triggeringStoryId: ReadingHarness.Story);
        if (retake) _h.AddSession(SessionStatus.Completed);
        var session = _h.AddSession();
        var recorder = new ChildRoadmapProgressRecorder(_h.UnitOfWork.Object,
            new ChildRoadmapAdvancer(_h.UnitOfWork.Object, _eligibility.Object),
            Mock.Of<IChildRoadmapNotifier>(), Mock.Of<IAuditLogWriter>());
        var service = new ChildReadingService(_h.UnitOfWork.Object, _eligibility.Object, recorder, _rewards.Object);

        var result = await service.CompleteAsync(ReadingHarness.Child, session.Id);
        var completedAt = session.CompletedAt;
        var replay = await service.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(retake, result.IsRetake);
        Assert.Equal(!retake, result.RoadmapStepCompleted);
        Assert.Equal(retake ? ChildRoadmapItemStatus.Open : ChildRoadmapItemStatus.Completed, currentStep.Status);
        Assert.Equal(retake || hold ? ChildRoadmapItemStatus.Waiting : ChildRoadmapItemStatus.Open, nextStep.Status);
        Assert.Equal(retake ? (int?)null : currentStep.Id, session.ChildRoadmapItemId);
        Assert.Equal(result.IsRetake, replay.IsRetake);
        Assert.Equal(result.RoadmapStepCompleted, replay.RoadmapStepCompleted);
        Assert.Equal(completedAt, session.CompletedAt);
        Assert.Equal(TelemetryEventType.SessionCompleted, Assert.Single(_h.Telemetry).EventType);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysOriginalRetakeDecisionAfterLaterCompletion()
    {
        var first = _h.AddSession();
        var second = _h.AddSession();

        var firstResult = await _sut.CompleteAsync(ReadingHarness.Child, first.Id);
        var secondResult = await _sut.CompleteAsync(ReadingHarness.Child, second.Id);
        var firstReplay = await _sut.CompleteAsync(ReadingHarness.Child, first.Id);
        var secondReplay = await _sut.CompleteAsync(ReadingHarness.Child, second.Id);

        Assert.False(firstResult.IsRetake);
        Assert.True(secondResult.IsRetake);
        Assert.Equal(firstResult.IsRetake, firstReplay.IsRetake);
        Assert.Equal(secondResult.IsRetake, secondReplay.IsRetake);
        Assert.Equal(2, _h.Telemetry.Count);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("other-child")]
    [InlineData("other-story")]
    [InlineData("deleted")]
    [InlineData("abandoned")]
    public async Task CompleteAsync_UnrelatedHistory_IsNotRetake(string state)
    {
        var previous = _h.AddSession(SessionStatus.Completed);
        if (state == "other-child") previous.ChildProfileId = ReadingHarness.OtherChild;
        if (state == "other-story") previous.StoryId = 11;
        if (state == "deleted") previous.IsDeleted = true;
        if (state == "abandoned") previous.Status = SessionStatus.Abandoned;
        var session = _h.AddSession();

        var result = await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.False(result.IsRetake);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(session.Id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SessionStatus.Started)]
    [InlineData(SessionStatus.Reading)]
    [InlineData(SessionStatus.Activity)]
    public async Task CompleteAsync_AllActiveStates_CanComplete(SessionStatus status)
    {
        using var cts = new CancellationTokenSource();
        var session = _h.AddSession(status);
        session.PagesCompleted = 4;
        session.TimeSpentSeconds = 90;

        await _sut.CompleteAsync(ReadingHarness.Child, session.Id, cts.Token);

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(4, session.PagesCompleted);
        Assert.Equal(90, session.TimeSpentSeconds);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(session.Id, false, cts.Token), Times.Once);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("other-child")]
    public async Task CompletionAndFlags_InaccessibleSession_HaveNoSideEffects(string state)
    {
        var session = _h.AddSession();
        if (state == "missing") _h.Sessions.Clear();
        if (state == "deleted") session.IsDeleted = true;
        if (state == "other-child") session.ChildProfileId = ReadingHarness.OtherChild;

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, true));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetFavoriteAsync(ReadingHarness.Child, session.Id, true));

        Assert.Equal(SessionStatus.Reading, session.Status);
        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
        _recorder.VerifyNoOtherCalls();
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(SessionStatus.Reading)]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.ForceExited)]
    public async Task CompletionAndFlags_InterruptedSession_IsForbidden(SessionStatus status)
    {
        var session = _h.AddSession(status);
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, true));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SetFavoriteAsync(ReadingHarness.Child, session.Id, true));

        Assert.Equal(ReadingSessionInterruptPolicy.ChildMessage, error.Message);
        Assert.Equal(status, session.Status);
        Assert.Empty(_h.Progress);
        Assert.Empty(_h.Telemetry);
        _recorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteAsync_AbandonedSession_IsForbidden()
    {
        var session = _h.AddSession(SessionStatus.Abandoned);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));

        Assert.Equal(SessionStatus.Abandoned, session.Status);
        Assert.Null(session.CompletedAt);
        Assert.Empty(_h.Telemetry);
        _recorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteAsync_CompletedWhileWaitingForLock_IsNotRecordedAgain()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        Mock.Get(_h.UnitOfWork.Object.Repository<ReadingSession>()).SetupSequence(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ReadingSession, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadingSession
            {
                Id = session.Id, StoryId = session.StoryId, ChildProfileId = session.ChildProfileId, Status = SessionStatus.Reading
            }).ReturnsAsync(session);

        await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.Empty(_h.Telemetry);
        _recorder.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteAsync_RecorderFailure_RequestsRollback()
    {
        var session = _h.AddSession();
        _recorder.Setup(r => r.RecordSessionCompletedAsync(session.Id, false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Recorder failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));

        _h.UnitOfWork.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetFlag_PreservesReadingPositionModeAndOtherFlag(bool bookmark)
    {
        var session = _h.AddSession(SessionStatus.Completed);
        var progress = new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.Child, StoryId = ReadingHarness.Story,
            LastPageRead = 5, LastReadingMode = "ListenFirst", IsBookmarked = true, IsFavorited = true
        };
        _h.Progress.Add(progress);

        var result = bookmark
            ? await _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, false)
            : await _sut.SetFavoriteAsync(ReadingHarness.Child, session.Id, false);

        Assert.Equal(!bookmark, result.IsBookmarked);
        Assert.Equal(bookmark, result.IsFavorited);
        Assert.Equal(5, progress.LastPageRead);
        Assert.Equal("ListenFirst", progress.LastReadingMode);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Single(_h.Progress);
        var telemetry = Assert.Single(_h.Telemetry);
        Assert.Equal(session.Id, telemetry.ReadingSessionId);
        Assert.Equal(bookmark ? TelemetryEventType.StoryBookmarked : TelemetryEventType.StoryFavorited, telemetry.EventType);
        using var payload = JsonDocument.Parse(telemetry.EventPayload!);
        Assert.False(payload.RootElement.GetProperty("value").GetBoolean());
    }

    [Fact]
    public async Task SetFlags_ShareProgressBetweenSessionsButNotChildren()
    {
        var first = _h.AddSession();
        var second = _h.AddSession();
        _h.Progress.Add(new ReadingProgress
        {
            Id = 1, ChildProfileId = ReadingHarness.OtherChild, StoryId = ReadingHarness.Story,
            IsBookmarked = false, IsFavorited = false
        });

        await _sut.SetBookmarkAsync(ReadingHarness.Child, first.Id, true);
        var result = await _sut.SetFavoriteAsync(ReadingHarness.Child, second.Id, true);

        Assert.True(result.IsBookmarked);
        Assert.True(result.IsFavorited);
        Assert.Equal(2, _h.Progress.Count);
        var otherChild = _h.Progress.Single(p => p.ChildProfileId == ReadingHarness.OtherChild);
        Assert.False(otherChild.IsBookmarked);
        Assert.False(otherChild.IsFavorited);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetFlag_NewProgress_IsAddedWithoutBeingMarkedModified(bool bookmark)
    {
        var session = _h.AddSession();

        if (bookmark) await _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, true);
        else await _sut.SetFavoriteAsync(ReadingHarness.Child, session.Id, true);

        var progress = Assert.Single(_h.Progress);
        Assert.Equal(1, progress.LastPageRead);
        Mock.Get(_h.UnitOfWork.Object.Repository<ReadingProgress>()).Verify(r => r.Update(It.IsAny<ReadingProgress>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetFlag_SaveFailure_RequestsRollback(bool bookmark)
    {
        var session = _h.AddSession();
        _h.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Save failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => bookmark
            ? _sut.SetBookmarkAsync(ReadingHarness.Child, session.Id, true)
            : _sut.SetFavoriteAsync(ReadingHarness.Child, session.Id, true));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(CancellationToken.None), Times.Once);
        _h.UnitOfWork.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- GetSignalAsync ----

    [Fact]
    public async Task GetSignalAsync_NoInterrupt_SaysKeepReading()
    {
        var session = _h.AddSession();

        var signal = await _sut.GetSignalAsync(ReadingHarness.Child, session.Id);

        Assert.False(signal.ShouldExit);
        Assert.Equal("Reading", signal.SessionStatus);
    }

    [Fact]
    public async Task GetSignalAsync_Interrupted_ExitsToJourneyMapWithFriendlyMessage()
    {
        var session = _h.AddSession();
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var signal = await _sut.GetSignalAsync(ReadingHarness.Child, session.Id);

        Assert.True(signal.ShouldExit);
        Assert.Equal(ReadingSessionInterruptPolicy.ReturnToJourneyMap, signal.ReturnTo);
        Assert.Equal(ReadingSessionInterruptPolicy.ChildMessage, signal.Message);
        Assert.Equal(SessionStatus.ForceExited, session.Status);
    }

    [Fact]
    public async Task GetSignalAsync_InterruptedAfterCompletion_KeepsCompleted()
    {
        var session = _h.AddSession(SessionStatus.Completed);
        session.ForceExitRequestedAt = DateTime.UtcNow;

        var signal = await _sut.GetSignalAsync(ReadingHarness.Child, session.Id);

        Assert.True(signal.ShouldExit);
        Assert.Equal(SessionStatus.Completed, session.Status);
    }

    // ---- LogEventAsync ----

    [Fact]
    public async Task LogEventAsync_AllowedEvent_IsStored()
    {
        var session = _h.AddSession();

        await _sut.LogEventAsync(ReadingHarness.Child, session.Id,
            new ReadingEventRequestDto { EventType = "TtsStarted", Payload = "{\"segment\":1}" });

        var log = _h.Telemetry.Single();
        Assert.Equal(TelemetryEventType.TtsStarted, log.EventType);
        Assert.Equal("{\"segment\":1}", log.EventPayload);
    }

    [Theory]
    [InlineData("RewardGranted")]
    [InlineData("SessionCompleted")]
    [InlineData("khong-co")]
    public async Task LogEventAsync_EventTheAppMayNotSend_IsBadRequest(string eventType)
    {
        var session = _h.AddSession();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LogEventAsync(ReadingHarness.Child, session.Id, new ReadingEventRequestDto { EventType = eventType }));
    }

    [Fact]
    public async Task LogEventAsync_PayloadTooLong_IsBadRequest()
    {
        var session = _h.AddSession();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.LogEventAsync(ReadingHarness.Child, session.Id, new ReadingEventRequestDto
            {
                EventType = "TtsStarted",
                Payload = new string('x', ChildReadingPolicy.MaxEventPayloadLength + 1)
            }));
    }
    // ---- Thưởng khi hoàn thành ----

    [Fact]
    public async Task CompleteAsync_FirstRead_AsksForRewardAndReportsIt()
    {
        var session = _h.AddSession();
        _rewards.Setup(r => r.OnStoryCompletedAsync(session, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RewardOutcome(10, new[] { "FIRST_STORY_COMPLETED" }));

        var result = await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(10, result.ExpGained);
        Assert.Equal(new[] { "FIRST_STORY_COMPLETED" }, result.NewBadges);
        _rewards.Verify(r => r.OnStoryCompletedAsync(session, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_Retake_AsksForRewardWithRetakeFlag()
    {
        _h.AddSession(SessionStatus.Completed);
        var second = _h.AddSession(SessionStatus.Reading);

        await _sut.CompleteAsync(ReadingHarness.Child, second.Id);

        _rewards.Verify(r => r.OnStoryCompletedAsync(second, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_CalledAgain_DoesNotRewardAgain()
    {
        var session = _h.AddSession();

        await _sut.CompleteAsync(ReadingHarness.Child, session.Id);
        var again = await _sut.CompleteAsync(ReadingHarness.Child, session.Id);

        Assert.Equal(0, again.ExpGained);
        _rewards.Verify(r => r.OnStoryCompletedAsync(
            It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_RewardFailure_RollsBack()
    {
        var session = _h.AddSession();
        _rewards.Setup(r => r.OnStoryCompletedAsync(It.IsAny<ReadingSession>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("reward"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteAsync(ReadingHarness.Child, session.Id));

        _h.UnitOfWork.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _recorder.Verify(r => r.RecordSessionCompletedAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
