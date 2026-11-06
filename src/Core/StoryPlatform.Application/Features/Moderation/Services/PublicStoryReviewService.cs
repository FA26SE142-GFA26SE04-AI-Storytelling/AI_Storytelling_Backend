using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.MediaStorage.Interfaces;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

using System.Text.Json;

namespace StoryPlatform.Application.Features.Moderation.Services;

public partial class PublicStoryReviewService : IPublicStoryReviewService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;
    private readonly IMediaStorage? _mediaStorage;

    /// <summary>Đường dẫn media trong gói duyệt sống 1 giờ; mỗi lần mở lại gói hoặc sau mỗi thao tác sẽ được ký lại.</summary>
    private static readonly TimeSpan ReviewMediaUrlLifetime = TimeSpan.FromHours(1);

    public PublicStoryReviewService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        IMediaStorage? mediaStorage = null)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
        _mediaStorage = mediaStorage;
    }

    private sealed record ReviewContext(UserAccount Actor, ModerationItem Item, Story Story, StoryVersion Version);

    private sealed record ReviewContent(
        IReadOnlyList<StoryScene> Scenes,
        IReadOnlyList<StorySegment> Segments,
        IReadOnlyList<IllustrationBeat> Beats,
        IReadOnlyList<MediaAsset> Assets,
        IReadOnlyList<QuizItem> Quizzes,
        IReadOnlyList<StoryVocabulary> Vocabulary,
        IReadOnlyList<ListeningInteraction> Listening,
        IReadOnlyList<ListeningInteractionOption> ListeningOptions,
        IReadOnlyList<ContentCategory> ProposedCategories);

    public async Task<PublicReviewPackageDto> GetPackageAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var content = await LoadContentAsync(context.Version.Id, cancellationToken);
        content = await WithProposedCategoriesAsync(content, context.Item, cancellationToken);
        var package = await BuildPackageAsync(context, content, cancellationToken);

        await _auditLogWriter.LogAsync(
            context.Actor.Id, "PublicStoryReviewPackageOpened", nameof(ModerationItem), context.Item.Id,
            beforeState: null,
            afterState: new { storyId = context.Story.Id, storyVersionId = context.Version.Id },
            cancellationToken: cancellationToken);

        return package;
    }

    private async Task<ReviewContext> LoadClaimedContextAsync(
        int actorUserId, int itemId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null
            || actor.Status != AccountStatus.Active
            || actor.Role is not (UserRole.Moderator or UserRole.Administrator))
        {
            throw new ForbiddenException(
                "Chỉ Moderator hoặc Administrator đang hoạt động mới duyệt được truyện Public Library.");
        }

        var item = await _unitOfWork.Repository<ModerationItem>().GetByIdAsync(itemId, cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", itemId);

        if (item.ItemType != ModerationItemType.PublicStoryReview)
        {
            throw new BadRequestException("Mục này không phải mục duyệt truyện Public Library.");
        }

        if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được đọc mục đang do chính mình nhận.");
        }

        if (item.ClaimedAt == null || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow)
        {
            throw new ConflictException(
                "Mục đã quá 2 giờ kể từ lúc nhận và sẽ được trả về hàng đợi. Vui lòng nhận lại.");
        }

        if (item.StoryId == null || item.StoryVersionId == null)
        {
            throw new BadRequestException("Mục chưa gắn truyện hoặc phiên bản để duyệt.");
        }

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(item.StoryId.Value, cancellationToken)
                    ?? throw new NotFoundException("Truyện", item.StoryId.Value);
        var version = await _unitOfWork.Repository<StoryVersion>()
                          .GetByIdAsync(item.StoryVersionId.Value, cancellationToken)
                      ?? throw new NotFoundException("Phiên bản truyện", item.StoryVersionId.Value);
        if (version.StoryId != story.Id)
        {
            throw new BadRequestException("Phiên bản không thuộc truyện của mục này.");
        }

        return new ReviewContext(actor, item, story, version);
    }

    private async Task<ReviewContent> LoadContentAsync(int versionId, CancellationToken cancellationToken)
    {
        var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(
            x => x.StoryVersionId == versionId, cancellationToken: cancellationToken))
            .OrderBy(x => x.SceneIndex).ToList();
        var sceneIds = scenes.Select(x => x.Id).ToList();

        var segments = await _unitOfWork.Repository<StorySegment>().FindAsync(
            x => sceneIds.Contains(x.StorySceneId), cancellationToken: cancellationToken);
        var beats = await _unitOfWork.Repository<IllustrationBeat>().FindAsync(
            x => sceneIds.Contains(x.StorySceneId), cancellationToken: cancellationToken);
        var assets = await _unitOfWork.Repository<MediaAsset>().FindAsync(
            x => x.IsCurrent && x.StoryVersionId == versionId, cancellationToken: cancellationToken);
        var quizzes = await _unitOfWork.Repository<QuizItem>().FindAsync(
            x => x.StoryVersionId == versionId, cancellationToken: cancellationToken);
        var vocabulary = await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
            x => x.StoryVersionId == versionId, cancellationToken: cancellationToken);
        // Dòng xoá mềm là bản cũ, không đưa cho Moderator.
        var listening = (await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
                x => x.StoryVersionId == versionId && !x.IsDeleted, cancellationToken: cancellationToken))
            .OrderBy(x => x.AnchorOffset).ToList();
        var listeningIds = listening.Select(x => x.Id).ToList();
        var listeningOptions = await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
            x => listeningIds.Contains(x.ListeningInteractionId) && !x.IsDeleted, cancellationToken: cancellationToken);
        return new ReviewContent(scenes, segments, beats, assets, quizzes, vocabulary, listening, listeningOptions, []);
    }

    private async Task<ReviewContent> WithProposedCategoriesAsync(ReviewContent content, ModerationItem item, CancellationToken ct)
    {
        var ids = PublicStoryLabels.Parse(item.ProposedContentCategoryIds);
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            category => ids.Contains(category.Id), cancellationToken: ct);
        return content with { ProposedCategories = categories };
    }

    private async Task<PublicReviewPackageDto> BuildPackageAsync(
        ReviewContext context, ReviewContent content, CancellationToken cancellationToken)
    {
        var item = context.Item;
        var viewedScenes = PublicReviewState.GetViewed(item.ReviewProgress, PublicReviewRequirements.ScenePart);
        var heardAudio = PublicReviewState.GetViewed(item.ReviewProgress, PublicReviewRequirements.AudioPart);
        var flags = BuildFlags(context.Version, item);
        var blockers = EvaluateApproveBlockers(context, content);
        var history = await LoadReviewHistoryAsync(item, cancellationToken);

        var package = new PublicReviewPackageDto
        {
            ItemId = item.Id,
            StoryId = context.Story.Id,
            StoryVersionId = context.Version.Id,
            VersionNo = context.Version.VersionNo,
            IsResubmission = item.IsResubmission,
            ProposedLabels = content.ProposedCategories.OrderBy(x => x.Id).Select(category => new PublicReviewLabelDto
            {
                ContentCategoryId = category.Id, Code = category.Code,
                DisplayName = category.DisplayName, Source = TagSource.Creator.ToString()
            }).ToList(),
            Story = new PublicReviewStoryDto
            {
                Title = context.Story.Title,
                Description = context.Story.Description,
                Genre = context.Story.Genre,
                AgeBand = context.Story.AgeBand,
                ReadingLevel = context.Story.ReadingLevel,
                VocabularyLevel = context.Story.VocabularyLevel,
                Language = context.Story.Language,
                MoralLesson = context.Story.MoralLesson,
                CoverImageUrl = context.Story.CoverImageUrl,
                Labels = await LoadLabelsAsync(context.Story.Id, cancellationToken)
            },
            Version = new PublicReviewVersionDto
            {
                Title = context.Version.Title,
                OutlineOpening = context.Version.OutlineOpening,
                OutlineDevelopment = context.Version.OutlineDevelopment,
                OutlineEnding = context.Version.OutlineEnding,
                Content = context.Version.Content,
                Lesson = context.Version.Lesson
            },
            Scenes = content.Scenes.Select(scene => new PublicReviewSceneDto
            {
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                SceneText = scene.SceneText,
                VisualDescription = scene.VisualDescription,
                Viewed = viewedScenes.Contains(scene.Id),
                Illustrations = content.Beats
                    .Where(beat => beat.StorySceneId == scene.Id)
                    .OrderBy(beat => beat.BeatOrder)
                    .Select(beat =>
                    {
                        var asset = content.Assets.FirstOrDefault(x =>
                            x.IllustrationBeatId == beat.Id && x.Type == MediaType.Illustration
                            && x.Status == MediaStatus.Ready);
                        return new PublicReviewIllustrationDto
                        {
                            BeatOrder = beat.BeatOrder,
                            VisualFocus = beat.VisualFocus,
                            AssetId = asset?.Id,
                            Url = asset?.Url,
                            Source = asset?.Source.ToString() ?? "AiGenerated",
                            SourceDeclaration = UploadDeclaration(asset, "SourceDeclaration"),
                            RightsDeclaration = UploadDeclaration(asset, "RightsDeclaration")
                        };
                    }).ToList(),
                Segments = content.Segments
                    .Where(segment => segment.StorySceneId == scene.Id)
                    .OrderBy(segment => segment.SegmentOrder)
                    .Select(segment =>
                    {
                        var audio = FindAudio(content, segment.Id);
                        return new PublicReviewSegmentDto
                        {
                            SegmentId = segment.Id,
                            SegmentOrder = segment.SegmentOrder,
                            Text = segment.TextContent,
                            AudioAssetId = audio?.Id,
                            AudioUrl = audio?.Url,
                            AudioSource = audio?.Source.ToString() ?? "AiGenerated",
                            RequiresFullAudioReview = audio?.Source == MediaAssetSource.UserUpload,
                            SourceDeclaration = UploadDeclaration(audio, "SourceDeclaration"),
                            RightsDeclaration = UploadDeclaration(audio, "RightsDeclaration"),
                            HasPronunciationWarning = PublicReviewRequirements.HasPronunciationWarning(audio),
                            Heard = heardAudio.Contains(segment.Id)
                        };
                    }).ToList()
            }).ToList(),
            Vocabulary = content.Vocabulary.OrderBy(x => x.Id).Select(x => new PublicReviewVocabularyDto
            {
                Id = x.Id,
                Term = x.Term,
                Definition = x.Definition
                , SourceQuote = x.SourceQuote, Example = x.Example
            }).ToList(),
            Quizzes = content.Quizzes.OrderBy(x => x.Id).Select(x => new PublicReviewQuizDto
            {
                Id = x.Id,
                Type = x.Type.ToString(),
                Question = x.Question,
                CorrectAnswer = x.CorrectAnswer,
                SourceQuote = x.SourceQuote, Explanation = x.Explanation, Hint = x.Hint,
                LearningGoal = x.LearningGoal, OptionFeedbackJson = x.OptionFeedbackJson,
                Choices = x.Choices
            }).ToList(),
            ListeningInteractions = content.Listening.Select(interaction => new PublicReviewListeningDto
            {
                Id = interaction.Id,
                Type = interaction.Type.ToString(),
                Kind = interaction.Kind?.ToString(),
                StorySceneId = interaction.StorySceneId,
                AnchorOffset = interaction.AnchorOffset,
                Prompt = interaction.Prompt,
                SourceQuote = interaction.SourceQuote,
                CharacterName = interaction.CharacterName,
                HiddenWord = interaction.HiddenWord,
                Hint = interaction.Hint,
                RevealOffset = interaction.RevealOffset,
                Explanation = interaction.Explanation,
                MatchFeedback = interaction.MatchFeedback,
                NoMatchFeedback = interaction.NoMatchFeedback,
                WaitFeedback = interaction.WaitFeedback,
                Options = content.ListeningOptions
                    .Where(option => option.ListeningInteractionId == interaction.Id)
                    .OrderBy(option => option.SortOrder)
                    .Select(option => new PublicReviewListeningOptionDto
                    {
                        Id = option.Id,
                        SortOrder = option.SortOrder,
                        Text = option.Text,
                        Feedback = option.Feedback,
                        IsMatch = option.IsMatch,
                        CorrectOrder = option.CorrectOrder,
                        SourceQuote = option.SourceQuote,
                        ImageDescription = option.ImageDescription,
                        ImageUrl = content.Assets.FirstOrDefault(asset =>
                            asset.ListeningInteractionOptionId == option.Id && asset.Type == MediaType.ListeningImage
                            && asset.Status == MediaStatus.Ready)?.Url
                    }).ToList(),
                // Audio của cả tương tác trước, rồi audio từng thẻ theo thứ tự thẻ; bỏ audio của thẻ đã xoá.
                Audio = content.Assets
                    .Where(asset => asset.ListeningInteractionId == interaction.Id && asset.Type == MediaType.ListeningAudio
                                    && asset.Status == MediaStatus.Ready && !string.IsNullOrWhiteSpace(asset.Url))
                    .Select(asset => (Asset: asset, Option: content.ListeningOptions.FirstOrDefault(option =>
                        option.ListeningInteractionId == interaction.Id && option.Id == asset.ListeningInteractionOptionId)))
                    .Where(x => x.Asset.ListeningInteractionOptionId is null || x.Option is not null)
                    .OrderBy(x => x.Option?.SortOrder ?? 0)
                    .ThenBy(x => x.Asset.AudioPartKey, StringComparer.Ordinal)
                    .Select(x => new PublicReviewListeningAudioDto
                    {
                        OptionId = x.Asset.ListeningInteractionOptionId, PartKey = x.Asset.AudioPartKey,
                        AssetId = x.Asset.Id, Url = x.Asset.Url
                    })
                    .ToList()
            }).ToList(),
            Flags = flags,
            AllFlagsResolved = flags.All(flag => flag.Resolved),
            Progress = BuildProgress(item, content),
            Checklist = BuildChecklist(item),
            ApproveBlockers = blockers,
            CanApprove = blockers.Count == 0,
            PreviousModeratorName = history.FirstOrDefault()?.DecidedByName,
            ReviewHistory = history
        };
        await SignMediaUrlsAsync(package, cancellationToken);
        return package;
    }

    /// <summary>
    /// Tranh và giọng đọc lưu trong kho riêng tư nên cơ sở dữ liệu chỉ giữ đường dẫn nội bộ. Moderator cần đường dẫn có chữ ký
    /// (giống phụ huynh xem gói media), nếu không trình duyệt không mở được và tranh/âm thanh luôn báo lỗi.
    /// </summary>
    private async Task SignMediaUrlsAsync(PublicReviewPackageDto package, CancellationToken cancellationToken)
    {
        if (_mediaStorage is null) return;
        // Mỗi lần ký là một lệnh gọi mạng tới kho lưu trữ; gói có hàng chục tranh/âm thanh nên phải ký song song,
        // nếu ký lần lượt thì mở gói mất hàng chục giây.
        var tasks = new List<Task>();
        void Sign(string? path, Action<string?> assign) =>
            tasks.Add(SignAndAssignAsync(path, assign, cancellationToken));
        foreach (var scene in package.Scenes)
        {
            foreach (var illustration in scene.Illustrations)
                Sign(illustration.Url, signed => illustration.Url = signed);
            foreach (var segment in scene.Segments)
                Sign(segment.AudioUrl, signed => segment.AudioUrl = signed);
        }
        foreach (var interaction in package.ListeningInteractions)
        {
            foreach (var option in interaction.Options)
                Sign(option.ImageUrl, signed => option.ImageUrl = signed);
            foreach (var audio in interaction.Audio)
                Sign(audio.Url, signed => audio.Url = signed);
        }
        await Task.WhenAll(tasks);
    }

    private async Task SignAndAssignAsync(string? path, Action<string?> assign, CancellationToken cancellationToken) =>
        assign(await SignMediaUrlAsync(path, cancellationToken));

    private async Task<string?> SignMediaUrlAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        // Gói cũ có thể lưu sẵn địa chỉ https ngoài kho: giữ nguyên. Địa chỉ tuyệt đối khác thì không đưa ra ngoài.
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return uri.Scheme == Uri.UriSchemeHttps ? path : null;
        return await _mediaStorage!.GetSignedUrlAsync(path, ReviewMediaUrlLifetime, cancellationToken);
    }

    private static MediaAsset? FindAudio(ReviewContent content, int segmentId) =>
        content.Assets.FirstOrDefault(x =>
            x.StorySegmentId == segmentId && x.Type == MediaType.TtsAudio
            && x.Status == MediaStatus.Ready && !string.IsNullOrWhiteSpace(x.Url));

    private static string? UploadDeclaration(MediaAsset? asset, string key)
    {
        if (asset?.Source != MediaAssetSource.UserUpload || string.IsNullOrWhiteSpace(asset.ValidationResultJson)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(asset.ValidationResultJson).TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null; }
        catch (JsonException) { return null; }
    }

    private static PublicReviewProgressDto BuildProgress(ModerationItem item, ReviewContent content)
    {
        var viewedScenes = PublicReviewState.GetViewed(item.ReviewProgress, PublicReviewRequirements.ScenePart);
        var heardAudio = PublicReviewState.GetViewed(item.ReviewProgress, PublicReviewRequirements.AudioPart);

        var missingScenes = content.Scenes.Where(scene => !viewedScenes.Contains(scene.Id))
            .Select(scene => scene.Id).ToList();
        var audioSegments = content.Segments
            .Select(segment => (Segment: segment, Audio: FindAudio(content, segment.Id)))
            .Where(pair => pair.Audio != null).ToList();
        var heardCount = audioSegments.Count(pair => heardAudio.Contains(pair.Segment.Id));
        var missingWarnings = audioSegments
            .Where(pair => (pair.Audio!.Source == MediaAssetSource.UserUpload || PublicReviewRequirements.HasPronunciationWarning(pair.Audio))
                           && !heardAudio.Contains(pair.Segment.Id))
            .Select(pair => pair.Segment.Id).ToList();
        var needsOne = audioSegments.Count > 0 && heardCount == 0;

        return new PublicReviewProgressDto
        {
            TotalScenes = content.Scenes.Count,
            MissingSceneIds = missingScenes,
            AudioHeardCount = heardCount,
            MissingWarningSegmentIds = missingWarnings,
            NeedsAtLeastOneAudio = needsOne,
            RequiredViewsComplete = missingScenes.Count == 0 && missingWarnings.Count == 0 && !needsOne
        };
    }

    private static List<PublicReviewFlagDto> BuildFlags(StoryVersion version, ModerationItem item)
    {
        var resolved = PublicReviewState.GetResolvedFlags(item.Flags);
        return PublicReviewState.ParseVersionFlags(version.Moderation.GrayZoneFlags).Select(flag =>
        {
            var handled = resolved.FirstOrDefault(x => x.FlagId == flag.Id);
            return new PublicReviewFlagDto
            {
                FlagId = flag.Id,
                StartOffset = flag.StartOffset,
                EndOffset = flag.EndOffset,
                CriterionCode = flag.Category,
                EvidenceQuote = flag.EvidenceQuote,
                ContextNote = flag.ContextNote,
                Kind = flag.Kind,
                Name = flag.Name,
                Reason = flag.Reason,
                Resolved = handled != null,
                ResolvedReason = handled?.Reason
            };
        }).ToList();
    }

    private static PublicReviewChecklistDto BuildChecklist(ModerationItem item)
    {
        var saved = PublicReviewState.GetChecklist(item.Checklist);
        bool? Get(string key) => saved.TryGetValue(key, out var value) ? value : null;

        return new PublicReviewChecklistDto
        {
            Safety = Get(PublicReviewRequirements.Safety),
            AgeFit = Get(PublicReviewRequirements.AgeFit),
            Education = Get(PublicReviewRequirements.Education),
            Language = Get(PublicReviewRequirements.Language),
            Copyright = Get(PublicReviewRequirements.Copyright),
            Image = Get(PublicReviewRequirements.Image),
            Audio = Get(PublicReviewRequirements.Audio),
            Interaction = Get(PublicReviewRequirements.Interaction)
        };
    }

    private async Task<List<PublicReviewLabelDto>> LoadLabelsAsync(
        int storyId, CancellationToken cancellationToken)
    {
        var links = await _unitOfWork.Repository<StoryCategory>().FindAsync(
            x => x.StoryId == storyId, cancellationToken: cancellationToken);
        var categoryIds = links.Select(x => x.ContentCategoryId).ToList();
        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            x => categoryIds.Contains(x.Id), cancellationToken: cancellationToken);

        return links.Join(categories, link => link.ContentCategoryId, category => category.Id,
                (link, category) => new PublicReviewLabelDto
                {
                    ContentCategoryId = category.Id,
                    Code = category.Code,
                    DisplayName = category.DisplayName,
                    Source = link.Source.ToString()
                })
            .OrderBy(x => x.ContentCategoryId).ToList();
    }
}
