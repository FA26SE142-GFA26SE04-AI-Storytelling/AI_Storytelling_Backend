using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

public static class AssistanceGenerationAccess
{
    public static async Task EnsureWritableAsync(IUnitOfWork uow, Story story, CancellationToken ct)
    {
        if (story.Source != StorySource.Ai) return;
        StoryPlatform.Application.Common.GenerationStoryAccess.EnsureSupported(story);
        if (story.Status != StoryStatus.ContentReview || story.ArchivedAt != null || story.IsPublished)
            throw new AssistanceException("AI_STORY_NOT_EDITABLE", 409);
        if (await uow.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == story.Id && !x.IsDeleted &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), ct))
            throw new AssistanceException("GENERATION_JOB_ACTIVE", 409);
    }
}
