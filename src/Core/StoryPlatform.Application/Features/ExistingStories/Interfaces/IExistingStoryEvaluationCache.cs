using StoryPlatform.Application.Features.ExistingStories.DTOs;

namespace StoryPlatform.Application.Features.ExistingStories.Interfaces;

public interface IExistingStoryEvaluationCache
{
    ExistingStoryEvaluationDto? Get(int storyId, int storyVersionId);
    void Set(ExistingStoryEvaluationDto evaluation);
}
