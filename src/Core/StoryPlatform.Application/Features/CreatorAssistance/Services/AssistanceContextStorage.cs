using System.Text.Json;
using System.Text.Json.Nodes;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Contracts.AI.CreatorAssistance;

namespace StoryPlatform.Application.Features.CreatorAssistance.Services;

// Only Core writes this field. Public request DTOs deliberately do not contain it.
public static class AssistanceContextStorage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Store(CreateAssistanceTaskRequest request, string? serverContext)
    {
        var node = JsonSerializer.SerializeToNode(request, Json)!;
        if (serverContext is not null) node["serverContext"] = serverContext;
        return node.ToJsonString(Json);
    }

    public static string? ServerContext(string json) => JsonNode.Parse(json)?["serverContext"]?.GetValue<string>();
    public static AssistanceChildPolicy? ChildPolicy(string json) => ServerContext(json) is { } context
        ? JsonSerializer.Deserialize<ParentAssistanceContext>(context, Json)?.Policy : null;
}
