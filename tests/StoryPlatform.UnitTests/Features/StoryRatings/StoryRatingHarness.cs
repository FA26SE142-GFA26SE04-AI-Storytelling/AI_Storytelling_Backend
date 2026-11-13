using System.Linq.Expressions;
using System.Text.Json;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Application.Features.StoryRatings.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests.Features.StoryRatings;

/// <summary>Dữ liệu trong bộ nhớ cho test đánh giá truyện: mỗi repository đọc/ghi thẳng vào một List.</summary>
public sealed class StoryRatingHarness
{
    public const int AdminId = 1;
    public const int ParentId = 2;
    public const int OtherParentId = 3;
    public const int StrangerParentId = 4;
    public const int ModeratorId = 11;
    public const int OtherModeratorId = 12;
    public const int CreatorId = 20;
    public const int OtherCreatorId = 21;
    public const int ChildId = 100;
    public const int OtherChildId = 101;
    public const int PublicStoryId = 10;
    public const int DraftStoryId = 30;
    public const int FamilyStoryId = 40;

    public readonly Mock<IUnitOfWork> UnitOfWork = new();
    public readonly Mock<IAuditLogWriter> Audit = new();
    public readonly Mock<INotificationService> Notifications = new();
    public readonly List<(int Recipient, NotificationType Type, string? Payload)> Sent = new();

    public readonly List<UserAccount> Users = new();
    public readonly List<Story> Stories = new();
    public readonly List<ChildProfile> Children = new();
    public readonly List<ReadingSession> Sessions = new();
    public readonly List<StoryRating> Ratings = new();
    public readonly List<StoryRatingReport> Reports = new();
    public readonly List<ModerationItem> Items = new();

