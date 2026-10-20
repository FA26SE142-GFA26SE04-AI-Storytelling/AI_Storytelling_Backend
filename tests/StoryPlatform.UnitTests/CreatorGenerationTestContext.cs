using System.Text.Json;
using StoryPlatform.Application.Features.AIStoryInput.Models;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.UnitTests;

internal static class CreatorGenerationTestContext
{
    public static void Configure(Story story, StoryGenerationRequest request)
    {
        story.Scope = StoryScope.Creator; story.Source = StorySource.Ai; story.ChildProfileId = null;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var context = JsonSerializer.Deserialize<AIStoryInputContextSnapshot>(request.ContextSnapshotJson, options)!;
        request.ContextSnapshotJson = JsonSerializer.Serialize(context with
        {
            ChildProfileId = null, AudienceScope = "Creator", CreatorUserId = story.AuthorUserId,
            RequiredApprovalMode = "always_manual", ConsentRecordedAt = null
        }, options);
    }
}
