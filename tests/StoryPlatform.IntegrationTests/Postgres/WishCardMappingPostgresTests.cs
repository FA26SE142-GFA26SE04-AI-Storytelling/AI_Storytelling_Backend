using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Services;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using StoryPlatform.Application.Features.WishCards.Services;
using StoryPlatform.Contracts.AI.WishCards;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using StoryPlatform.Infrastructure.Persistence.Repositories;
using Xunit;

namespace StoryPlatform.IntegrationTests.Postgres;

// Opt-in only. The existing fixture creates/clones disposable databases; it never migrates the local application database.
public sealed class WishCardMappingPostgresTests(AuthoringGuidePostgresFixture fixture) : IClassFixture<AuthoringGuidePostgresFixture>
{
    [PostgresFact]
    public Task Moderator_removal_wins_against_inflight_AI() => RunRaceAsync(initialMapping: true);

    [PostgresFact]
    public Task Add_then_remove_invalidates_AI_even_when_mapping_set_is_unchanged() => RunRaceAsync(initialMapping: false);

    private async Task RunRaceAsync(bool initialMapping)
    {
        await using var database = await fixture.CreateDatabaseAsync();
        int storyId, versionId, cardId;
        const string content = "Mèo Mun nghỉ ngơi trong nhà.";
        await using (var seed = new ApplicationDbContext(database.Options))
        {
            var story = new Story { Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Ready,
                AuthorUserId = database.ModeratorId, Title = "Mèo Mun", Content = content, Language = "vi", AgeBand = "6-8" };
            var version = new StoryVersion { Story = story, VersionNo = 1, IsCurrent = true, Title = "Mèo Mun", Content = content };
            var card = new WishCard { Group = WishCardGroup.Character, Label = "Mèo Mun", IsActive = true,
                ImageUrl = "https://test.invalid/cat.png" };
            seed.AddRange(story, version, card);
            await seed.SaveChangesAsync();
            (storyId, versionId, cardId) = (story.Id, version.Id, card.Id);
            if (initialMapping)
            {
                seed.Set<StoryWishCardMatch>().Add(new() { StoryId = storyId, WishCardId = cardId,
                    Source = WishCardMatchSource.Moderator, CreatedByUserId = database.ModeratorId });
                await seed.SaveChangesAsync();
            }
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var aiDb = new ApplicationDbContext(database.Options);
        await using var aiUow = new UnitOfWork(aiDb);
        var client = new BlockingMatcher(cardId, content);
        var service = new WishCardAiMatchService(aiUow, client, new AuditLogWriter(aiUow), new WishCardMappingChangeReader(aiDb));
        var pending = service.MatchAsync(database.ModeratorId, storyId, versionId, deadline.Token);
        try
        {
            await client.Started.Task.WaitAsync(deadline.Token);
            await using var manualDb = new ApplicationDbContext(database.Options);
            await using var manualUow = new UnitOfWork(manualDb);
            var manual = new WishCardMatchService(manualUow, new AuditLogWriter(manualUow));
            if (!initialMapping) await manual.AddAsync(database.ModeratorId, storyId, cardId, deadline.Token);
            await manual.RemoveAsync(database.ModeratorId, storyId, cardId, deadline.Token);
        }
        catch
        {
            await deadline.CancelAsync();
            throw;
        }
        finally
        {
            client.Release.TrySetResult(true);
            if (deadline.IsCancellationRequested)
            {
                try { await pending; }
                catch (Exception) { /* Preserve the original assertion/timeout while draining the in-flight request. */ }
            }
        }
        var error = await Assert.ThrowsAsync<ConflictException>(async () => await pending.WaitAsync(deadline.Token));
        Assert.Equal("WISH_CARD_MAPPING_STALE", error.Message);
        await using var verify = new ApplicationDbContext(database.Options);
        Assert.Empty(await verify.Set<StoryWishCardMatch>().Where(x => x.StoryId == storyId).ToListAsync());
    }

    private sealed class BlockingMatcher(int cardId, string quote) : IWishCardMatchingAiClient
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MatchWishCardsResponse> MatchAsync(MatchWishCardsRequest request, CancellationToken ct)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(ct);
            return new() { RequestId = request.RequestId, SourceFingerprint = request.SourceFingerprint, Model = "fixture",
                Matches = [new(cardId, quote, "Nhân vật xuất hiện trong truyện.")] };
        }
    }
}
