using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

/// <summary>Dữ liệu giả trong bộ nhớ cho Luồng 3 (đọc, quiz, tương tác lúc nghe).</summary>
public sealed class ReadingHarness
{
    public const int Child = 100;
    public const int OtherChild = 101;
    public const int Story = 10;
    public const int Version = 50;
    public const int CredentialId = 900;
    public const string SessionKey = "key-1";

    public readonly Mock<IUnitOfWork> UnitOfWork = new();

    public readonly List<ChildProfile> Children = new();
    public readonly List<ChildSession> ChildSessions = new();
    public readonly List<Story> Stories = new();
    public readonly List<StoryVersion> Versions = new();
    public readonly List<StoryScene> Scenes = new();
    public readonly List<StorySegment> Segments = new();
    public readonly List<MediaAsset> Assets = new();
    public readonly List<ReadingSession> Sessions = new();
    public readonly List<ReadingProgress> Progress = new();
    public readonly List<StoryVocabulary> Vocabulary = new();
    public readonly List<VocabularyNotebookEntry> Notebook = new();
    public readonly List<TelemetryLog> Telemetry = new();
    public readonly List<InterventionCase> Cases = new();
    public readonly List<ModerationItem> ModerationItems = new();
    public readonly List<QuizItem> Quizzes = new();
    public readonly List<QuizAttempt> Attempts = new();
    public readonly List<Achievement> Achievements = new();
    public readonly List<Badge> Badges = new();
    public readonly List<SafetyPolicy> Policies = new();
    public readonly List<SafetyPolicyCategory> PolicyCategories = new();
    public readonly List<StoryCategory> StoryCategories = new();

    public ReadingHarness()
    {
        Back(Children); Back(ChildSessions); Back(Stories); Back(Versions); Back(Scenes); Back(Segments);
        Back(Assets); Back(Sessions); Back(Progress); Back(Vocabulary); Back(Notebook); Back(Telemetry);
        Back(Cases); Back(ModerationItems); Back(Quizzes); Back(Attempts); Back(Achievements); Back(Badges);
        Back(Policies); Back(PolicyCategories); Back(StoryCategories);

        Children.Add(new ChildProfile
        {
            Id = Child, OwnerUserId = 7, Nickname = "Bé", AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active
        });
        Children.Add(new ChildProfile
        {
            Id = OtherChild, OwnerUserId = 7, Nickname = "Bé khác", AgeBand = AgeBand.Age_6_8,
            Status = ChildProfileStatus.Active
        });
        ChildSessions.Add(new ChildSession
        {
            Id = 1, ChildProfileId = Child, SessionKey = SessionKey, LastActivityAt = DateTime.UtcNow,
            EntryMethod = ChildEntryMethod.EasyLogin, ChildAccessCredentialId = CredentialId
        });
        Stories.Add(new Story
        {
            Id = Story, Title = "Truyện 10", Scope = StoryScope.Parent, AgeBand = "6-8", IsPublished = true,
            Status = StoryStatus.Ready, AuthorUserId = 7, ChildProfileId = Child
        });
        Versions.Add(new StoryVersion
            { Id = Version, StoryId = Story, VersionNo = 1, Title = "Truyện 10", IsCurrent = true, Lesson = "Biết chia sẻ" });
    }

    public StoryScene AddScene(int sceneIndex, string text, int versionId = Version)
    {
        var scene = new StoryScene
        {
            Id = Scenes.Count + 1, StoryVersionId = versionId, SceneIndex = sceneIndex, SceneText = text,
            TextRangeStart = 0, TextRangeEnd = text.Length
        };
        Scenes.Add(scene);
        return scene;
    }

    public StorySegment AddSegment(StoryScene scene, int order, string text)
    {
        var segment = new StorySegment
        {
            Id = Segments.Count + 1, StorySceneId = scene.Id, SegmentOrder = order, TextContent = text,
            StartOffset = 0, EndOffset = text.Length
        };
        Segments.Add(segment);
        return segment;
    }

    public MediaAsset AddAudio(StorySegment segment, string url, string? timings = null)
    {
        var asset = new MediaAsset
        {
            Id = Assets.Count + 1, StoryVersionId = Version, StorySegmentId = segment.Id, Type = MediaType.TtsAudio,
            Status = MediaStatus.Ready, Url = url, WordTimings = timings
        };
        Assets.Add(asset);
        return asset;
    }

    public MediaAsset AddIllustration(StoryScene scene, string url)
    {
        var asset = new MediaAsset
        {
            Id = Assets.Count + 1, StoryVersionId = Version, StorySceneId = scene.Id, SceneIndex = scene.SceneIndex,
            Type = MediaType.Illustration, Status = MediaStatus.Ready, Url = url
        };
        Assets.Add(asset);
        return asset;
    }

    public ReadingSession AddSession(
        SessionStatus status = SessionStatus.Reading, int childId = Child, int storyId = Story)
    {
        var session = new ReadingSession
        {
            Id = Sessions.Count + 1, ChildProfileId = childId, StoryId = storyId, StoryVersionId = Version,
            Status = status, StartedAt = DateTime.UtcNow.AddMinutes(-5), ChildAccessCredentialId = CredentialId
        };
        Sessions.Add(session);
        return session;
    }

    public StoryVocabulary AddVocabulary(int id, string term, string definition = "nghĩa")
    {
        var vocab = new StoryVocabulary { Id = id, StoryVersionId = Version, Term = term, Definition = definition };
        Vocabulary.Add(vocab);
        return vocab;
    }

    public InterventionCase AddHoldMode(
        int childId, InterventionStatus status = InterventionStatus.OpenHoldMode, int? triggeringStoryId = null)
    {
        var holdCase = new InterventionCase
        {
            Id = Cases.Count + 1, ChildProfileId = childId, Status = status, OpenedAt = DateTime.UtcNow.AddDays(-1),
            TriggeringStoryId = triggeringStoryId
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
        repo.Setup(r => r.Delete(It.IsAny<T>())).Callback((T entity) => list.Remove(entity));
        repo.Setup(r => r.CountAsync(It.IsAny<Expression<Func<T, bool>>?>(), It.IsAny<CancellationToken>()))
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
