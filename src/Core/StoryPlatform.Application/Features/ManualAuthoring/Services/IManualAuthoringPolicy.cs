using System.Linq.Expressions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ManualAuthoring.Services;

public sealed record ManualDraftContext(int ChildProfileId, string? Purpose);

public interface IManualAuthoringPolicy
{
    StoryScope Scope { get; }
    Expression<Func<Story, bool>> VisibleStories(int userId);
    Task InitializeAsync(int userId, Story story, ManualDraftContext? context, CancellationToken ct);
    Task AuthorizeAsync(int userId, Story story, CancellationToken ct);
    Task EnsureWritableAsync(Story story, CancellationToken ct) => Task.CompletedTask;
    Task ValidateAssistanceContextAsync(Story story, string? context, CancellationToken ct) => Task.CompletedTask;
}

public sealed class CreatorManualAuthoringPolicy(StoryPlatform.Application.Abstractions.Persistence.IUnitOfWork uow) : IManualAuthoringPolicy
{
    public async Task EnsureWritableAsync(Story story, CancellationToken ct)
    {
        if (story.Source == StorySource.Ai) throw new ConflictException("AI_STORY_USE_GENERATION_REVIEW_API");
        await StoryPlatform.Application.Common.CreatorPublicationGuard.EnsureWritableAsync(uow, story.Id, null, ct);
        if (await uow.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == story.Id &&
            x.Operation == GenerationJobOperation.GenerateMediaPackage &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct))
            throw new ConflictException("MEDIA_JOB_ACTIVE");
    }
    public StoryScope Scope => StoryScope.Creator;
    public Expression<Func<Story, bool>> VisibleStories(int userId) =>
        story => story.Scope == StoryScope.Creator && story.AuthorUserId == userId && !story.IsDeleted;
    public Task InitializeAsync(int userId, Story story, ManualDraftContext? context, CancellationToken ct) => Task.CompletedTask;
    public Task AuthorizeAsync(int userId, Story story, CancellationToken ct)
    {
        if (story.Scope != Scope || story.AuthorUserId != userId) throw new NotFoundException("Story", story.Id);
        return Task.CompletedTask;
    }
}
