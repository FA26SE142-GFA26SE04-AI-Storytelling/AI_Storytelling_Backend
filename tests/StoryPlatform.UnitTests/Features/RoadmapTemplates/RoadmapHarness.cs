using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests.Features.RoadmapTemplates;

public sealed class RoadmapHarness
{
    public const int AdminId = 1;
    public const int ParentId = 7;
    public const int ModeratorId = 11;
    public const int OtherModeratorId = 12;
    public const int CreatorId = 20;

    public readonly Mock<IUnitOfWork> UnitOfWork = new();
    public readonly Mock<IAuditLogWriter> Audit = new();
    public readonly List<UserAccount> Users = new();
    public readonly List<Story> Stories = new();
    public readonly List<ReadingRoadmap> Roadmaps = new();
    public readonly List<ReadingRoadmapItem> RoadmapItems = new();
    public readonly List<ContentCategory> Categories = new();
    public readonly List<StoryCategory> StoryCategories = new();
    public readonly List<ModerationItem> Items = new();

    public RoadmapHarness()
    {
        Back(Users);
        Back(Stories);
        Back(Roadmaps);
        Back(RoadmapItems);
        Back(Categories);
        Back(StoryCategories);
        Back(Items);

        AddUser(AdminId, "admin_demo", UserRole.Administrator);
        AddUser(ParentId, "parent_demo1", UserRole.Parent);
        AddUser(ModeratorId, "moderator_demo1", UserRole.Moderator, "Nguyen Thi Lan");
        AddUser(OtherModeratorId, "moderator_demo2", UserRole.Moderator, "Tran Van Minh");
        AddUser(CreatorId, "creator_demo", UserRole.ContentCreator);

        Categories.Add(new ContentCategory
        {
            Id = 5, Code = "friendship", DisplayName = "Tình bạn", IsActive = true
        });
        Categories.Add(new ContentCategory
        {
            Id = 6, Code = "animals", DisplayName = "Động vật", IsActive = true
        });
        Categories.Add(new ContentCategory
        {
            Id = 7, Code = "retired", DisplayName = "Đã ngừng", IsActive = false
        });

        AddStory(10, "6-8", 2);
        AddStory(11, "6-8", 3);
        AddStory(12, "9-10", 4);
    }

    public UserAccount AddUser(int id, string username, UserRole role, string? fullName = null)
    {
        var user = new UserAccount
        {
            Id = id,
            Username = username,
            Email = $"{username}@example.com",
            FullName = fullName ?? username,
            Role = role,
            Status = AccountStatus.Active
        };
        Users.Add(user);
        return user;
    }

    public Story AddStory(int id, string ageBand, int? readingLevel)
    {
        var story = new Story
        {
            Id = id,
            Title = $"Truyện {id}",
            Scope = StoryScope.Creator,
            AgeBand = ageBand,
            ReadingLevel = readingLevel,
            IsPublished = true,
            Status = StoryStatus.Ready,
            PublishedAt = DateTime.UtcNow.AddDays(-id),
            AuthorUserId = CreatorId
        };
        Stories.Add(story);
        return story;
    }

    public ReadingRoadmap AddRoadmap(
        int id,
        AgeBand ageBand,
        int categoryId,
        RoadmapTemplateStatus status,
        int createdBy,
        params int[] storyIds)
    {
        var roadmap = new ReadingRoadmap
        {
            Id = id,
            Name = $"Bộ {id}",
            Description = $"Mô tả bộ {id}",
            AgeBand = ageBand,
            MainCategoryId = categoryId,
            ReadingLevelMin = 1,
            ReadingLevelMax = 3,
            DefaultDaysPerStory = 2,
            Status = status,
            CreatedByUserId = createdBy
        };
        if (status == RoadmapTemplateStatus.Published)
        {
            roadmap.PublishedAt = DateTime.UtcNow.AddDays(-2);
            roadmap.PublishedByUserId = OtherModeratorId;
        }

        if (status == RoadmapTemplateStatus.PendingReview)
        {
            roadmap.SubmittedAt = DateTime.UtcNow.AddHours(-3);
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
        repository.Setup(repo => repo.AddRangeAsync(
                It.IsAny<IEnumerable<T>>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<T> entities, CancellationToken _) =>
            {
                foreach (var entity in entities.ToList())
                {
                    if (entity.Id == 0)
                    {
                        entity.Id = list.Count == 0 ? 1 : list.Max(existing => existing.Id) + 1;
                    }

                    list.Add(entity);
                }

                return Task.CompletedTask;
            });
        repository.Setup(repo => repo.DeleteRange(It.IsAny<IEnumerable<T>>()))
            .Callback((IEnumerable<T> entities) =>
            {
                foreach (var entity in entities.ToList())
                {
                    list.RemoveAll(existing => existing.Id == entity.Id);
                }
            });
        repository.Setup(repo => repo.GetPagedAsync(
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
        UnitOfWork.Setup(unit => unit.Repository<T>()).Returns(repository.Object);
    }
}