    public StoryRatingHarness()
    {
        Back(Users);
        Back(Stories);
        Back(Children);
        Back(Sessions);
        Back(Ratings);
        Back(Reports);
        Back(Items);
        Notifications
            .Setup(n => n.CreateAsync(It.IsAny<int>(), It.IsAny<NotificationType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((int recipient, NotificationType type, string? payload, CancellationToken _) =>
                Sent.Add((recipient, type, payload)));

        AddUser(AdminId, "admin_demo", "Admin Demo", UserRole.Administrator);
        AddUser(ParentId, "parent_demo1", "Trần Thị Hoa", UserRole.User);
        AddUser(OtherParentId, "parent_demo2", "Lê Văn Minh", UserRole.User);
        AddUser(StrangerParentId, "parent_demo3", "Phạm Thu", UserRole.User);
        AddUser(ModeratorId, "moderator_demo1", "Nguyễn Thị Lan", UserRole.Moderator);
        AddUser(OtherModeratorId, "moderator_demo2", "Trần Văn Minh", UserRole.Moderator);
        AddUser(CreatorId, "creator_demo", "Lê Thị Mai", UserRole.ContentCreator);
        AddUser(OtherCreatorId, "creator_demo2", "Đỗ Văn Nam", UserRole.ContentCreator);

        Stories.Add(new Story
        {
            Id = PublicStoryId, Title = "Cá heo tốt bụng", Scope = StoryScope.Creator, IsPublished = true,
            Status = StoryStatus.Ready, AuthorUserId = CreatorId, AgeBand = "6-8"
        });
        Stories.Add(new Story
        {
            Id = DraftStoryId, Title = "Bản nháp", Scope = StoryScope.Creator, IsPublished = false,
            Status = StoryStatus.Draft, AuthorUserId = CreatorId, AgeBand = "6-8"
        });
        Stories.Add(new Story
        {
            Id = FamilyStoryId, Title = "Truyện của bé", Scope = StoryScope.Parent, IsPublished = true,
            Status = StoryStatus.Ready, AuthorUserId = ParentId, ChildProfileId = ChildId, AgeBand = "6-8"
        });

        Children.Add(new ChildProfile
        {
            Id = ChildId, OwnerUserId = ParentId, Nickname = "Bé An", AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active
        });
        Children.Add(new ChildProfile
        {
            Id = OtherChildId, OwnerUserId = OtherParentId, Nickname = "Bé Chi", AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active
        });
        Sessions.Add(new ReadingSession { Id = 1, ChildProfileId = ChildId, StoryId = PublicStoryId, Status = SessionStatus.Completed });
        Sessions.Add(new ReadingSession { Id = 2, ChildProfileId = OtherChildId, StoryId = PublicStoryId, Status = SessionStatus.Started });
    }

    public StoryRatingService Service() => new(
        UnitOfWork.Object, new ModerationQueueService(UnitOfWork.Object, Audit.Object), Notifications.Object, Audit.Object);

    public StoryRatingModerationService ModerationService() => new(
        UnitOfWork.Object, Notifications.Object, Audit.Object);

    public UserAccount AddUser(int id, string username, string fullName, UserRole role)
    {
        var user = new UserAccount
        {
            Id = id, Username = username, Email = $"{username}@example.com", FullName = fullName,
            Role = role, Status = AccountStatus.Active
        };
        Users.Add(user);
        return user;
    }

    public StoryRating AddRating(int id, int reviewerId, int rating, StoryRatingStatus status,
        string? comment = null, string? hiddenReason = null, int storyId = PublicStoryId)
    {
        var entity = new StoryRating
        {
            Id = id, StoryId = storyId, ReviewerUserId = reviewerId, Rating = rating, Status = status,
            Comment = comment, HiddenReason = hiddenReason
        };
        Ratings.Add(entity);
        return entity;
    }

    public ModerationItem AddOpenItem(int id, int ratingId, int createdBy, string source = "Screening",
        string? reasonCode = null, int? claimedBy = null, DateTime? claimedAt = null)
    {
        var item = new ModerationItem
        {
            Id = id,
            ItemType = ModerationItemType.StoryRating,
            Status = claimedBy.HasValue ? ModerationItemStatus.Claimed : ModerationItemStatus.Pending,
            Priority = ModerationPriority.Low,
            StoryId = Ratings.Single(rating => rating.Id == ratingId).StoryId,
            StoryRatingId = ratingId,
            CreatedByUserId = createdBy,
            ClaimedByUserId = claimedBy,
            ClaimedAt = claimedBy.HasValue ? claimedAt ?? DateTime.UtcNow.AddMinutes(-5) : null,
            Flags = JsonSerializer.Serialize(new { source, reasonCode })
        };
        Items.Add(item);
        return item;
    }

    public StoryRatingReport AddReport(int id, int ratingId, int reporterId, int itemId,
        StoryRatingReportReason reason = StoryRatingReportReason.Offensive, string? note = null)
    {
        var report = new StoryRatingReport
        {
            Id = id, StoryRatingId = ratingId, ReporterUserId = reporterId, ModerationItemId = itemId,
            Reason = reason, Note = note, Status = StoryRatingReportStatus.Open
        };
        Reports.Add(report);
        return report;
    }

    private void Back<T>(List<T> list) where T : BaseEntity
    {
        var repository = new Mock<IGenericRepository<T>>();
        repository.Setup(repo => repo.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => list.FirstOrDefault(entity => entity.Id == (int)id));
        repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<T>)list.Where(predicate.Compile()).ToList());
        repository.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                list.FirstOrDefault(predicate.Compile()));
        repository.Setup(repo => repo.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => list.Any(predicate.Compile()));
        repository.Setup(repo => repo.CountAsync(It.IsAny<Expression<Func<T, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) =>
                predicate == null ? list.Count : list.Count(predicate.Compile()));
        repository.Setup(repo => repo.Query()).Returns(() => list.AsQueryable());
        repository.Setup(repo => repo.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((T entity, CancellationToken _) =>
            {
                if (entity.Id == 0)
                {
                    entity.Id = list.Count == 0 ? 1 : list.Max(existing => existing.Id) + 1;
                }

                list.Add(entity);
                return entity;
            });
        repository.Setup(repo => repo.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int pageIndex, int pageSize, Expression<Func<T, bool>>? filter,
                Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy, string? _, CancellationToken _) =>
            {
                IQueryable<T> query = list.AsQueryable();
                if (filter != null)
                {
                    query = query.Where(filter);
                }

                var total = query.Count();
                if (orderBy != null)
                {
                    query = orderBy(query);
                }

                var items = query.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
                return ((IReadOnlyList<T>)items, total);
            });
        UnitOfWork.Setup(unit => unit.Repository<T>()).Returns(repository.Object);
    }
}
