using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests.Features.ContentReports;

public sealed class ContentReportHarness
{
    public const int AdminId = 1;
    public const int ReporterId = 7;
    public const int OtherParentId = 8;
    public const int ModeratorId = 11;
    public const int OtherModeratorId = 12;
    public const int CreatorId = 20;
    public const int ChildId = 100;
    public const int PublicStoryId = 10;
    public const int PrivateStoryId = 20;

    public readonly Mock<IUnitOfWork> UnitOfWork = new();
    public readonly Mock<IAuditLogWriter> Audit = new();
    public readonly Mock<INotificationService> Notifications = new();

    public readonly List<UserAccount> Users = new();
    public readonly List<Story> Stories = new();
    public readonly List<StoryVersion> Versions = new();
    public readonly List<ChildProfile> Children = new();
    public readonly List<SupervisionRelationship> Relationships = new();
    public readonly List<ContentReport> Reports = new();
    public readonly List<ModerationItem> Items = new();

    public ContentReportHarness()
    {
        Back(Users);
        Back(Stories);
        Back(Versions);
        Back(Children);
        Back(Relationships);
        Back(Reports);
        Back(Items);

        AddUser(AdminId, "admin_demo", UserRole.Administrator);
        AddUser(ReporterId, "parent_demo1", UserRole.Parent);
        AddUser(OtherParentId, "parent_demo2", UserRole.Parent);
        AddUser(ModeratorId, "moderator_demo1", UserRole.Moderator);
        AddUser(OtherModeratorId, "moderator_demo2", UserRole.Moderator);
        AddUser(CreatorId, "creator_demo", UserRole.ContentCreator);
        AddChild(ChildId, ReporterId, AgeBand.Age_6_8);

        Stories.Add(new Story
        {
            Id = PublicStoryId,
            Title = "Cậu bé và chú rùa",
            Scope = StoryScope.Creator,
            AgeBand = "6-8",
            IsPublished = true,
            Status = StoryStatus.Ready,
            AuthorUserId = CreatorId
        });
        Versions.Add(new StoryVersion
        {
            Id = 50,
            StoryId = PublicStoryId,
            VersionNo = 1,
            Title = "v1",
            ReviewStatus = VersionReviewStatus.Approved,
            ReviewedAt = DateTime.UtcNow.AddDays(-5)
        });
        Versions.Add(new StoryVersion
        {
            Id = 51, StoryId = PublicStoryId, VersionNo = 2, Title = "v2"
        });

        Stories.Add(new Story
        {
            Id = PrivateStoryId,
            Title = "Truyện của bé",
            Scope = StoryScope.Parent,
            AgeBand = "6-8",
            ChildProfileId = ChildId,
            IsPublished = true,
            Status = StoryStatus.Ready,
            AuthorUserId = ReporterId
        });
        Versions.Add(new StoryVersion
        {
            Id = 60,
            StoryId = PrivateStoryId,
            VersionNo = 1,
            Title = "bản riêng",
            IsCurrent = true
        });
    }

    public UserAccount AddUser(int id, string username, UserRole role)
    {
        var user = new UserAccount
        {
            Id = id,
            Username = username,
            Email = $"{username}@example.com",
            FullName = username,
            Role = role,
            Status = AccountStatus.Active
        };
        Users.Add(user);
        return user;
    }

    public ChildProfile AddChild(
        int childId,
        int supervisorId,
        AgeBand ageBand,
        ChildProfileStatus status = ChildProfileStatus.Active,
        SupervisorRole role = SupervisorRole.Owner)
    {
        var child = new ChildProfile
        {
            Id = childId,
            OwnerUserId = supervisorId,
            Nickname = $"Bé {childId}",
            AgeBand = ageBand,
            Status = status
        };
        Children.Add(child);
        Relationships.Add(new SupervisionRelationship
        {
            Id = Relationships.Count + 1,
            ChildProfileId = childId,
            SupervisorUserId = supervisorId,
            SupervisorRole = role
        });
        return child;
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
        repository.Setup(repo => repo.ExistsAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                list.Any(predicate.Compile()));
        repository.Setup(repo => repo.CountAsync(
                It.IsAny<Expression<Func<T, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) =>
                predicate == null ? list.Count : list.Count(predicate.Compile()));
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
        UnitOfWork.Setup(unit => unit.Repository<T>()).Returns(repository.Object);
    }
}
