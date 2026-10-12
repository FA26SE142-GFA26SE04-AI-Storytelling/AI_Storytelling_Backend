using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Learning.Services;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Learning;

public class LearningProfileServiceTests
{
    private readonly Mock<IGenericRepository<LearningProfile>> _profileRepo = new();
    private readonly Mock<IGenericRepository<LearningProfileTopic>> _topicRepo = new();
    private readonly Mock<IGenericRepository<ChildProfile>> _childRepo = new();
    private readonly Mock<IGenericRepository<ChildProfileVersionHistory>> _historyRepo = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<ISupervisionAccessGuard> _guard = new();
    private readonly LearningProfileService _sut;

    public LearningProfileServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<LearningProfile>()).Returns(_profileRepo.Object);
        _unitOfWork.Setup(u => u.Repository<LearningProfileTopic>()).Returns(_topicRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfile>()).Returns(_childRepo.Object);
        _unitOfWork.Setup(u => u.Repository<ChildProfileVersionHistory>()).Returns(_historyRepo.Object);
        SetupChild(AgeBand.Age_6_8, ChildProfileStatus.Draft);
        _sut = new LearningProfileService(_unitOfWork.Object, _guard.Object);
    }

    [Fact]
    public async Task SetLearningProfileAsync_WithoutApproveReadingLevelPermission_StopsBeforeWrite()
    {
        _guard.Setup(g => g.EnsurePermissionAsync(
                1, 2, Permission.ApproveReadingLevel, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Không có quyền."));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SetLearningProfileAsync(1, 2, ValidRequest()));

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLearningProfileAsync_NewProfile_CreatesProfileAndTopicsInOneSave()
    {
        AllowSupervision();
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LearningProfile?)null);
        LearningProfile? added = null;
        _profileRepo.Setup(r => r.AddAsync(It.IsAny<LearningProfile>(), It.IsAny<CancellationToken>()))
            .Callback<LearningProfile, CancellationToken>((value, _) => added = value)
            .ReturnsAsync((LearningProfile value, CancellationToken _) => value);

        var result = await _sut.SetLearningProfileAsync(1, 2, ValidRequest());

        Assert.Equal(1, added!.ChildProfileId);
        Assert.Equal(2, added.ReadingLevel);
        Assert.Single(result.Topics);
        _topicRepo.Verify(r => r.AddAsync(
            It.Is<LearningProfileTopic>(t => t.LearningProfile == added && t.Topic == "Động vật"),
            It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLearningProfileAsync_ExistingProfile_ReplacesTopics()
    {
        AllowSupervision();
        var profile = new LearningProfile { Id = 5, ChildProfileId = 1, ReadingLevel = 1 };
        var oldTopics = new List<LearningProfileTopic>
        {
            new() { Id = 9, LearningProfileId = 5, Topic = "Cũ" }
        };
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _topicRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<LearningProfileTopic, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(oldTopics);
        var request = ValidRequest();
        request.ReadingLevel = 4;

        await _sut.SetLearningProfileAsync(1, 2, request);

        Assert.Equal(4, profile.ReadingLevel);
        _profileRepo.Verify(r => r.Update(profile), Times.Once);
        _topicRepo.Verify(r => r.DeleteRange(oldTopics), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SetLearningProfileAsync_InvalidReadingLevel_ThrowsBadRequest(int level)
    {
        AllowSupervision();
        var request = ValidRequest();
        request.ReadingLevel = level;

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _sut.SetLearningProfileAsync(1, 2, request));
    }

    [Fact]
    public async Task GetLearningProfileAsync_NotYetSet_ThrowsNotFound()
    {
        AllowSupervision();
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((LearningProfile?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetLearningProfileAsync(1, 2));
    }

    [Fact]
    public async Task GetLearningProfileAsync_Exists_ReturnsDtoWithTopics()
    {
        AllowSupervision();
        var profile = new LearningProfile { Id = 5, ChildProfileId = 1, ReadingLevel = 3 };
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        _topicRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<LearningProfileTopic, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LearningProfileTopic> { new() { LearningProfileId = 5, Topic = "Động vật", Relation = TopicRelation.FavoriteTopic } });

        var result = await _sut.GetLearningProfileAsync(1, 2);

        Assert.Equal(3, result.ReadingLevel);
        Assert.Single(result.Topics);
    }

    [Fact]
    public async Task DeleteLearningProfileAsync_NotExists_ThrowsNotFound()
    {
        AllowSupervision();
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync((LearningProfile?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteLearningProfileAsync(1, 2));
    }

    [Fact]
    public async Task DeleteLearningProfileAsync_Exists_DeletesProfileAndCascadesTopics()
    {
        AllowSupervision();
        var profile = new LearningProfile { Id = 5, ChildProfileId = 1 };
        _profileRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var topics = new List<LearningProfileTopic> { new() { Id = 9, LearningProfileId = 5, Topic = "Động vật" } };
        _topicRepo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<LearningProfileTopic, bool>>>(), null,
                It.IsAny<CancellationToken>())).ReturnsAsync(topics);

        await _sut.DeleteLearningProfileAsync(1, 2);

        _topicRepo.Verify(r => r.DeleteRange(topics), Times.Once);
        _profileRepo.Verify(r => r.Delete(profile), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AgeBand.Age_6_8, ReadingMode.ListenFirst)]
    [InlineData(AgeBand.Age_9_12, ReadingMode.ReadFirst)]
    public async Task SetLearningProfileAsync_NewProfileWithoutMode_UsesDefaultOfAgeBand(
        AgeBand ageBand, ReadingMode expected)
    {
        AllowSupervision();
        SetupChild(ageBand, ChildProfileStatus.Draft);
        SetupNoProfile(out var added);

        var result = await _sut.SetLearningProfileAsync(1, 2, ValidRequest());

        Assert.Equal(expected, added()!.ReadingMode);
        Assert.Equal(expected.ToString(), result.ReadingMode);
    }

    [Fact]
    public async Task SetLearningProfileAsync_ExplicitMode_IsStored()
    {
        AllowSupervision();
        SetupNoProfile(out var added);
        var request = ValidRequest();
        request.ReadingMode = ReadingMode.ReadFirst;

        await _sut.SetLearningProfileAsync(1, 2, request);

        Assert.Equal(ReadingMode.ReadFirst, added()!.ReadingMode);
    }

    [Fact]
    public async Task SetLearningProfileAsync_ActiveChildAndConfigChanged_WritesManualVersionHistory()
    {
        AllowSupervision();
        SetupChild(AgeBand.Age_6_8, ChildProfileStatus.Active);
        var profile = new LearningProfile
        {
            Id = 5, ChildProfileId = 1, ReadingLevel = 1, ReadingMode = ReadingMode.ListenFirst
        };
        _profileRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _topicRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<LearningProfileTopic, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LearningProfileTopic>());
        var request = ValidRequest();
        request.ReadingLevel = 3;

        await _sut.SetLearningProfileAsync(1, 2, request);

        _historyRepo.Verify(repository => repository.AddAsync(
            It.Is<ChildProfileVersionHistory>(history =>
                history.ChildProfileId == 1
                && history.Source == VersionHistorySource.Manual
                && history.RecommendationId == null
                && history.AppliedByUserId == 2
                && history.VersionStatus == VersionHistoryStatus.ActiveVersion
                && history.PreviousConfig.Contains("\"readingLevel\":1")
                && history.NewConfig.Contains("\"readingLevel\":3")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLearningProfileAsync_ActiveChildButNothingChanged_WritesNoHistory()
    {
        AllowSupervision();
        SetupChild(AgeBand.Age_6_8, ChildProfileStatus.Active);
        var request = ValidRequest();
        var profile = new LearningProfile
        {
            Id = 5,
            ChildProfileId = 1,
            ReadingLevel = request.ReadingLevel,
            ComprehensionGoal = request.ComprehensionGoal?.Trim(),
            ReadingMode = ReadingMode.ListenFirst
        };
        _profileRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _topicRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<LearningProfileTopic, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(request.Topics.Select(topic => new LearningProfileTopic
            {
                Topic = topic.Topic.Trim(),
                Relation = topic.Relation
            }).ToList());

        await _sut.SetLearningProfileAsync(1, 2, request);

        _historyRepo.Verify(repository => repository.AddAsync(
            It.IsAny<ChildProfileVersionHistory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLearningProfileAsync_DraftChildChanged_WritesNoHistory()
    {
        AllowSupervision();
        SetupChild(AgeBand.Age_6_8, ChildProfileStatus.Draft);
        var profile = new LearningProfile { Id = 5, ChildProfileId = 1, ReadingLevel = 1 };
        _profileRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
        _topicRepo.Setup(repository => repository.FindAsync(
                It.IsAny<Expression<Func<LearningProfileTopic, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LearningProfileTopic>());

        await _sut.SetLearningProfileAsync(1, 2, ValidRequest());

        _historyRepo.Verify(repository => repository.AddAsync(
            It.IsAny<ChildProfileVersionHistory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void AllowSupervision()
    {
        _guard.Setup(g => g.EnsurePermissionAsync(
                1, 2, Permission.ApproveReadingLevel, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _guard.Setup(g => g.EnsureActiveSupervisionAsync(1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SupervisionRelationship());
    }

    private void SetupChild(AgeBand ageBand, ChildProfileStatus status) => _childRepo
        .Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ChildProfile
        {
            Id = 1, OwnerUserId = 2, AgeBand = ageBand, Status = status
        });

    private void SetupNoProfile(out Func<LearningProfile?> added)
    {
        LearningProfile? captured = null;
        _profileRepo.Setup(repository => repository.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<LearningProfile, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((LearningProfile?)null);
        _profileRepo.Setup(repository => repository.AddAsync(
                It.IsAny<LearningProfile>(), It.IsAny<CancellationToken>()))
            .Callback<LearningProfile, CancellationToken>((value, _) => captured = value)
            .ReturnsAsync((LearningProfile value, CancellationToken _) => value);
        added = () => captured;
    }

    private static SetLearningProfileRequestDto ValidRequest() => new()
    {
        ReadingLevel = 2,
        ComprehensionGoal = "Hiểu ý chính",
        Topics = new List<SetLearningProfileTopicRequestDto>
        {
            new() { Topic = " Động vật ", Relation = TopicRelation.FavoriteTopic }
        }
    };
}
