using System.Collections.Concurrent;
using System.Text.Json;
using StoryPlatform.Application.Features.StoryReview.DTOs;

namespace StoryPlatform.Application.Features.StoryReview.Services;

public sealed class InMemoryProposalCache : IProposalCache
{
    private readonly ConcurrentDictionary<string, CachedProposal> _cache = new();
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromHours(1);

    public Task<string> CreateAsync(AIProposalDto proposal)
    {
        var id = Guid.NewGuid().ToString("N");
        var cached = new CachedProposal(proposal with { ProposalId = id }, DateTime.UtcNow.Add(_defaultExpiration));
        _cache[id] = cached;
        return Task.FromResult(id);
    }

    public Task<AIProposalDto?> GetAsync(string proposalId)
    {
        if (_cache.TryGetValue(proposalId, out var cached))
        {
            if (DateTime.UtcNow < cached.ExpiresAt)
            {
                return Task.FromResult<AIProposalDto?>(cached.Proposal);
            }
            // Remove expired
            _cache.TryRemove(proposalId, out _);
        }
        return Task.FromResult<AIProposalDto?>(null);
    }

    public Task UpdateStatusAsync(string proposalId, string status)
    {
        if (_cache.TryGetValue(proposalId, out var cached))
        {
            var updated = cached.Proposal with { Status = status };
            _cache[proposalId] = cached with { Proposal = updated };
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string proposalId)
    {
        _cache.TryRemove(proposalId, out _);
        return Task.CompletedTask;
    }

    public Task CleanupExpiredAsync()
    {
        var now = DateTime.UtcNow;
        var expired = _cache.Where(kvp => now >= kvp.Value.ExpiresAt).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
        {
            _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    private record CachedProposal(AIProposalDto Proposal, DateTime ExpiresAt);
}

public interface IProposalCache
{
    Task<string> CreateAsync(AIProposalDto proposal);
    Task<AIProposalDto?> GetAsync(string proposalId);
    Task UpdateStatusAsync(string proposalId, string status);
    Task DeleteAsync(string proposalId);
}
