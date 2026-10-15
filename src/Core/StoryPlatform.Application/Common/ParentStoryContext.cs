using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Common;

public static class ParentStoryContext
{
    public static int RequireChildId(Story story)
    {
        if (story.Scope != StoryScope.Parent) throw new NotFoundException("Story", story.Id);
        if (story.ChildProfileId is not > 0) throw new ConflictException("PARENT_STORY_CHILD_REQUIRED");
        return story.ChildProfileId.Value;
    }
}
