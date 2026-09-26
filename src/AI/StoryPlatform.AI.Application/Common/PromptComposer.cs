using System.Text.Json;
using System.Text.Json.Nodes;
using StoryPlatform.AI.Application.Abstractions.Prompting;

namespace StoryPlatform.AI.Application.Common;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

internal static class PromptComposer
{
    public static string Compose<T>(PromptTemplate template, T input)
    {
        var node = JsonSerializer.SerializeToNode(input, JsonDefaults.Options)
            ?? throw new JsonException("The generation context is empty.");
        if (node is JsonObject contextObject)
            contextObject.Remove("snapshot"); // Never disclose pinned prompts/config to the LLM as story input.
        var context = node.ToJsonString(JsonDefaults.Options);
        return template.Template.Replace("{{context}}", context, StringComparison.Ordinal);
    }
}
