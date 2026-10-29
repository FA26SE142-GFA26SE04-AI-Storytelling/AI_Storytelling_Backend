using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

/// <summary>Dữ liệu giả trong bộ nhớ cho lộ trình của trẻ (WF-L4A).</summary>
public sealed class ChildRoadmapHarness
{
    public const int ParentId = 7;
    public const int ModeratorId = 11;
    public const int Child = 100;
    public const int OtherChild = 101;
    public const int SecondSupervisorId = 8;
    public const int StrangerId = 9;

    public readonly Mock<IUnitOfWork> UnitOfWork = new();
    public readonly Mock<IAuditLogWriter> Audit = new();
    public readonly Mock<INotificationService> Notifications = new();

    public readonly List<ChildProfile> Children = new();
    public readonly List<Story> Stories = new();
    public readonly List<ChildRoadmapItem> Items = new();
    public readonly List<InterventionCase> Cases = new();
    public readonly List<SafetyPolicy> Policies = new();
    public readonly List<SafetyPolicyCategory> PolicyCategories = new();
    public readonly List<StoryCategory> StoryCategories = new();
    public readonly List<UserAccount> Users = new();
    public readonly List<LearningProfile> Learning = new();
    public readonly List<ReadingSession> Sessions = new();
    public readonly List<ReadingRoadmap> Roadmaps = new();
    public readonly List<ReadingRoadmapItem> RoadmapItems = new();
    public readonly List<ContentCategory> Categories = new();

    public ChildRoadmapHarness()
    {
        Back(Children);
        Back(Stories);
        Back(Items);
        Back(Cases);
        Back(Policies);
        Back(PolicyCategories);
        Back(StoryCategories);
        Back(Users);
        Back(Learning);
        Back(Sessions);
        Back(Roadmaps);
        Back(RoadmapItems);
        Back(Categories);

        AddChild(Child, ChildProfileStatus.Active);
        AddChild(OtherChild, ChildProfileStatus.Active);
        AddUser(ParentId, UserRole.User);
        AddUser(SecondSupervisorId, UserRole.User);
        AddUser(StrangerId, UserRole.User);
        Categories.Add(new ContentCategory
            { Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true });
        Categories.Add(new ContentCategory
            { Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true });
        foreach (var id in new[] { 10, 11, 12, 13 })
        {
            AddStory(id);
        }
    }

    public ChildProfile AddChild(int id, ChildProfileStatus status)
    {
        var child = new ChildProfile
        {
            Id = id, OwnerUserId = ParentId, Nickname = $"Be {id}", AgeBand = AgeBand.Age_6_8, Status = status
        };
        Children.Add(child);
        return child;
    }

    public Story AddStory(int id)
    {
        var story = new Story
        {
            Id = id, Title = $"Truyện {id}", Scope = StoryScope.Parent, AgeBand = "6-8", IsPublished = true,
            Status = StoryStatus.Ready, AuthorUserId = ParentId, ChildProfileId = Child
        };
        Stories.Add(story);
        return story;
    }

    /// <summary>Thêm một bước vào lộ trình; Position mặc định = số bước hiện có của trẻ + 1.</summary>
    public ChildRoadmapItem AddItem(
        int id, int childId, int storyId, ChildRoadmapItemStatus status, int? position = null, int? expectedDays = null)
    {
        var item = new ChildRoadmapItem
        {
            Id = id, ChildProfileId = childId, StoryId = storyId, Status = status, AddedByUserId = ParentId,
            Position = position ?? (Items.Count(existing => existing.ChildProfileId == childId) + 1),
            ExpectedDays = expectedDays
        };
        if (status == ChildRoadmapItemStatus.Open)
        {
            item.OpenedAt = DateTime.UtcNow.AddHours(-1);
        }

        Items.Add(item);
        return item;
    }

    public InterventionCase AddHoldMode(int childId, InterventionStatus status = InterventionStatus.OpenHoldMode)
    {
        var holdCase = new InterventionCase
        {
            Id = Cases.Count + 1, ChildProfileId = childId, Status = status, OpenedAt = DateTime.UtcNow.AddDays(-1)
        };
        Cases.Add(holdCase);
        return holdCase;
    }

