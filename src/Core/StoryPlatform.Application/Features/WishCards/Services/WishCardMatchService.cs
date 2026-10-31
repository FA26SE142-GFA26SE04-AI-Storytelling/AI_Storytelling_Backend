using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.WishCards.Services;

public class WishCardMatchService : IWishCardMatchService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;

    public WishCardMatchService(IUnitOfWork unitOfWork, IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<IReadOnlyList<WishCardMatchDto>> ListAsync(int storyId, CancellationToken ct = default)
    {
        await RequireStoryAsync(storyId, ct);
        var matches = await _unitOfWork.Repository<StoryWishCardMatch>().FindAsync(
            match => match.StoryId == storyId && !match.IsDeleted, cancellationToken: ct);
        var cardIds = matches.Select(match => match.WishCardId).ToList();
        var cards = (await _unitOfWork.Repository<WishCard>().FindAsync(
            card => cardIds.Contains(card.Id), cancellationToken: ct)).ToDictionary(card => card.Id);
        return matches
            .Where(match => cards.ContainsKey(match.WishCardId))
            .OrderBy(match => cards[match.WishCardId].Group)
            .ThenBy(match => cards[match.WishCardId].SortOrder)
            .ThenBy(match => match.WishCardId)
            .Select(match => ToDto(match, cards[match.WishCardId]))
            .ToList();
    }

    public Task<WishCardMatchDto> AddAsync(int actorUserId, int storyId, int wishCardId, CancellationToken ct = default) =>
        WithStoryLockAsync(storyId, () => AddLockedAsync(actorUserId, storyId, wishCardId, ct), ct);

    private async Task<WishCardMatchDto> AddLockedAsync(int actorUserId, int storyId, int wishCardId, CancellationToken ct)
    {
        await RequireStoryAsync(storyId, ct);
        var card = await _unitOfWork.Repository<WishCard>().GetByIdAsync(wishCardId, ct);
        if (card == null || card.IsDeleted)
        {
            throw new NotFoundException("Thẻ ước", wishCardId);
        }

        if (!WishCardRules.IsWishGroup(card.Group))
        {
            throw new BadRequestException("Chỉ gắn được thẻ nhân vật, nơi chốn, thử thách hoặc bài học.");
        }

        var repository = _unitOfWork.Repository<StoryWishCardMatch>();
        var existing = await repository.FirstOrDefaultAsync(
            match => match.StoryId == storyId && match.WishCardId == wishCardId && !match.IsDeleted,
            cancellationToken: ct);
        if (existing != null)
        {
            return ToDto(existing, card);
        }

        var created = await repository.AddAsync(new StoryWishCardMatch
        {
            StoryId = storyId,
            WishCardId = wishCardId,
            Source = WishCardMatchSource.Moderator,
            CreatedByUserId = actorUserId
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditLogWriter.LogAsync(actorUserId, "WishCardMatchAdded", nameof(StoryWishCardMatch), created.Id,
            null, new { storyId, wishCardId, source = created.Source.ToString() }, ct);
        await _auditLogWriter.LogAsync(actorUserId, WishCardMappingAudit.Action, WishCardMappingAudit.EntityType, storyId,
            null, new { operation = "Add", wishCardIds = new[] { wishCardId } }, ct);
        return ToDto(created, card);
    }

    public Task RemoveAsync(int actorUserId, int storyId, int wishCardId, CancellationToken ct = default) =>
        WithStoryLockAsync(storyId, async () => { await RemoveLockedAsync(actorUserId, storyId, wishCardId, ct); return true; }, ct);

    private async Task RemoveLockedAsync(int actorUserId, int storyId, int wishCardId, CancellationToken ct)
    {
        var repository = _unitOfWork.Repository<StoryWishCardMatch>();
        var match = await repository.FirstOrDefaultAsync(
            existing => existing.StoryId == storyId && existing.WishCardId == wishCardId && !existing.IsDeleted,
            cancellationToken: ct)
            ?? throw new NotFoundException("Thẻ của truyện", wishCardId);

        repository.Delete(match);
        await _unitOfWork.SaveChangesAsync(ct);
        await _auditLogWriter.LogAsync(actorUserId, "WishCardMatchRemoved", nameof(StoryWishCardMatch), match.Id,
            new { storyId, wishCardId, source = match.Source.ToString() }, null, ct);
        await _auditLogWriter.LogAsync(actorUserId, WishCardMappingAudit.Action, WishCardMappingAudit.EntityType, storyId,
            null, new { operation = "Remove", wishCardIds = new[] { wishCardId } }, ct);
    }

    private async Task RequireStoryAsync(int storyId, CancellationToken ct)
    {
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(storyId, ct);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", storyId);
        }
    }

    private async Task<T> WithStoryLockAsync<T>(int storyId, Func<Task<T>> action, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            var result = await action();
            await _unitOfWork.CommitTransactionAsync(ct);
            return result;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }

    private static WishCardMatchDto ToDto(StoryWishCardMatch match, WishCard card) => new()
    {
        StoryId = match.StoryId,
        WishCardId = card.Id,
        Group = card.Group.ToString(),
        Label = card.Label,
        Source = match.Source.ToString()
    };
}
