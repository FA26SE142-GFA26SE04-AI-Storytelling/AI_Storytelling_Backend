using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.AIStoryInput.Interfaces;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes.Services;

public class ParentStoryWishService : IParentStoryWishService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _guard;
    private readonly IChildRoadmapService _roadmap;
    private readonly IAIStoryInputService _aiInput;
    private readonly IStoryWishSuggestionService _suggestions;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly TimeProvider _clock;

    public ParentStoryWishService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard guard,
        IChildRoadmapService roadmap,
        IAIStoryInputService aiInput,
        IStoryWishSuggestionService suggestions,
        IAuditLogWriter auditLogWriter,
        TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _roadmap = roadmap;
        _aiInput = aiInput;
        _suggestions = suggestions;
        _auditLogWriter = auditLogWriter;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ParentWishDto>> ListAsync(
        int parentUserId, int childProfileId, CancellationToken ct = default)
    {
        await _guard.EnsurePermissionAsync(childProfileId, parentUserId, Permission.ApproveStory, ct);
        var wishes = (await _unitOfWork.Repository<StoryWish>().FindAsync(
                wish => wish.ChildProfileId == childProfileId
                        && !wish.IsDeleted
                        && wish.Status != StoryWishStatus.Draft,
                cancellationToken: ct))
            .OrderBy(wish => IsWaiting(wish) ? 0 : 1)
            .ThenByDescending(wish => wish.SubmittedAt ?? wish.CreatedAt)
            .ThenByDescending(wish => wish.Id)
            .Take(StoryWishDecisionPolicy.ListLimit)
            .ToList();
        return await ToDtosAsync(wishes, ct);
    }

    public async Task<ParentWishDetailDto> GetAsync(int parentUserId, int wishId, CancellationToken ct = default)
    {
        var wish = await RequireAsync(parentUserId, wishId, ct);
        return new ParentWishDetailDto
        {
            Wish = (await ToDtosAsync(new[] { wish }, ct)).Single(),
            Suggestions = await _suggestions.ForParentAsync(parentUserId, wishId, ct)
        };
    }

    public async Task<ParentWishGenerationFormDto> GetGenerationFormAsync(
        int parentUserId, int wishId, CancellationToken ct = default)
    {
        var wish = await RequireAsync(parentUserId, wishId, ct);
        EnsurePending(wish);

        var context = await _aiInput.GetContextAsync(parentUserId, wish.ChildProfileId, ct);
        var cards = await CardsByIdAsync(new[] { wish }, ct);
        string Label(int? id) => id is int value && cards.TryGetValue(value, out var card) ? card.Label : string.Empty;

        var character = Label(wish.CharacterCardId);
        var place = Label(wish.PlaceCardId);
        var challenge = wish.ChallengeCardId.HasValue ? Label(wish.ChallengeCardId) : null;
        var lesson = wish.LessonLeftToParent ? string.Empty : Label(wish.LessonCardId);

        return new ParentWishGenerationFormDto
        {
            WishId = wish.Id,
            LessonLeftToParent = wish.LessonLeftToParent,
            Request = new SubmitAIStoryInputRequestDto
            {
                Topic = StoryWishDecisionPolicy.Topic(character, place, challenge),
                CharacterMode = "specified",
                Characters = new[] { character },
                SettingMode = "specified",
                Setting = place,
                Lesson = lesson,
                VocabularyLevel = context.DefaultVocabularyLevel,
                Language = context.DefaultLanguage,
                TargetLength = context.RecommendedTargetLength > 0
                    ? Math.Min(context.RecommendedTargetLength, context.MaximumLength) : context.MaximumLength,
                OutputMode = "learning",
                ChildProfileId = wish.ChildProfileId,
                IdempotencyKey = StoryWishDecisionPolicy.GenerationKey(wish.Id)
            }
        };
    }

    public async Task<ParentWishCreateStoryResultDto> CreateStoryAsync(
        int parentUserId, int wishId, SubmitAIStoryInputRequestDto request, CancellationToken ct = default)
    {
        var wish = await RequireAsync(parentUserId, wishId, ct);
        if (wish.Status != StoryWishStatus.Pending &&
            !((wish.Status is StoryWishStatus.Accepted or StoryWishStatus.Fulfilled) &&
                wish.ResolvedByUserId == parentUserId && wish.GenerationRequestId.HasValue && wish.GeneratedStoryId.HasValue))
            throw new ConflictException(StoryWishDecisionPolicy.AlreadyHandledMessage);
        if (request.ChildProfileId != wish.ChildProfileId)
        {
            throw new BadRequestException(StoryWishDecisionPolicy.ChildMismatchMessage);
        }

        if (request.ExistingStoryId.HasValue || request.CreatorAudience is not null)
            throw new BadRequestException("WISH_REQUIRES_NEW_PARENT_STORY");
        // The input service owns atomic wish/request creation and the server-chosen idempotency key.
        var progress = await _aiInput.SubmitAsync(parentUserId, new SubmitAIStoryInputRequestDto
        {
            ChildProfileId = wish.ChildProfileId, SourceWishId = wish.Id,
            IdempotencyKey = StoryWishDecisionPolicy.GenerationKey(wish.Id),
            Topic = request.Topic, Genre = request.Genre, CharacterMode = request.CharacterMode, Characters = request.Characters,
            SettingMode = request.SettingMode, Setting = request.Setting, Lesson = request.Lesson,
            VocabularyLevel = request.VocabularyLevel, Language = request.Language, TargetLength = request.TargetLength,
            OutputMode = request.OutputMode
        }, ct);
        wish = await RequireAsync(parentUserId, wishId, ct);
        if (wish.GeneratedStoryId != progress.StoryId || wish.GenerationRequestId != progress.RequestId)
            throw new ConflictException("WISH_GENERATION_LINK_CONFLICT");

        return new ParentWishCreateStoryResultDto
        {
            Wish = (await ToDtosAsync(new[] { wish }, ct)).Single(),
            Generation = progress
        };
    }

    public async Task<AIStoryInputProgressDto> GetGenerationProgressAsync(int parentUserId, int wishId, CancellationToken ct = default)
    {
        var wish = await RequireAsync(parentUserId, wishId, ct);
        if (wish.GeneratedStoryId is not int storyId || wish.GenerationRequestId is not int requestId)
            throw new ConflictException("WISH_GENERATION_NOT_STARTED");
        return await _aiInput.GetProgressAsync(parentUserId, storyId, requestId, ct);
    }

    public async Task<ParentWishDto> UseExistingAsync(
        int parentUserId, int wishId, UseExistingStoryRequestDto request, CancellationToken ct = default)
    {
        var wish = await RequireAsync(parentUserId, wishId, ct);
        if (wish.Kind != StoryWishKind.Wish)
        {
            throw new BadRequestException(StoryWishDecisionPolicy.WishOnlyMessage);
        }

        EnsurePending(wish);
        var rank = request.SuggestionRank;
        var suggestion = await _unitOfWork.Repository<StoryWishSuggestion>().FirstOrDefaultAsync(
            row => row.StoryWishId == wishId && row.Rank == rank && !row.IsDeleted, cancellationToken: ct)
            ?? throw new BadRequestException(StoryWishDecisionPolicy.OnlySuggestedMessage);
        if (suggestion.CommunitySubmissionId.HasValue)
        {
            throw new BadRequestException(StoryWishDecisionPolicy.CommunityNotSupportedMessage);
        }

        // Lộ trình tự kiểm ApproveStory, kiểm truyện hợp với bé, chèn "Đọc tiếp theo" (WF-L4A UC-04).
        await _roadmap.AddForWishAsync(parentUserId, wish.ChildProfileId, suggestion.StoryId, wish.Id, ct);
        wish = await RequireAsync(parentUserId, wishId, ct);
        return (await ToDtosAsync(new[] { wish }, ct)).Single();
    }

    public async Task<ParentWishDto> DeclineAsync(
        int parentUserId, int wishId, DeclineWishRequestDto request, CancellationToken ct = default)
    {
        var wish = await RequireAsync(parentUserId, wishId, ct);
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(-wish.ChildProfileId, ct);
            wish = await RequireAsync(parentUserId, wishId, ct);
            if (!IsWaiting(wish))
            {
                throw new ConflictException(StoryWishDecisionPolicy.AlreadyHandledMessage);
            }

            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            if (reason is { Length: > StoryWishDecisionPolicy.MaxDeclineReasonLength })
            {
                throw new BadRequestException(StoryWishDecisionPolicy.ReasonTooLongMessage);
            }

            var wasAccepted = wish.Status == StoryWishStatus.Accepted;
            var before = new { status = wish.Status.ToString() };
            var now = Now();
            wish.Status = StoryWishStatus.Declined;
            wish.DeclineReason = reason;
            wish.ResolutionNote = wasAccepted ? StoryWishDecisionPolicy.NoteParentCancelled : null;
            wish.ResolvedByUserId = parentUserId;
            wish.ResolvedAt = now;
            wish.UpdatedAt = now;
            _unitOfWork.Repository<StoryWish>().Update(wish);
            await _unitOfWork.SaveChangesAsync(ct);
            // Chỉ ghi "có lý do hay không": nội dung lý do có thể chứa thông tin riêng về bé.
            await _auditLogWriter.LogAsync(parentUserId, "StoryWishDeclined", nameof(StoryWish), wish.Id, before,
                new { status = wish.Status.ToString(), note = wish.ResolutionNote, hasReason = reason != null }, ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return (await ToDtosAsync(new[] { wish }, ct)).Single();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private static bool IsWaiting(StoryWish wish) =>
        wish.Status is StoryWishStatus.Pending or StoryWishStatus.Accepted;

    /// <summary>Điều ước đã gửi (không phải bản nháp) và người gọi có ApproveStory trên đúng bé.</summary>
    private async Task<StoryWish> RequireAsync(int parentUserId, int wishId, CancellationToken ct)
    {
        var wish = await _unitOfWork.Repository<StoryWish>().FirstOrDefaultAsync(
            x => x.Id == wishId && !x.IsDeleted, cancellationToken: ct);
        if (wish == null || wish.IsDeleted || wish.Status == StoryWishStatus.Draft)
        {
            throw new NotFoundException("Điều ước", wishId);
        }

        await _guard.EnsurePermissionAsync(wish.ChildProfileId, parentUserId, Permission.ApproveStory, ct);
        return wish;
    }

    private static void EnsurePending(StoryWish wish)
    {
        if (wish.Status != StoryWishStatus.Pending)
        {
            throw new ConflictException(StoryWishDecisionPolicy.AlreadyHandledMessage);
        }
    }

    private async Task<Dictionary<int, WishCard>> CardsByIdAsync(IEnumerable<StoryWish> wishes, CancellationToken ct)
    {
        var ids = wishes
            .SelectMany(wish => new[] { wish.CharacterCardId, wish.PlaceCardId, wish.ChallengeCardId, wish.LessonCardId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, WishCard>();
        }

        return (await _unitOfWork.Repository<WishCard>().FindAsync(
            card => ids.Contains(card.Id), cancellationToken: ct)).ToDictionary(card => card.Id);
    }

    private async Task<List<ParentWishDto>> ToDtosAsync(IReadOnlyCollection<StoryWish> wishes, CancellationToken ct)
    {
        var cards = await CardsByIdAsync(wishes, ct);
        return wishes.Select(wish => new ParentWishDto
        {
            WishId = wish.Id,
            ChildProfileId = wish.ChildProfileId,
            Kind = wish.Kind.ToString(),
            Status = wish.Status.ToString(),
            Cards = new[] { wish.CharacterCardId, wish.PlaceCardId, wish.ChallengeCardId, wish.LessonCardId }
                .Where(id => id.HasValue && cards.ContainsKey(id.Value))
                .Select(id => cards[id!.Value])
                .Select(card => new ChildWishCardDto
                {
                    Id = card.Id, Group = card.Group.ToString(), Label = card.Label,
                    ImageUrl = card.ImageUrl, AudioUrl = card.AudioUrl
                }).ToList(),
            LessonLeftToParent = wish.LessonLeftToParent,
            SubmittedAt = wish.SubmittedAt,
            ExpiresAt = wish.Status == StoryWishStatus.Pending && wish.PendingSince.HasValue
                ? wish.PendingSince.Value + StoryWishDecisionPolicy.PendingLifetime
                : null,
            DeclineReason = wish.DeclineReason,
            ResolutionNote = wish.ResolutionNote,
            GeneratedStoryId = wish.GeneratedStoryId,
            GenerationRequestId = wish.GenerationRequestId,
            FulfilledStoryId = wish.FulfilledStoryId,
            FulfilledWithExistingStory = wish.FulfilledWithExistingStory,
            ResolvedAt = wish.ResolvedAt
        }).ToList();
    }
}
