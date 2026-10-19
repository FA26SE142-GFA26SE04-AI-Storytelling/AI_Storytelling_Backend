using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
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

    public readonly Mock<IUnitOfWork> UnitOfWork = new();
    public readonly Mock<IAuditLogWriter> Audit = new();

    public readonly List<ChildProfile> Children = new();
    public readonly List<Story> Stories = new();
    public readonly List<ChildRoadmapItem> Items = new();
    public readonly List<InterventionCase> Cases = new();

    public ChildRoadmapHarness()
    {
        Back(Children);
        Back(Stories);
        Back(Items);
        Back(Cases);

        AddChild(Child, ChildProfileStatus.Active);
        AddChild(OtherChild, ChildProfileStatus.Active);
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
            Status = StoryStatus.Ready, AuthorUserId = ParentId
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

    private void Back<T>(List<T> list) where T : BaseEntity
    {
        var repo = new Mock<IGenericRepository<T>>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => list.FirstOrDefault(x => x.Id == (int)id));
        repo.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<T>)list.Where(predicate.Compile()).ToList());
        repo.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => list.Any(predicate.Compile()));
        UnitOfWork.Setup(u => u.Repository<T>()).Returns(repo.Object);
    }
}
