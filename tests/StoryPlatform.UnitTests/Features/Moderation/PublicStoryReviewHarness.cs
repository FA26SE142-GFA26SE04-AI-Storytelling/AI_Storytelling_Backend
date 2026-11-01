using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class PublicStoryReviewHarness
{
    public const int ModeratorId = 11;
    public const int OtherModeratorId = 12;
    public const int CreatorId = 20;
    public const int ItemId = 100;
    public const int StoryId = 10;
    public const int VersionId = 50;

    public readonly Mock<IUnitOfWork> UnitOfWork = new();
    public readonly Mock<IAuditLogWriter> Audit = new();
    public readonly Mock<INotificationService> Notifications = new();

    public readonly List<UserAccount> Users = new();
    public readonly List<StoryGenerationJob> Jobs = new();
    public readonly List<ModerationItem> Items = new();
    public readonly List<ParentStoryOperation> ParentOperations = new();
    public readonly List<Story> Stories = new();
    public readonly List<StoryVersion> Versions = new();
    public readonly List<StoryScene> Scenes = new();
    public readonly List<StorySegment> Segments = new();
    public readonly List<IllustrationBeat> Beats = new();
    public readonly List<MediaAsset> Assets = new();
    public readonly List<QuizItem> Quizzes = new();
    public readonly List<StoryVocabulary> Vocabulary = new();
    public readonly List<ListeningInteraction> ListeningInteractions = new();
    public readonly List<ListeningInteractionOption> ListeningOptions = new();
    public readonly List<StoryCategory> StoryCategories = new();
    public readonly List<ContentCategory> ContentCategories = new();

    public PublicStoryReviewService Sut { get; }

    public PublicStoryReviewHarness()
    {
        Back(Users);
        Back(Items); Back(ParentOperations);
        Back(Jobs);
        Back(Stories);
        Back(Versions);
        Back(Scenes);
        Back(Segments);
        Back(Beats);
        Back(Assets);
        Back(Quizzes);
        Back(Vocabulary);
        Back(ListeningInteractions);
        Back(ListeningOptions);
        Back(StoryCategories);
        Back(ContentCategories);

        Users.Add(new UserAccount
        {
            Id = ModeratorId,
            Username = "moderator_demo1",
            Email = "moderator1@example.com",
            FullName = "Nguyen Thi Lan",
            Role = UserRole.Moderator,
            Status = AccountStatus.Active
        });
        Users.Add(new UserAccount
        {
            Id = OtherModeratorId,
            Username = "moderator_demo2",
            Email = "moderator2@example.com",
            FullName = "Tran Van Minh",
            Role = UserRole.Moderator,
            Status = AccountStatus.Active
        });
        Users.Add(new UserAccount
        {
            Id = CreatorId,
            Username = "creator_demo",
            Email = "creator@example.com",
            FullName = "Content Creator",
            Role = UserRole.ContentCreator,
            Status = AccountStatus.Active
        });

        Stories.Add(new Story
        {
            Id = StoryId,
            Title = "Cậu bé và chú rùa",
            Description = "Truyện về sự kiên nhẫn",
            Genre = "fable",
            AgeBand = "6-8",
            ReadingLevel = 2,
            Language = "vi",
            AuthorUserId = CreatorId,
            Status = StoryStatus.Ready,
            Scope = StoryScope.Creator
        });
        Versions.Add(new StoryVersion
        {
            Id = VersionId,
            StoryId = StoryId,
            VersionNo = 1,
            IsCurrent = true,
            Title = "Cậu bé và chú rùa",
            Content = "Mở đầu. Giữa truyện. Kết thúc.",
            Lesson = "Kiên nhẫn sẽ thắng",
            OutlineOpening = "mở",
            OutlineDevelopment = "giữa",
            OutlineEnding = "kết"
        });
        Items.Add(new ModerationItem
        {
            Id = ItemId,
            ItemType = ModerationItemType.PublicStoryReview,
            Status = ModerationItemStatus.Claimed,
            ClaimedByUserId = ModeratorId,
            ClaimedAt = DateTime.UtcNow.AddMinutes(-10),
            StoryId = StoryId,
            StoryVersionId = VersionId,
            CreatedByUserId = CreatorId
        });

        Sut = new PublicStoryReviewService(UnitOfWork.Object, Audit.Object, Notifications.Object);
    }

    public int AddScene(int sceneId, int index, bool withAudio = true, bool withWordTimings = true)
    {
        Scenes.Add(new StoryScene
        {
            Id = sceneId,
            StoryVersionId = VersionId,
            SceneIndex = index,
            TextRangeStart = 0,
            TextRangeEnd = 5,
            SceneText = $"Scene {index}"
        });
        var beatId = sceneId * 10;
        Beats.Add(new IllustrationBeat
        {
            Id = beatId,
            StorySceneId = sceneId,
            BeatOrder = 1,
            StartOffset = 0,
            EndOffset = 5,
            VisualFocus = "chú rùa"
        });
        Assets.Add(new MediaAsset
        {
            Id = sceneId * 100,
            StoryVersionId = VersionId,
            StorySceneId = sceneId,
            IllustrationBeatId = beatId,
            Type = MediaType.Illustration,
            Status = MediaStatus.Ready,
            Url = $"https://cdn.test/img{sceneId}.png"
        });

        var segmentId = sceneId * 1000;
        Segments.Add(new StorySegment
        {
            Id = segmentId,
            StorySceneId = sceneId,
            SegmentOrder = 1,
            StartOffset = 0,
            EndOffset = 5,
            TextContent = $"Scene {index}"
        });
        if (withAudio)
        {
            Assets.Add(new MediaAsset
            {
                Id = sceneId * 100 + 1,
                StoryVersionId = VersionId,
                StorySceneId = sceneId,
                StorySegmentId = segmentId,
                Type = MediaType.TtsAudio,
                Status = MediaStatus.Ready,
                Url = $"https://cdn.test/audio{sceneId}.mp3",
                WordTimings = withWordTimings ? "[{\"w\":\"a\",\"t\":0}]" : null
            });
        }

        return segmentId;
    }

    public int MakeApprovable()
    {
        var segmentId = AddScene(1, 1);
        Items[0].ReviewProgress = $"{{\"viewed\":{{\"scene\":[1],\"audio\":[{segmentId}]}}}}";
        Items[0].Checklist =
            "{\"safety\":true,\"ageFit\":true,\"education\":true,\"language\":true,"
            + "\"copyright\":true,\"image\":true,\"audio\":true,\"interaction\":true}";
        return segmentId;
    }

    public StoryVersion CloneVersion(int sourceVersionId, int newVersionId)
    {
        var source = Versions.Single(version => version.Id == sourceVersionId);
        var copy = new StoryVersion
        {
            Id = newVersionId,
            StoryId = source.StoryId,
            VersionNo = source.VersionNo + 1,
            IsCurrent = true,
            Title = source.Title,
            Content = source.Content,
            Lesson = source.Lesson,
            OutlineOpening = source.OutlineOpening,
            OutlineDevelopment = source.OutlineDevelopment,
            OutlineEnding = source.OutlineEnding
        };
        source.IsCurrent = false;
        Versions.Add(copy);

        var offset = newVersionId * 100000;
        var sceneMap = new Dictionary<int, int>();
        var segmentMap = new Dictionary<int, int>();
        var beatMap = new Dictionary<int, int>();

        foreach (var scene in Scenes.Where(scene => scene.StoryVersionId == sourceVersionId).ToList())
        {
            sceneMap[scene.Id] = scene.Id + offset;
            Scenes.Add(new StoryScene
            {
                Id = scene.Id + offset,
                StoryVersionId = newVersionId,
                SceneIndex = scene.SceneIndex,
                TextRangeStart = scene.TextRangeStart,
                TextRangeEnd = scene.TextRangeEnd,
                SceneText = scene.SceneText,
                VisualDescription = scene.VisualDescription
            });
        }

        foreach (var segment in Segments.Where(segment => sceneMap.ContainsKey(segment.StorySceneId)).ToList())
        {
            segmentMap[segment.Id] = segment.Id + offset;
            Segments.Add(new StorySegment
            {
                Id = segment.Id + offset,
                StorySceneId = sceneMap[segment.StorySceneId],
                SegmentOrder = segment.SegmentOrder,
                StartOffset = segment.StartOffset,
                EndOffset = segment.EndOffset,
                TextContent = segment.TextContent
            });
        }

        foreach (var beat in Beats.Where(beat => sceneMap.ContainsKey(beat.StorySceneId)).ToList())
        {
            beatMap[beat.Id] = beat.Id + offset;
            Beats.Add(new IllustrationBeat
            {
                Id = beat.Id + offset,
                StorySceneId = sceneMap[beat.StorySceneId],
                BeatOrder = beat.BeatOrder,
                StartOffset = beat.StartOffset,
                EndOffset = beat.EndOffset,
                VisualFocus = beat.VisualFocus
            });
        }

        foreach (var asset in Assets.Where(asset => asset.StoryVersionId == sourceVersionId).ToList())
        {
            Assets.Add(new MediaAsset
            {
                Id = asset.Id + offset,
                StoryVersionId = newVersionId,
                StorySceneId = asset.StorySceneId.HasValue ? sceneMap[asset.StorySceneId.Value] : null,
                IllustrationBeatId = asset.IllustrationBeatId.HasValue
                    ? beatMap[asset.IllustrationBeatId.Value]
                    : null,
                StorySegmentId = asset.StorySegmentId.HasValue
                    ? segmentMap[asset.StorySegmentId.Value]
                    : null,
                Type = asset.Type,
                Status = asset.Status,
                Url = asset.Url,
                WordTimings = asset.WordTimings
            });
        }

        foreach (var quiz in Quizzes.Where(quiz => quiz.StoryVersionId == sourceVersionId).ToList())
        {
            Quizzes.Add(new QuizItem
            {
                Id = quiz.Id + offset,
                StoryVersionId = newVersionId,
                Type = quiz.Type,
                Question = quiz.Question,
                CorrectAnswer = quiz.CorrectAnswer,
                Choices = quiz.Choices
            });
        }

        foreach (var word in Vocabulary.Where(word => word.StoryVersionId == sourceVersionId).ToList())
        {
            Vocabulary.Add(new StoryVocabulary
            {
                Id = word.Id + offset,
                StoryVersionId = newVersionId,
                Term = word.Term,
                Definition = word.Definition
            });
        }

        return copy;
    }

    private void Back<T>(List<T> list) where T : BaseEntity
    {
        var repository = new Mock<IGenericRepository<T>>();
        repository.Setup(r => r.GetByIdAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((object id, CancellationToken _) => list.FirstOrDefault(x => x.Id == (int)id));
        repository.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) => list.FirstOrDefault(predicate.Compile()));
        repository.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) => list.Any(predicate.Compile()));
        repository.Setup(r => r.CountAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) => predicate == null ? list.Count : list.Count(predicate.Compile()));
        repository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, string? _, CancellationToken _) =>
                (IReadOnlyList<T>)list.Where(predicate.Compile()).ToList());
        repository.Setup(r => r.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((T entity, CancellationToken _) =>
            {
                if (entity.Id == 0)
                {
                    entity.Id = list.Count == 0 ? 1 : list.Max(x => x.Id) + 1;
                }

                list.Add(entity);
                return entity;
            });
        repository.Setup(r => r.Delete(It.IsAny<T>())).Callback((T entity) => list.Remove(entity));
        UnitOfWork.Setup(u => u.Repository<T>()).Returns(repository.Object);
    }
}
