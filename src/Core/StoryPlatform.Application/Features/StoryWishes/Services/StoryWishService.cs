using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryWishes.Services;

public class StoryWishService : IStoryWishService
{
    private static readonly WishCardGroup[] CardOrder =
        { WishCardGroup.Character, WishCardGroup.Place, WishCardGroup.Challenge, WishCardGroup.Lesson };

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildWishCardService _cards;
    private readonly IStoryWishNotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly IStoryWishSuggestionService _suggestions;

    public StoryWishService(
        IUnitOfWork unitOfWork, IChildWishCardService cards, IStoryWishNotifier notifier, TimeProvider clock, IStoryWishSuggestionService suggestions)
    {
        _unitOfWork = unitOfWork;
        _cards = cards;
        _notifier = notifier;
        _clock = clock;
        _suggestions = suggestions;
    }

    public async Task<WishDraftDto> StartDraftAsync(int childProfileId, CancellationToken ct = default)
    {
        await EnsureCanWishAsync(childProfileId, ct);
        var now = Now();
        await DeleteExpiredDraftsAsync(childProfileId, now, ct);
        await EnsureWithinLimitsAsync(childProfileId, now, ct);

        var draft = await FindDraftAsync(childProfileId, ct);
        var resumed = draft != null;
        if (draft == null)
        {
            draft = await _unitOfWork.Repository<StoryWish>().AddAsync(new StoryWish
            {
                ChildProfileId = childProfileId,
                Kind = StoryWishKind.Wish,
                Status = StoryWishStatus.Draft,
                CreatedAt = now
            }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return await ToDraftDtoAsync(draft, resumed, ct);
    }

    public async Task<WishDraftDto> SelectCardAsync(
        int childProfileId, SelectWishCardRequestDto request, CancellationToken ct = default)
    {
        await EnsureCanWishAsync(childProfileId, ct);
        var now = Now();
        await DeleteExpiredDraftsAsync(childProfileId, now, ct);
        var draft = await FindDraftAsync(childProfileId, ct)
                    ?? throw new NotFoundException(StoryWishPolicy.DraftMissingMessage);

        if (!WishCardRules.TryParseGroup(request.Group, out var group) || !WishCardRules.IsWishGroup(group))
        {
            throw new BadRequestException(ChildWishCardPolicy.UnknownGroupMessage);
        }

        if (request.LeaveToParent)
        {
            if (group != WishCardGroup.Lesson || request.CardId.HasValue)
            {
                throw new BadRequestException(StoryWishPolicy.PickCardMessage);
            }

            draft.LessonCardId = null;
            draft.LessonLeftToParent = true;
        }
        else if (request.CardId is int cardId)
        {
            if (!await IsVisibleAsync(childProfileId, group, cardId, ct))
            {
                throw new BadRequestException(StoryWishPolicy.PickCardMessage);
            }

            SetCard(draft, group, cardId);
        }
        else
        {
            SetCard(draft, group, null);
        }

        draft.UpdatedAt = now;
        _unitOfWork.Repository<StoryWish>().Update(draft);
        await _unitOfWork.SaveChangesAsync(ct);
        return await ToDraftDtoAsync(draft, resumed: true, ct);
    }

    public async Task<ChildWishDto> SubmitAsync(int childProfileId, CancellationToken ct = default)
    {
        await EnsureCanWishAsync(childProfileId, ct);
        var now = Now();
        await DeleteExpiredDraftsAsync(childProfileId, now, ct);
        var draft = await FindDraftAsync(childProfileId, ct)
                    ?? throw new NotFoundException(StoryWishPolicy.DraftMissingMessage);
        if (!draft.CharacterCardId.HasValue || !draft.PlaceCardId.HasValue)
        {
            throw new BadRequestException(StoryWishPolicy.NeedRequiredCardsMessage);
        }

        var stale = new List<WishCardGroup>();
        foreach (var (group, cardId) in Chosen(draft).ToList())
        {
            if (!await IsVisibleAsync(childProfileId, group, cardId, ct)) stale.Add(group);
        }
        if (stale.Count > 0)
        {
            foreach (var group in stale) SetCard(draft, group, null);
            draft.UpdatedAt = now;
            _unitOfWork.Repository<StoryWish>().Update(draft);
            await _unitOfWork.SaveChangesAsync(ct);
            throw new BadRequestException(StoryWishPolicy.CardUnavailableMessage);
        }

        await EnsureWithinLimitsAsync(childProfileId, now, ct);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(-childProfileId, ct);
            now = Now();
            await EnsureWithinLimitsAsync(childProfileId, now, ct);
            draft = await FindDraftAsync(childProfileId, ct)
                    ?? throw new NotFoundException(StoryWishPolicy.DraftMissingMessage);
            if (!draft.CharacterCardId.HasValue || !draft.PlaceCardId.HasValue)
            {
                throw new BadRequestException(StoryWishPolicy.NeedRequiredCardsMessage);
            }

            await EnsureChosenCardsRemainVisibleAsync(childProfileId, draft, ct);
            draft.Status = StoryWishStatus.Pending;
            draft.SubmittedAt = now;
            draft.PendingSince = now;
            draft.UpdatedAt = now;
            _unitOfWork.Repository<StoryWish>().Update(draft);
            await _suggestions.ComputeAsync(draft, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            await _notifier.NotifySubmittedAsync(draft, ct);
            await _unitOfWork.CommitTransactionAsync(ct);
            return await ToChildDtoAsync(draft, ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChildWishDto>> ListMineAsync(
        int childProfileId, CancellationToken ct = default)
    {
        await EnsureChildActiveAsync(childProfileId, ct);
        var wishes = (await _unitOfWork.Repository<StoryWish>().FindAsync(
                wish => wish.ChildProfileId == childProfileId
                        && wish.Status != StoryWishStatus.Draft
                        && wish.Status != StoryWishStatus.Cancelled
                        && !wish.IsDeleted,
                cancellationToken: ct))
            .OrderByDescending(wish => wish.SubmittedAt ?? wish.CreatedAt)
            .ThenByDescending(wish => wish.Id)
            .Take(StoryWishPolicy.ListLimit)
            .ToList();
        var result = new List<ChildWishDto>();
        foreach (var wish in wishes) result.Add(await ToChildDtoAsync(wish, ct));
        return result;
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private async Task EnsureChildActiveAsync(int childProfileId, CancellationToken ct)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, ct);
        if (child == null || child.IsDeleted || child.Status != ChildProfileStatus.Active)
        {
            throw new ForbiddenException(ChildWishCardPolicy.ProfileUnavailableMessage);
        }
    }

    private async Task EnsureCanWishAsync(int childProfileId, CancellationToken ct)
    {
        await EnsureChildActiveAsync(childProfileId, ct);
        var policy = await _unitOfWork.Repository<SafetyPolicy>().FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && !existing.IsDeleted, cancellationToken: ct);
        if (policy is { StoryWishesEnabled: false })
        {
            throw new ForbiddenException(ChildWishCardPolicy.WishesOffMessage);
        }
    }

    private async Task EnsureWithinLimitsAsync(int childProfileId, DateTime now, CancellationToken ct)
    {
        var wishes = _unitOfWork.Repository<StoryWish>();
        var waiting = await wishes.CountAsync(
            wish => wish.ChildProfileId == childProfileId
                    && !wish.IsDeleted
                    && (wish.Status == StoryWishStatus.Pending || wish.Status == StoryWishStatus.Accepted), ct);
        if (waiting >= StoryWishPolicy.MaxWaiting)
        {
            throw new ConflictException(StoryWishPolicy.TooManyWaitingMessage);
        }

        var since = StoryWishPolicy.StartOfLocalDayUtc(now);
        var today = await wishes.CountAsync(
            wish => wish.ChildProfileId == childProfileId
                    && !wish.IsDeleted
                    && wish.SubmittedAt != null
                    && wish.SubmittedAt >= since, ct);
        if (today >= StoryWishPolicy.MaxPerDay)
        {
            throw new ConflictException(StoryWishPolicy.DailyLimitMessage);
        }
    }

    private async Task DeleteExpiredDraftsAsync(int childProfileId, DateTime now, CancellationToken ct)
    {
        var cutoff = now - StoryWishPolicy.DraftLifetime;
        var repository = _unitOfWork.Repository<StoryWish>();
        var expired = await repository.FindAsync(
            wish => wish.ChildProfileId == childProfileId
                    && wish.Status == StoryWishStatus.Draft
                    && wish.CreatedAt <= cutoff,
            cancellationToken: ct);
        foreach (var draft in expired.ToList()) repository.Delete(draft);
        if (expired.Count > 0) await _unitOfWork.SaveChangesAsync(ct);
    }

    private Task<StoryWish?> FindDraftAsync(int childProfileId, CancellationToken ct) =>
        _unitOfWork.Repository<StoryWish>().FirstOrDefaultAsync(
            wish => wish.ChildProfileId == childProfileId
                    && wish.Status == StoryWishStatus.Draft
                    && !wish.IsDeleted,
            cancellationToken: ct);

    private async Task<bool> IsVisibleAsync(int childProfileId, WishCardGroup group, int cardId, CancellationToken ct) =>
        (await _cards.ListAsync(childProfileId, group.ToString(), ct)).Any(card => card.Id == cardId);

    private async Task EnsureChosenCardsRemainVisibleAsync(int childProfileId, StoryWish wish, CancellationToken ct)
    {
        foreach (var (group, cardId) in Chosen(wish))
        {
            if (!await IsVisibleAsync(childProfileId, group, cardId, ct))
            {
                throw new BadRequestException(StoryWishPolicy.CardUnavailableMessage);
            }
        }
    }

    private static void SetCard(StoryWish wish, WishCardGroup group, int? cardId)
    {
        switch (group)
        {
            case WishCardGroup.Character: wish.CharacterCardId = cardId; break;
            case WishCardGroup.Place: wish.PlaceCardId = cardId; break;
            case WishCardGroup.Challenge: wish.ChallengeCardId = cardId; break;
            case WishCardGroup.Lesson:
                wish.LessonCardId = cardId;
                wish.LessonLeftToParent = false;
                break;
        }
    }

    private static IEnumerable<(WishCardGroup Group, int CardId)> Chosen(StoryWish wish)
    {
        if (wish.CharacterCardId is int character) yield return (WishCardGroup.Character, character);
        if (wish.PlaceCardId is int place) yield return (WishCardGroup.Place, place);
        if (wish.ChallengeCardId is int challenge) yield return (WishCardGroup.Challenge, challenge);
        if (wish.LessonCardId is int lesson) yield return (WishCardGroup.Lesson, lesson);
    }

    private async Task<List<ChildWishCardDto>> CardsOfAsync(StoryWish wish, CancellationToken ct)
    {
        var chosen = Chosen(wish).ToList();
        if (chosen.Count == 0) return new List<ChildWishCardDto>();
        var ids = chosen.Select(item => item.CardId).ToList();
        var cards = (await _unitOfWork.Repository<WishCard>().FindAsync(
            card => ids.Contains(card.Id), cancellationToken: ct)).ToDictionary(card => card.Id);
        return chosen.OrderBy(item => Array.IndexOf(CardOrder, item.Group))
            .Where(item => cards.ContainsKey(item.CardId))
            .Select(item => cards[item.CardId])
            .Select(card => new ChildWishCardDto
            {
                Id = card.Id,
                Group = card.Group.ToString(),
                Label = card.Label,
                ImageUrl = card.ImageUrl,
                AudioUrl = card.AudioUrl
            }).ToList();
    }

    private async Task<WishDraftDto> ToDraftDtoAsync(StoryWish draft, bool resumed, CancellationToken ct) => new()
    {
        WishId = draft.Id,
        Resumed = resumed,
        ExpiresAt = draft.CreatedAt + StoryWishPolicy.DraftLifetime,
        Cards = await CardsOfAsync(draft, ct),
        LessonLeftToParent = draft.LessonLeftToParent,
        CanSubmit = draft.CharacterCardId.HasValue && draft.PlaceCardId.HasValue
    };

    private async Task<ChildWishDto> ToChildDtoAsync(StoryWish wish, CancellationToken ct) => new()
    {
        WishId = wish.Id,
        Kind = wish.Kind.ToString(),
        Status = wish.Status.ToString(),
        Message = StoryWishPolicy.StatusMessage(wish),
        Cards = await CardsOfAsync(wish, ct),
        LessonLeftToParent = wish.LessonLeftToParent,
        FulfilledStoryId = wish.Status == StoryWishStatus.Fulfilled ? wish.FulfilledStoryId : null,
        SubmittedAt = wish.SubmittedAt,
        Suggestions = await _suggestions.ForChildAsync(wish, ct)
    };
}
