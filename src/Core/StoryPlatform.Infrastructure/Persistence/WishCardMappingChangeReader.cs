using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Infrastructure.Persistence;

public sealed class WishCardMappingChangeReader(ApplicationDbContext db) : IWishCardMappingChangeReader
{
    // Call under the story workflow lock, on the same context/transaction as the mapping snapshot.
    public Task<int> ReadWatermarkAsync(CancellationToken ct) => db.Set<AuditLog>().AsNoTracking()
        .OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);

    public Task<bool> HasChangesAfterAsync(int storyId, int watermark, CancellationToken ct) =>
        db.Set<AuditLog>().AsNoTracking().AnyAsync(x => x.Id > watermark && x.Action == WishCardMappingAudit.Action &&
            x.EntityType == WishCardMappingAudit.EntityType && x.EntityId == storyId, ct);
}
