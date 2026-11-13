using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public class ModerationQueueAuthorWorkTests
{
    private const int ModeratorId = 11;
    private const int CreatorId = 13;

    private readonly List<UserAccount> _users = new();
    private readonly List<Story> _stories = new();
    private readonly List<StoryVersion> _versions = new();
    private readonly List<ModerationItem> _items = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<ModerationItem>> _itemRepo = new();
    private readonly Mock<IGenericRepository<UserAccount>> _userRepo = new();
    private readonly Mock<IGenericRepository<Story>> _storyRepo = new();
    private readonly Mock<IGenericRepository<StoryVersion>> _versionRepo = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly ModerationQueueService _sut;
    private string? _lastPagedIncludeProperties;

    public ModerationQueueAuthorWorkTests()
    {
        _users.Add(new UserAccount
        {
            Id = ModeratorId,
            Username = "moderator_demo",
            FullName = "Nguyễn Kiểm Duyệt",
            Email = "mod@test.local",
            Role = UserRole.Moderator,
            Status = AccountStatus.Active
        });
        _users.Add(new UserAccount
        {
            Id = CreatorId,
            Username = "creator_demo",
            FullName = "Nguyễn Văn A",
            Email = "creator.secret@test.local",
            Role = UserRole.ContentCreator,
            Status = AccountStatus.Active
        });

        _uow.Setup(x => x.Repository<ModerationItem>()).Returns(_itemRepo.Object);
        _uow.Setup(x => x.Repository<UserAccount>()).Returns(_userRepo.Object);
        _uow.Setup(x => x.Repository<Story>()).Returns(_storyRepo.Object);
        _uow.Setup(x => x.Repository<StoryVersion>()).Returns(_versionRepo.Object);
        _uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _userRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _users.FirstOrDefault(u => u.Id == (int)id));
        _userRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<UserAccount, bool>>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserAccount, bool>> p, string? _, CancellationToken _) =>
                _users.FirstOrDefault(p.Compile()));

        _storyRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _stories.FirstOrDefault(s => s.Id == (int)id));
        _storyRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Story, bool>>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Story, bool>> p, string? _, CancellationToken _) =>
                _stories.FirstOrDefault(p.Compile()));

        _versionRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _versions.FirstOrDefault(v => v.Id == (int)id));
        _versionRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<StoryVersion, bool>>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<StoryVersion, bool>> p, string? _, CancellationToken _) =>
                _versions.FirstOrDefault(p.Compile()));

        _itemRepo.Setup(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => _items.FirstOrDefault(i => i.Id == (int)id));

        _itemRepo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<ModerationItem, bool>>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ModerationItem, bool>> p, string? include, CancellationToken _) =>
            {
                var found = _items.FirstOrDefault(p.Compile());
                return found == null ? null : WithRequestedIncludes(found, include);
            });

        _itemRepo.Setup(x => x.GetPagedAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                int page,
                int size,
                Expression<Func<ModerationItem, bool>>? filter,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>? orderBy,
                string? include,
                CancellationToken _) =>
            {
                _lastPagedIncludeProperties = include;
                var q = _items.AsQueryable();
                if (filter != null) q = q.Where(filter);
                var total = q.Count();
                if (orderBy != null) q = orderBy(q);
                var pageItems = q.Skip((page - 1) * size).Take(size)
                    .Select(i => WithRequestedIncludes(i, include))
                    .ToList();
                return (pageItems, total);
            });

        _sut = new ModerationQueueService(_uow.Object, _audit.Object);
    }

    private ModerationItem WithRequestedIncludes(ModerationItem src, string? include)
    {
        var inc = (include ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        return new ModerationItem
        {
            Id = src.Id,
            ItemType = src.ItemType,
            Status = src.Status,
            Priority = src.Priority,
            StoryId = src.StoryId,
            StoryVersionId = src.StoryVersionId,
            ContentReportId = src.ContentReportId,
            ReadingRoadmapId = src.ReadingRoadmapId,
            CreatedByUserId = src.CreatedByUserId,
            ClaimedByUserId = src.ClaimedByUserId,
            ClaimedAt = src.ClaimedAt,
            IsResubmission = src.IsResubmission,
            SlaDueAt = src.SlaDueAt,
            CreatedAt = src.CreatedAt,
            RowVersion = src.RowVersion,
            Story = inc.Contains(nameof(ModerationItem.Story))
                ? src.Story ?? _stories.FirstOrDefault(s => s.Id == src.StoryId)
                : null,
            StoryVersion = inc.Contains(nameof(ModerationItem.StoryVersion))
                ? src.StoryVersion ?? _versions.FirstOrDefault(v => v.Id == src.StoryVersionId)
                : null,
            CreatedByUser = inc.Contains(nameof(ModerationItem.CreatedByUser))
                ? src.CreatedByUser ?? _users.FirstOrDefault(u => u.Id == src.CreatedByUserId)
                : null,
            ClaimedByUser = inc.Contains(nameof(ModerationItem.ClaimedByUser))
                ? src.ClaimedByUser ?? _users.FirstOrDefault(u => u.Id == src.ClaimedByUserId)
                : null
        };
    }

    [Fact]
    public async Task ListAsync_PublicStoryReview_ReturnsSubmittedVersionTitle_AuthorFullName_AndEagerLoadsRelations()
    {
        _stories.Add(new Story { Id = 44, Title = "Tên truyện mới hơn đang soạn", AuthorUserId = CreatorId });
        _versions.Add(new StoryVersion { Id = 100, StoryId = 44, VersionNo = 1, Title = "Mái Nhà Ấm Áp Sau Cơn Mưa" });
        _versions.Add(new StoryVersion { Id = 101, StoryId = 44, VersionNo = 2, Title = "Bản Nháp Mới Hơn", IsCurrent = true });
        _items.Add(new ModerationItem
        {
            Id = 19,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Pending,
            StoryId = 44,
            StoryVersionId = 100,
            CreatedByUserId = CreatorId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        });

        var result = await _sut.ListAsync(ModeratorId, new ModerationQueueQueryDto());
        var dto = Assert.Single(result.Items);

        Assert.Equal("Mái Nhà Ấm Áp Sau Cơn Mưa", dto.StoryTitle);
        Assert.Equal(CreatorId, dto.CreatedByUserId);
        Assert.Equal("Nguyễn Văn A", dto.CreatedByName);
        Assert.DoesNotContain("@", dto.CreatedByName ?? string.Empty);

        Assert.NotNull(_lastPagedIncludeProperties);
        Assert.Contains(nameof(ModerationItem.Story), _lastPagedIncludeProperties);
        Assert.Contains(nameof(ModerationItem.StoryVersion), _lastPagedIncludeProperties);
        Assert.Contains(nameof(ModerationItem.CreatedByUser), _lastPagedIncludeProperties);
        Assert.Contains(nameof(ModerationItem.ClaimedByUser), _lastPagedIncludeProperties);
        _storyRepo.Verify(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        _versionRepo.Verify(x => x.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListAsync_PublicStoryReview_FallsBackToUsernameAndStoryTitle_WhenMissingVersionTitleOrFullName()
    {
        var creator = _users.Single(u => u.Id == CreatorId);
        creator.FullName = "   ";
        _stories.Add(new Story { Id = 45, Title = "Truyện dự phòng từ Story", AuthorUserId = CreatorId });
        _versions.Add(new StoryVersion { Id = 102, StoryId = 45, VersionNo = 1, Title = "  " });
        _items.Add(new ModerationItem
        {
            Id = 20,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Pending,
            StoryId = 45,
            StoryVersionId = 102,
            CreatedByUserId = CreatorId
        });

        var result = await _sut.ListAsync(ModeratorId, new ModerationQueueQueryDto());
        var dto = Assert.Single(result.Items);

        Assert.Equal("Truyện dự phòng từ Story", dto.StoryTitle);
        Assert.Equal(CreatorId, dto.CreatedByUserId);
        Assert.Equal("creator_demo", dto.CreatedByName);
    }

    [Fact]
    public async Task ListAsync_PublicStoryReview_WithMissingRelations_DoesNotThrowAndReturnsNulls()
    {
        _items.Add(new ModerationItem
        {
            Id = 21,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Pending,
            StoryId = 999,
            StoryVersionId = 888,
            CreatedByUserId = 777
        });

        var result = await _sut.ListAsync(ModeratorId, new ModerationQueueQueryDto());
        var dto = Assert.Single(result.Items);

        Assert.Equal(999, dto.StoryId);
        Assert.Null(dto.StoryTitle);
        Assert.Equal(777, dto.CreatedByUserId);
        Assert.Null(dto.CreatedByName);
    }

    [Theory]
    [InlineData(ModerationItemType.ContentReport)]
    [InlineData(ModerationItemType.GrayZoneStory)]
    [InlineData(ModerationItemType.RoadmapTemplateReview)]
    [InlineData(ModerationItemType.CommunitySubmission)]
    [InlineData(ModerationItemType.StoryRating)]
    public async Task ListAsync_ClaimAsync_OpenAsync_NonPublicStoryTypes_DoNotExposeSubmitterIdentity(ModerationItemType itemType)
    {
        const int ReporterOrChildAccountId = 55;
        _users.Add(new UserAccount
        {
            Id = ReporterOrChildAccountId,
            Username = "parent_reporter",
            FullName = "Phụ Huynh Báo Cáo",
            Email = "reporter@test.local",
            Role = UserRole.User,
            Status = AccountStatus.Active
        });
        _stories.Add(new Story { Id = 50, Title = "Truyện bị báo cáo", AuthorUserId = CreatorId });
        _versions.Add(new StoryVersion { Id = 150, StoryId = 50, VersionNo = 1, Title = "Phiên bản bị báo cáo" });
        _items.Add(new ModerationItem
        {
            Id = 30,
            ItemType = itemType,
            Status = ModerationItemStatus.Pending,
            StoryId = itemType == ModerationItemType.RoadmapTemplateReview ? null : 50,
            StoryVersionId = itemType == ModerationItemType.RoadmapTemplateReview ? null : 150,
            ReadingRoadmapId = itemType == ModerationItemType.RoadmapTemplateReview ? 12 : null,
            CreatedByUserId = ReporterOrChildAccountId
        });

        var listed = Assert.Single((await _sut.ListAsync(ModeratorId, new ModerationQueueQueryDto())).Items);
        Assert.Null(listed.CreatedByUserId);
        Assert.Null(listed.CreatedByName);

        var claimed = await _sut.ClaimAsync(ModeratorId, 30);
        Assert.Null(claimed.CreatedByUserId);
        Assert.Null(claimed.CreatedByName);

        var opened = await _sut.OpenAsync(ModeratorId, 30);
        Assert.Null(opened.CreatedByUserId);
        Assert.Null(opened.CreatedByName);
    }

    [Fact]
    public async Task ClaimAsync_And_OpenAsync_PublicStoryReview_ReturnSubmittedVersionTitleAndCreatorIdentity()
    {
        _stories.Add(new Story { Id = 44, Title = "Tên Story Mới Hơn", AuthorUserId = CreatorId });
        _versions.Add(new StoryVersion { Id = 100, StoryId = 44, VersionNo = 1, Title = "Tên Phiên Bản Đã Nộp" });
        _items.Add(new ModerationItem
        {
            Id = 19,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Pending,
            StoryId = 44,
            StoryVersionId = 100,
            CreatedByUserId = CreatorId
        });

        var claimed = await _sut.ClaimAsync(ModeratorId, 19);
        Assert.Equal("Tên Phiên Bản Đã Nộp", claimed.StoryTitle);
        Assert.Equal(CreatorId, claimed.CreatedByUserId);
        Assert.Equal("Nguyễn Văn A", claimed.CreatedByName);

        var opened = await _sut.OpenAsync(ModeratorId, 19);
        Assert.Equal("Tên Phiên Bản Đã Nộp", opened.StoryTitle);
        Assert.Equal(CreatorId, opened.CreatedByUserId);
        Assert.Equal("Nguyễn Văn A", opened.CreatedByName);
    }

    [Fact]
    public async Task WithdrawAndResubmit_PublicStoryReview_PreservesCreatorIdentityAndSubmittedVersionTitle()
    {
        var harness = new PublicStoryReviewHarness();
        harness.Items.Clear();
        var creator = harness.Users.Single(u => u.Id == PublicStoryReviewHarness.CreatorId);
        creator.FullName = "Tác Giả Rút Gửi Lại";
        harness.Stories[0].Title = "Tên truyện mới sau đó";
        harness.Versions[0].Title = "Phiên bản V1 đã nộp";
        harness.ContentCategories.Add(new ContentCategory { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
        harness.AddScene(1, 1);

        var readiness = new Mock<IMediaReadinessService>();
        readiness.Setup(s => s.CheckAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaReadinessResult(true, 1, 1, 1, 1,
                Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>()));

        var queue = new ModerationQueueService(harness.UnitOfWork.Object, harness.Audit.Object);
        var submission = new PublicStorySubmissionService(harness.UnitOfWork.Object, queue, readiness.Object, harness.Audit.Object);

        var first = await submission.SubmitAsync(PublicStoryReviewHarness.CreatorId, new SubmitPublicStoryRequestDto
        {
            StoryId = PublicStoryReviewHarness.StoryId,
            StoryVersionId = PublicStoryReviewHarness.VersionId,
            ContentCategoryIds = [5]
        });
        await submission.WithdrawAsync(PublicStoryReviewHarness.CreatorId, first.ItemId);
        var second = await submission.SubmitAsync(PublicStoryReviewHarness.CreatorId, new SubmitPublicStoryRequestDto
        {
            StoryId = PublicStoryReviewHarness.StoryId,
            StoryVersionId = PublicStoryReviewHarness.VersionId,
            ContentCategoryIds = [5]
        });

        var resubmittedItem = harness.Items.Single(x => x.Id == second.ItemId);
        Mock.Get(harness.UnitOfWork.Object.Repository<ModerationItem>())
            .Setup(r => r.GetPagedAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Expression<Func<ModerationItem, bool>>?>(),
                It.IsAny<Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                int page,
                int size,
                Expression<Func<ModerationItem, bool>>? filter,
                Func<IQueryable<ModerationItem>, IOrderedQueryable<ModerationItem>>? orderBy,
                string? include,
                CancellationToken _) =>
            {
                var q = harness.Items.AsQueryable();
                if (filter != null) q = q.Where(filter);
                var total = q.Count();
                if (orderBy != null) q = orderBy(q);
                var pageItems = q.Skip((page - 1) * size).Take(size)
                    .Select(i => new ModerationItem
                    {
                        Id = i.Id,
                        ItemType = i.ItemType,
                        Status = i.Status,
                        Priority = i.Priority,
                        StoryId = i.StoryId,
                        StoryVersionId = i.StoryVersionId,
                        CreatedByUserId = i.CreatedByUserId,
                        ClaimedByUserId = i.ClaimedByUserId,
                        ClaimedAt = i.ClaimedAt,
                        IsResubmission = i.IsResubmission,
                        SlaDueAt = i.SlaDueAt,
                        CreatedAt = i.CreatedAt,
                        RowVersion = i.RowVersion,
                        Story = harness.Stories.FirstOrDefault(s => s.Id == i.StoryId),
                        StoryVersion = harness.Versions.FirstOrDefault(v => v.Id == i.StoryVersionId),
                        CreatedByUser = harness.Users.FirstOrDefault(u => u.Id == i.CreatedByUserId),
                        ClaimedByUser = harness.Users.FirstOrDefault(u => u.Id == i.ClaimedByUserId)
                    }).ToList();
                return (pageItems, total);
            });

        var pendingQueue = await queue.ListAsync(PublicStoryReviewHarness.ModeratorId,
            new ModerationQueueQueryDto { Status = ModerationItemStatus.Pending });
        var listed = Assert.Single(pendingQueue.Items);
        Assert.Equal(second.ItemId, listed.Id);
        Assert.True(listed.IsResubmission);
        Assert.Equal("Phiên bản V1 đã nộp", listed.StoryTitle);
        Assert.Equal(PublicStoryReviewHarness.CreatorId, listed.CreatedByUserId);
        Assert.Equal("Tác Giả Rút Gửi Lại", listed.CreatedByName);

        var claimed = await queue.ClaimAsync(PublicStoryReviewHarness.ModeratorId, second.ItemId);
        Assert.Equal("Phiên bản V1 đã nộp", claimed.StoryTitle);
        Assert.Equal("Tác Giả Rút Gửi Lại", claimed.CreatedByName);

        var opened = await queue.OpenAsync(PublicStoryReviewHarness.ModeratorId, resubmittedItem.Id);
        Assert.Equal("Phiên bản V1 đã nộp", opened.StoryTitle);
        Assert.Equal("Tác Giả Rút Gửi Lại", opened.CreatedByName);
    }
}
