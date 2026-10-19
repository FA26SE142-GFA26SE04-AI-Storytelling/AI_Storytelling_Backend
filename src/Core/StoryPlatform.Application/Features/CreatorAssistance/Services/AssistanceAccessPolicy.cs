using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

public interface IAssistanceAccessPolicy
{
    StoryScope Scope { get; }
    string RouteSegment { get; }
    Task AuthorizeAsync(int user, Story story, CancellationToken ct);
    Task EnsureWritableAsync(Story story, CancellationToken ct);
    Task<string?> CaptureContextAsync(Story story, CancellationToken ct);
    Task ValidateContextAsync(Story story, string? snapshot, CancellationToken ct);
}

public sealed class CreatorAssistanceAccessPolicy : IAssistanceAccessPolicy
{
    public StoryScope Scope => StoryScope.Creator;
    public string RouteSegment => "creator";
    public Task AuthorizeAsync(int user, Story story, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureWritableAsync(Story story, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> CaptureContextAsync(Story story, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task ValidateContextAsync(Story story, string? snapshot, CancellationToken ct) => Task.CompletedTask;
}
