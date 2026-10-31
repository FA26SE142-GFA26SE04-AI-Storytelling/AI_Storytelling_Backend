using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Contracts.AI.WishCards;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.WishCards.Services;

public sealed class WishCardAiMatchService(IUnitOfWork uow, IWishCardMatchingAiClient ai, IAuditLogWriter audit,
    IWishCardMappingChangeReader changes)
{
    public async Task<StoryWishCardAiMatchResult> MatchAsync(int actorId, int storyId, int versionId, CancellationToken ct)
    {
        MatchWishCardsRequest request;
        string mappingFingerprint;
        int watermark;
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(storyId, ct);
            await AuthorizeAsync(actorId, ct);
            request = await SnapshotAsync(storyId, versionId, ct);
            mappingFingerprint = WishCardMappingAudit.Fingerprint(await uow.Repository<StoryWishCardMatch>()
                .FindAsync(x => x.StoryId == storyId && !x.IsDeleted, cancellationToken: ct));
            watermark = await changes.ReadWatermarkAsync(ct);
            await uow.CommitTransactionAsync(ct);
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            uow.DiscardTrackedChanges();
            throw;
        }
        if (request.Candidates.Count == 0) return new(storyId, versionId, 0, [], request.SourceFingerprint);
        MatchWishCardsResponse response;
        try
        {
            response = await ai.MatchAsync(request, ct);
            WishCardMatchingRules.ValidateResponse(request, response);
        }
        catch (System.Text.Json.JsonException) { throw new WishCardAiException("WISH_CARD_OUTPUT_INVALID", 502); }
        ct.ThrowIfCancellationRequested();

        // No provider work under a transaction lock. Re-read authority, current content and catalog before writing.
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(storyId, ct);
            await AuthorizeAsync(actorId, ct);
            var fresh = await SnapshotAsync(storyId, versionId, ct);
            if (fresh.SourceFingerprint != request.SourceFingerprint) throw new ConflictException("WISH_CARD_SOURCE_STALE");
            var repository = uow.Repository<StoryWishCardMatch>();
            var existing = await repository.FindAsync(x => x.StoryId == storyId && !x.IsDeleted, cancellationToken: ct);
            if (WishCardMappingAudit.Fingerprint(existing) != mappingFingerprint ||
                await changes.HasChangesAfterAsync(storyId, watermark, ct))
                throw new ConflictException("WISH_CARD_MAPPING_STALE");
            var added = 0;
            foreach (var match in response.Matches.Where(x => !existing.Any(row => row.WishCardId == x.CardId)))
            {
                await repository.AddAsync(new StoryWishCardMatch
                {
                    StoryId = storyId, WishCardId = match.CardId, Source = WishCardMatchSource.Ai,
                    CreatedByUserId = actorId
                }, ct);
                added++;
            }
            await uow.SaveChangesAsync(ct);
            await audit.LogAsync(actorId, "WishCardsMatchedByAi", nameof(Story), storyId, null,
                new { versionId, request.SourceFingerprint, response.Model, response.PromptVersion,
                    cardIds = response.Matches.Select(x => x.CardId).ToArray(), addedCount = added }, ct);
            if (added > 0)
                await audit.LogAsync(actorId, WishCardMappingAudit.Action, WishCardMappingAudit.EntityType, storyId,
                    null, new { operation = "AiAppend", wishCardIds = response.Matches.Where(x =>
                        !existing.Any(row => row.WishCardId == x.CardId)).Select(x => x.CardId).ToArray() }, ct);
            await uow.CommitTransactionAsync(ct);
            return new(storyId, versionId, added, response.Matches, request.SourceFingerprint);
        }
        catch
        {
            await uow.RollbackTransactionAsync(CancellationToken.None);
            uow.DiscardTrackedChanges();
            throw;
        }
    }

    private async Task AuthorizeAsync(int actorId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!await uow.Repository<UserAccount>().ExistsAsync(x => x.Id == actorId && !x.IsDeleted &&
            x.Status == AccountStatus.Active && (x.Role == UserRole.Moderator || x.Role == UserRole.Administrator) &&
            (x.LockedUntil == null || x.LockedUntil <= now), ct)) throw new ForbiddenException();
    }

    private async Task<MatchWishCardsRequest> SnapshotAsync(int storyId, int versionId, CancellationToken ct)
    {
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId && !x.IsDeleted,
            cancellationToken: ct) ?? throw new NotFoundException("Story", storyId);
        // Private family stories are not sent to this catalog classifier by a staff operation.
        if (story.Scope != StoryScope.Creator) throw new ForbiddenException();
        if (story.Status != StoryStatus.Ready || story.ArchivedAt != null) throw new ConflictException("STORY_NOT_READY");
        var version = await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == versionId &&
            x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct)
            ?? throw new ConflictException("CURRENT_STORY_VERSION_REQUIRED");
        if (string.IsNullOrWhiteSpace(version.Content)) throw new ConflictException("STORY_CONTENT_REQUIRED");
        var older = story.AgeBand == "9-10";
        var age = older ? AgeBand.Age_9_10 : AgeBand.Age_6_8;
        var cards = await uow.Repository<WishCard>().FindAsync(x => x.IsActive && !x.IsDeleted &&
            (x.AgeBand == null || x.AgeBand == age) && (x.Group == WishCardGroup.Character || x.Group == WishCardGroup.Place ||
                x.Group == WishCardGroup.Challenge || x.Group == WishCardGroup.Lesson), cancellationToken: ct);
        if (cards.Count > WishCardMatchingRules.MaximumCards) throw new BadRequestException("WISH_CARD_CATALOG_TOO_LARGE");
        var request = new MatchWishCardsRequest
        {
            RequestId = "wish-cards-" + Guid.NewGuid().ToString("N"), Title = version.Title, Content = version.Content,
            AgeBand = story.AgeBand, Language = story.Language,
            Candidates = cards.OrderBy(x => x.Id).Select(x => new WishCardCandidate(x.Id, x.Group.ToString(), x.Label)).ToArray()
        };
        request = request with { SourceFingerprint = WishCardMatchingRules.Fingerprint(request) };
        if (cards.Count > 0)
        {
            try { WishCardMatchingRules.ValidateRequest(request); }
            catch (ArgumentException) { throw new BadRequestException("WISH_CARD_REQUEST_INVALID"); }
        }
        return request;
    }
}

public sealed class WishCardAiException(string code, int statusCode) : AppException(code, statusCode);
