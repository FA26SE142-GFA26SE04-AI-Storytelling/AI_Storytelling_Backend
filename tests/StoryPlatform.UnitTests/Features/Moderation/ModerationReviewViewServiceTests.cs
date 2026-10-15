using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class ModerationReviewViewServiceTests
{
    private readonly Mock<IGenericRepository<ModerationReviewView>> _viewRepoMock = new();
    private readonly Mock<IGenericRepository<Story>> _storyRepoMock = new();
    private readonly Mock<IGenericRepository<StoryVersion>> _versionRepoMock = new();
    private readonly Mock<IGenericRepository<ReadingRoadmap>> _roadmapRepoMock = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IAuditLogWriter> _auditLogWriterMock = new();
    private readonly ModerationReviewViewService _sut;

    private readonly UserAccount _moderator = new()
    {
        Id = 11, Username = "moderator_demo1", Email = "moderator1@example.com", FullName = "Nguyen Thi Lan",
        Role = UserRole.Moderator, Status = AccountStatus.Active
    };

    public ModerationReviewViewServiceTests()
    {
        _unitOfWorkMock.Setup(u => u.Repository<ModerationReviewView>()).Returns(_viewRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<Story>()).Returns(_storyRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<StoryVersion>()).Returns(_versionRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<ReadingRoadmap>()).Returns(_roadmapRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Repository<UserAccount>()).Returns(_userRepoMock.Object);
        _userRepoMock.Setup(r => r.GetByIdAsync(_moderator.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_moderator);
        _viewRepoMock
            .Setup(r => r.AddAsync(It.IsAny<ModerationReviewView>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModerationReviewView view, CancellationToken _) => view);
        _viewRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModerationReviewView?)null);
        _sut = new ModerationReviewViewService(_unitOfWorkMock.Object, _auditLogWriterMock.Object);
    }

    [Fact]
    public async Task GrantAsync_ExpiredView_CreatesFreshGrant()
    {
        ReturnStory(published: true);
        var expired = new ModerationReviewView
        {
            Id = 5, ModeratorUserId = 11, StoryId = 10, SourceTab = "published_content",
            GrantedAt = DateTime.UtcNow.AddHours(-3), ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        _viewRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ModerationReviewView, bool>> predicate, string? _, CancellationToken _) =>
                predicate.Compile()(expired) ? expired : null);

        var result = await _sut.GrantAsync(11,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10 });

        Assert.True(result.GrantedAt > expired.ExpiresAt);
        Assert.Equal(TimeSpan.FromHours(2), result.ExpiresAt - result.GrantedAt);
        _viewRepoMock.Verify(r => r.AddAsync(
            It.IsAny<ModerationReviewView>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_DifferentVersion_DoesNotReuseWrongVersionGrant()
    {
        ReturnStory(published: true);
        _versionRepoMock.Setup(r => r.GetByIdAsync(55, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryVersion { Id = 55, StoryId = 10 });
        var existing = new ModerationReviewView
        {
            Id = 5, ModeratorUserId = 11, StoryId = 10, StoryVersionId = 54,
            SourceTab = "published_content", ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        _viewRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ModerationReviewView, bool>> predicate, string? _, CancellationToken _) =>
                predicate.Compile()(existing) ? existing : null);

        var result = await _sut.GrantAsync(11, new GrantReviewViewRequestDto
        {
            SourceTab = "published_content", StoryId = 10, StoryVersionId = 55
        });

        Assert.Equal(55, result.StoryVersionId);
        _viewRepoMock.Verify(r => r.AddAsync(
            It.IsAny<ModerationReviewView>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(11, 10, null, 30, true)]
    [InlineData(11, 10, null, -1, false)]
    [InlineData(12, 10, null, 30, false)]
    [InlineData(11, 99, null, 30, false)]
    [InlineData(11, null, 4, 30, false)]
    public async Task HasActiveViewAsync_MatchesActorTargetAndExpiry(
        int ownerId, int? storyId, int? roadmapId, int remainingMinutes, bool expected)
    {
        var view = new ModerationReviewView
        {
            ModeratorUserId = ownerId, StoryId = storyId, ReadingRoadmapId = roadmapId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(remainingMinutes)
        };
        _viewRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ModerationReviewView, bool>> predicate, CancellationToken _) =>
                predicate.Compile()(view));

        Assert.Equal(expected, await _sut.HasActiveViewAsync(11, 10, null));
    }

    [Fact]
    public async Task SuspendedModerator_CannotGrantOrUseExistingView()
    {
        _moderator.Status = AccountStatus.Suspended;
        _viewRepoMock.Setup(r => r.ExistsAsync(
                It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.False(await _sut.HasActiveViewAsync(11, 10, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GrantAsync(11,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10 }));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(10, 4)]
    public async Task HasActiveViewAsync_AmbiguousTarget_ReturnsFalse(int? storyId, int? roadmapId)
    {
        Assert.False(await _sut.HasActiveViewAsync(11, storyId, roadmapId));
        _viewRepoMock.Verify(r => r.ExistsAsync(
            It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void ReturnStory(bool published) =>
        _storyRepoMock.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Story { Id = 10, IsPublished = published });

    [Fact]
    public async Task GrantAsync_PublishedStory_CreatesViewExpiringInTwoHoursAndAudits()
    {
        ReturnStory(published: true);
        var before = DateTime.UtcNow;

        var result = await _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10 });

        Assert.Equal(10, result.StoryId);
        Assert.Equal("published_content", result.SourceTab);
        Assert.InRange(result.ExpiresAt, before.AddHours(2), DateTime.UtcNow.AddHours(2));
        _viewRepoMock.Verify(r => r.AddAsync(
            It.Is<ModerationReviewView>(v => v.ModeratorUserId == _moderator.Id && v.StoryId == 10),
            It.IsAny<CancellationToken>()), Times.Once);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _moderator.Id, "ModerationReviewViewGranted", nameof(ModerationReviewView), It.IsAny<int>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_ActiveViewAlreadyExists_ReusesItWithoutExtendingAndStillAudits()
    {
        ReturnStory(published: true);
        var existing = new ModerationReviewView
        {
            Id = 7, ModeratorUserId = _moderator.Id, StoryId = 10, SourceTab = "published_content",
            GrantedAt = DateTime.UtcNow.AddMinutes(-30), ExpiresAt = DateTime.UtcNow.AddMinutes(90)
        };
        _viewRepoMock
            .Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10 });

        Assert.Equal(7, result.Id);
        Assert.Equal(existing.ExpiresAt, result.ExpiresAt);
        _viewRepoMock.Verify(r => r.AddAsync(
            It.IsAny<ModerationReviewView>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditLogWriterMock.Verify(a => a.LogAsync(
            _moderator.Id, "ModerationReviewViewGranted", nameof(ModerationReviewView), 7,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantAsync_StoryNotPublished_ThrowsBadRequest()
    {
        ReturnStory(published: false);

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10 }));
    }

    [Fact]
    public async Task GrantAsync_VersionOfAnotherStory_ThrowsBadRequest()
    {
        ReturnStory(published: true);
        _versionRepoMock.Setup(r => r.GetByIdAsync(55, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryVersion { Id = 55, StoryId = 99 });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10, StoryVersionId = 55 }));
    }

    [Fact]
    public async Task GrantAsync_PublishedRoadmapTemplate_Succeeds()
    {
        _roadmapRepoMock.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadingRoadmap { Id = 4, Status = RoadmapTemplateStatus.Published });

        var result = await _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = "roadmap_templates", ReadingRoadmapId = 4 });

        Assert.Equal(4, result.ReadingRoadmapId);
    }

    [Fact]
    public async Task GrantAsync_DraftRoadmapTemplate_ThrowsBadRequest()
    {
        _roadmapRepoMock.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadingRoadmap { Id = 4, Status = RoadmapTemplateStatus.Draft });

        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = "roadmap_templates", ReadingRoadmapId = 4 }));
    }

    [Theory]
    [InlineData("unknown_tab", 10, null)]
    [InlineData("published_content", null, 4)]
    [InlineData("roadmap_templates", 10, null)]
    public async Task GrantAsync_TabAndTargetDoNotMatch_ThrowsBadRequest(string tab, int? storyId, int? roadmapId)
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.GrantAsync(_moderator.Id,
            new GrantReviewViewRequestDto { SourceTab = tab, StoryId = storyId, ReadingRoadmapId = roadmapId }));
    }

    [Fact]
    public async Task GrantAsync_ActorIsNotStaff_ThrowsForbidden()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = 2, Role = UserRole.Parent, Status = AccountStatus.Active });

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.GrantAsync(2,
            new GrantReviewViewRequestDto { SourceTab = "published_content", StoryId = 10 }));
    }

    [Fact]
    public async Task HasActiveViewAsync_ReturnsWhetherUnexpiredViewExists()
    {
        _viewRepoMock
            .Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<ModerationReviewView, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Assert.True(await _sut.HasActiveViewAsync(_moderator.Id, 10, null));
    }
}
