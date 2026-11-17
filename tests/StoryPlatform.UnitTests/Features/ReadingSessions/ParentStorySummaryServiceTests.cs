using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ParentStorySummaryServiceTests
{
    private const int Parent = 7;
    private readonly ReadingHarness _h = new();
    private readonly Mock<IChildProfileAccessGuard> _guard = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly ParentStorySummaryService _sut;

    public ParentStorySummaryServiceTests()
    {
        _storage.Setup(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, TimeSpan _, CancellationToken _) => $"https://signed/{path}");
        _sut = new ParentStorySummaryService(_h.UnitOfWork.Object, _guard.Object, _storage.Object);
    }

    private StorySummary AddSummary(string? audioPath = "story-summaries/100/1/a.webm", int childId = ReadingHarness.Child)
    {
        var session = _h.AddSession(SessionStatus.Completed, childId);
        var summary = new StorySummary
        {
            Id = _h.Summaries.Count + 1, ReadingSessionId = session.Id, ChildProfileId = childId,
            StoryId = ReadingHarness.Story, StoryVersionId = ReadingHarness.Version,
            AudioStoragePath = audioPath, RecordingSentAt = audioPath == null ? null : DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMinutes(-_h.Summaries.Count)
        };
        _h.Summaries.Add(summary);
        return summary;
    }

    [Fact]
    public async Task ListAsync_HidesDeletedAndOtherChildren_AndHasNoUrlWithoutRecording()
    {
        AddSummary(audioPath: null);
        AddSummary().IsDeleted = true;
        AddSummary(childId: ReadingHarness.OtherChild);

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Null(item.RecordingUrl);
        _storage.Verify(s => s.GetSignedUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_NotOwner_PropagatesGuardFailure()
    {
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("x"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.ListAsync(99, ReadingHarness.Child, new ParentStorySummaryQueryDto()));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile_AndSoftDeletesSummaryAndFlags()
    {
        var summary = AddSummary();
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = 5 });

        await _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id);

        _storage.Verify(s => s.DeleteAsync("story-summaries/100/1/a.webm", It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(summary.IsDeleted);
        Assert.Null(summary.AudioStoragePath);
        Assert.All(_h.SummaryFlags, f => Assert.True(f.IsDeleted));
    }

    [Fact]
    public async Task DeleteAsync_NotOwner_DeletesNothing()
    {
        var summary = AddSummary();
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("x"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.DeleteAsync(99, ReadingHarness.Child, summary.Id));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(summary.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_SummaryOfAnotherChild_IsNotFound()
    {
        var summary = AddSummary(childId: ReadingHarness.OtherChild);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id));

        _storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(summary.IsDeleted);
    }

    [Fact]
    public async Task PurgeChildAsync_DeletesEveryRecordingOfTheChild()
    {
        var first = AddSummary("p/1.webm");
        var second = AddSummary("p/2.webm");
        var other = AddSummary("p/3.webm", ReadingHarness.OtherChild);

        await _sut.PurgeChildAsync(ReadingHarness.Child);

        _storage.Verify(s => s.DeleteAsync("p/1.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("p/2.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("p/3.webm", It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(first.IsDeleted && second.IsDeleted);
        Assert.Null(first.AudioStoragePath);
        Assert.False(other.IsDeleted);
    }
    [Fact]
    public async Task ListAsync_ReturnsQuestion_WithTheExactPicture_SceneText_AndSignedQuestionAudio()
    {
        var summary = AddSummary();
        var scene = _h.AddScene(2, "Thỏ ra vườn. Thỏ gặp rùa.");
        var seen = _h.AddBeatIllustration(scene, 2, 13, 25, "https://img/beat-2-old.png");
        seen.IsCurrent = false;
        _h.AddBeatIllustration(scene, 2, 13, 25, "https://img/beat-2-new.png"); // sinh lại sau khi bé hỏi
        var beat = _h.Beats.Single();
        summary.FlagsSentAt = DateTime.UtcNow;
        _h.SummaryFlags.Add(new StorySummaryFlag
        {
            Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id, IllustrationBeatId = beat.Id,
            MediaAssetId = seen.Id, QuestionText = "Sao thỏ buồn?", AudioStoragePath = "story-summaries/100/1/questions/q.webm",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3)
        });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Equal(1, item.UnansweredCount);
        Assert.Equal("Truyện 10", item.Title);
        Assert.Equal("https://signed/story-summaries/100/1/a.webm", item.RecordingUrl);
        var question = Assert.Single(item.Flags);
        Assert.Equal((1, scene.Id, 2, 2, "Thỏ ra vườn. Thỏ gặp rùa.", "Thỏ gặp rùa."),
            (question.QuestionId, question.SceneId, question.SceneIndex, question.BeatOrder, question.SceneText, question.PictureText));
        Assert.Equal("https://img/beat-2-old.png", question.IllustrationUrl);
        Assert.Equal("Sao thỏ buồn?", question.QuestionText);
        Assert.Equal("https://signed/story-summaries/100/1/questions/q.webm", question.QuestionAudioUrl);
        Assert.Null(question.AnsweredAt);
        _storage.Verify(s => s.GetSignedUrlAsync("story-summaries/100/1/questions/q.webm", StorySummaryPolicy.ListenUrlLifetime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_OldSceneFlag_UsesTheSessionsOwnIllustration_NotAnotherPackages()
    {
        var summary = AddSummary(audioPath: null);
        var scene = _h.AddScene(1, "Thỏ gặp rùa.");
        _h.AddIllustration(scene, "https://img/base.png");
        var foreign = _h.AddIllustration(scene, "https://img/package.png");
        foreign.ParentStoryPackageId = 77; // mới hơn nhưng thuộc gói khác
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var question = Assert.Single(Assert.Single(page.Items).Flags);
        Assert.Equal("https://img/base.png", question.IllustrationUrl);
        Assert.Null(question.BeatOrder);
        Assert.Null(question.PictureText);
        Assert.Null(question.QuestionText);
        Assert.Null(question.QuestionAudioUrl);
    }

    [Fact]
    public async Task ListAsync_CountsOnlyUnansweredQuestions()
    {
        var summary = AddSummary(audioPath: null);
        var first = _h.AddScene(0, "Cảnh 0.");
        var second = _h.AddScene(1, "Cảnh 1.");
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = first.Id, AnsweredAt = DateTime.UtcNow });
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 2, StorySummaryId = summary.Id, StorySceneId = second.Id });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        var item = Assert.Single(page.Items);
        Assert.Equal(1, item.UnansweredCount);
        Assert.Equal(new[] { 0, 1 }, item.Flags.Select(f => f.SceneIndex));
    }
    [Fact]
    public async Task SetAnsweredAsync_MarksAndUnmarks_KeepingTheFirstAnsweredTime()
    {
        var summary = AddSummary(audioPath: null);
        var scene = _h.AddScene(0, "Cảnh 0.");
        var flag = new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id };
        _h.SummaryFlags.Add(flag);

        var marked = await _sut.SetAnsweredAsync(Parent, ReadingHarness.Child, summary.Id, flag.Id, true);
        var first = marked.AnsweredAt;
        var again = await _sut.SetAnsweredAsync(Parent, ReadingHarness.Child, summary.Id, flag.Id, true);
        Assert.NotNull(first);
        Assert.Equal(first, again.AnsweredAt);

        var cleared = await _sut.SetAnsweredAsync(Parent, ReadingHarness.Child, summary.Id, flag.Id, false);
        Assert.Null(cleared.AnsweredAt);
        Assert.Null(flag.AnsweredAt);
    }

    [Fact]
    public async Task SetAnsweredAsync_QuestionOfAnotherSummaryOrChild_IsNotFound()
    {
        var mine = AddSummary(audioPath: null);
        var other = AddSummary(audioPath: null, childId: ReadingHarness.OtherChild);
        var scene = _h.AddScene(0, "Cảnh 0.");
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = other.Id, StorySceneId = scene.Id });

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetAnsweredAsync(Parent, ReadingHarness.Child, mine.Id, 1, true));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetAnsweredAsync(Parent, ReadingHarness.Child, other.Id, 1, true));
    }

    [Fact]
    public async Task DeleteAsync_AlsoDeletesQuestionRecordings()
    {
        var summary = AddSummary();
        var scene = _h.AddScene(0, "Cảnh 0.");
        _h.SummaryFlags.Add(new StorySummaryFlag
        {
            Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id, AudioStoragePath = "story-summaries/100/1/questions/q.webm"
        });

        await _sut.DeleteAsync(Parent, ReadingHarness.Child, summary.Id);

        _storage.Verify(s => s.DeleteAsync("story-summaries/100/1/questions/q.webm", It.IsAny<CancellationToken>()), Times.Once);
        var flag = Assert.Single(_h.SummaryFlags);
        Assert.True(flag.IsDeleted);
        Assert.Null(flag.AudioStoragePath);
    }

    private ParentStoryPackage Package(StorySummary summary, StoryScene scene, params int[] assetIds)
    {
        _h.Sessions.Single(session => session.Id == summary.ReadingSessionId).ParentStoryPackageId = 77;
        var package = new ParentStoryPackage
        {
            Id = 77, ChildProfileId = summary.ChildProfileId, StoryId = summary.StoryId,
            StoryVersionId = summary.StoryVersionId!.Value,
            ManifestJson = JsonSerializer.Serialize(new ParentPackageManifest([], [], [], [scene.Id], [], assetIds), ParentPackageContext.Json)
        };
        var repo = new Mock<IGenericRepository<ParentStoryPackage>>();
        repo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<ParentStoryPackage, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ParentStoryPackage, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<ParentStoryPackage>)new[] { package }.Where(predicate.Compile()).ToArray());
        _h.UnitOfWork.Setup(u => u.Repository<ParentStoryPackage>()).Returns(repo.Object);
        return package;
    }

    [Fact]
    public async Task ListAsync_LegacyPackageQuestion_UsesFrozenManifestEvenWhenOriginalAssetIsNotCurrent()
    {
        var summary = AddSummary(audioPath: null);
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        var frozen = _h.AddIllustration(scene, "https://img/frozen.png");
        frozen.IsCurrent = false;
        _h.AddIllustration(scene, "https://img/new-base.png");
        var foreign = _h.AddIllustration(scene, "https://img/other-package.png");
        foreign.ParentStoryPackageId = 88;
        Package(summary, scene, frozen.Id).Status = "Archived";
        _h.SummaryFlags.Add(new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        Assert.Equal("https://img/frozen.png", Assert.Single(Assert.Single(page.Items).Flags).IllustrationUrl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListAsync_SavedAssetOutsideTheSessionScope_IsHiddenWithoutSubstitutingAnotherImage(bool package)
    {
        var summary = AddSummary(audioPath: null);
        var scene = _h.AddScene(0, "Thỏ ra vườn.");
        var outside = _h.AddIllustration(scene, "https://img/outside.png");
        outside.ParentStoryPackageId = 88;
        var allowed = _h.AddIllustration(scene, "https://img/allowed.png");
        if (package) Package(summary, scene, allowed.Id);
        _h.SummaryFlags.Add(new StorySummaryFlag
        {
            Id = 1, StorySummaryId = summary.Id, StorySceneId = scene.Id, MediaAssetId = outside.Id
        });

        var page = await _sut.ListAsync(Parent, ReadingHarness.Child, new ParentStorySummaryQueryDto());

        Assert.Null(Assert.Single(Assert.Single(page.Items).Flags).IllustrationUrl);
    }

    [Fact]
    public async Task SetAnsweredAsync_NotOwner_DoesNotChangeTheQuestion()
    {
        var summary = AddSummary(audioPath: null);
        var question = new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = 1 };
        _h.SummaryFlags.Add(question);
        _guard.Setup(g => g.EnsureOwnerAsync(ReadingHarness.Child, 99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("x"));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.SetAnsweredAsync(99, ReadingHarness.Child, summary.Id, question.Id, true));

        Assert.Null(question.AnsweredAt);
        _h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetAnsweredAsync_DeletedSummaryOrQuestion_IsNotFound(bool deletedSummary)
    {
        var summary = AddSummary(audioPath: null);
        summary.IsDeleted = deletedSummary;
        var question = new StorySummaryFlag { Id = 1, StorySummaryId = summary.Id, StorySceneId = 1, IsDeleted = !deletedSummary };
        _h.SummaryFlags.Add(question);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.SetAnsweredAsync(Parent, ReadingHarness.Child, summary.Id, question.Id, true));

        Assert.Null(question.AnsweredAt);
    }

    [Fact]
    public async Task PurgeChildAsync_AlsoDeletesOnlyThatChildsQuestionAudio()
    {
        var mine = AddSummary(audioPath: null);
        var other = AddSummary(audioPath: null, childId: ReadingHarness.OtherChild);
        _h.SummaryFlags.Add(new StorySummaryFlag
        {
            Id = 1, StorySummaryId = mine.Id, StorySceneId = 1, AudioStoragePath = "questions/mine.webm",
            AudioContentType = "audio/webm", AudioBytes = 20
        });
        _h.SummaryFlags.Add(new StorySummaryFlag
        {
            Id = 2, StorySummaryId = other.Id, StorySceneId = 1, AudioStoragePath = "questions/other.webm"
        });

        await _sut.PurgeChildAsync(ReadingHarness.Child);

        _storage.Verify(s => s.DeleteAsync("questions/mine.webm", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync("questions/other.webm", It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(_h.SummaryFlags[0].AudioStoragePath);
        Assert.Null(_h.SummaryFlags[0].AudioContentType);
        Assert.Null(_h.SummaryFlags[0].AudioBytes);
        Assert.False(_h.SummaryFlags[1].IsDeleted);
    }
}
