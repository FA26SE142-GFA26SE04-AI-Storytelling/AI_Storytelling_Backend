using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

public static class ParentStoryContext
{
    public static bool IsEditorStory(Story story) =>
        story.Scope == StoryScope.Parent && story.Source == StorySource.Manual &&
        !string.IsNullOrWhiteSpace(story.CreateOperationId);

    public static int RequireChildId(Story story)
    {
        if (IsEditorStory(story)) throw new ConflictException("PARENT_MANUAL_USE_AUTHORING_API");
        if (story.Scope != StoryScope.Parent) throw new NotFoundException("Story", story.Id);
        if (story.ChildProfileId is not > 0) throw new ConflictException("PARENT_STORY_CHILD_REQUIRED");
        return story.ChildProfileId.Value;
    }
}