    public SafetyPolicy AddPolicy(int childId, int maxLength = 0)
    {
        var policy = new SafetyPolicy
        {
            Id = Policies.Count + 1,
            ChildProfileId = childId,
            MaxStoryLength = maxLength
        };
        Policies.Add(policy);
        return policy;
    }

    public void Block(int childId, int categoryId)
    {
        var policy = Policies.FirstOrDefault(existing => existing.ChildProfileId == childId) ?? AddPolicy(childId);
        PolicyCategories.Add(new SafetyPolicyCategory
        {
            Id = PolicyCategories.Count + 1,
            SafetyPolicyId = policy.Id,
            ContentCategoryId = categoryId,
            Rule = PolicyRule.Blocked
        });
    }

    public void Tag(int storyId, int categoryId) => StoryCategories.Add(new StoryCategory
    {
        Id = StoryCategories.Count + 1,
        StoryId = storyId,
        ContentCategoryId = categoryId
    });

    public UserAccount AddUser(int id, UserRole role, AccountStatus status = AccountStatus.Active)
    {
        var user = new UserAccount
        {
            Id = id,
            Username = $"user_{id}",
            Email = $"user_{id}@example.com",
            FullName = $"User {id}",
            Role = role,
            Status = status
        };
        Users.Add(user);
        return user;
    }

    public LearningProfile AddLearning(int childId, int readingLevel)
    {
        var profile = new LearningProfile
        {
            Id = Learning.Count + 1,
            ChildProfileId = childId,
            ReadingLevel = readingLevel
        };
        Learning.Add(profile);
        return profile;
    }

    public ReadingRoadmap AddTemplate(
        int id, AgeBand age, int categoryId, RoadmapTemplateStatus status, params int[] storyIds)
    {
        var roadmap = new ReadingRoadmap
        {
            Id = id,
            Name = $"Bộ {id}",
            Description = $"Mô tả bộ {id}",
            AgeBand = age,
            MainCategoryId = categoryId,
            ReadingLevelMin = 1,
            ReadingLevelMax = 3,
            DefaultDaysPerStory = 2,
            Status = status,
            CreatedByUserId = ModeratorId
        };
        if (status == RoadmapTemplateStatus.Published)
        {
            roadmap.PublishedAt = DateTime.UtcNow.AddDays(-2);
        }

        if (status == RoadmapTemplateStatus.Archived)
        {
            roadmap.ArchivedAt = DateTime.UtcNow.AddDays(-1);
        }

        Roadmaps.Add(roadmap);
        for (var position = 0; position < storyIds.Length; position++)
        {
            RoadmapItems.Add(new ReadingRoadmapItem
            {
                Id = id * 100 + position,
                ReadingRoadmapId = id,
                StoryId = storyIds[position],
                Position = position + 1
            });
        }

        return roadmap;
    }

    public void SetItemDays(int roadmapId, int storyId, int days) =>
        RoadmapItems.Single(item => item.ReadingRoadmapId == roadmapId && item.StoryId == storyId).Days = days;

    private void Back<T>(List<T> list) where T : BaseEntity
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => list.FirstOrDefault(x => x.Id == (int)id));
        repo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<T>)list.Where(predicate.Compile()).ToList());
        repo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                list.FirstOrDefault(predicate.Compile()));
        repo.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((T entity, CancellationToken _) =>
            {
                if (entity.Id == 0)
                {
                    entity.Id = list.Count == 0 ? 1 : list.Max(x => x.Id) + 1;
                }

                list.Add(entity);
                return entity;
            });
        repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<T>>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<T> entities, CancellationToken _) =>
            {
                foreach (var entity in entities.ToList())
                {
                    if (entity.Id == 0)
                    {
                        entity.Id = list.Count == 0 ? 1 : list.Max(x => x.Id) + 1;
                    }

                    list.Add(entity);
                }

                return Task.CompletedTask;
            });
        repo.Setup(r => r.CountAsync(
                It.IsAny<Expression<Func<T, bool>>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) =>
                predicate == null ? list.Count : list.Count(predicate.Compile()));
        repo.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => list.Any(predicate.Compile()));
        repo.Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<Func<IQueryable<T>, IOrderedQueryable<T>>?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
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
        UnitOfWork.Setup(u => u.Repository<T>()).Returns(repo.Object);
    }
}
