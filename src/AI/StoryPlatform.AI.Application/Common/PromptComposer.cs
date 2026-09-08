using System.Text.Json;
using StoryPlatform.AI.Application.Abstractions.Prompting;

namespace StoryPlatform.AI.Application.Common;

internal static class PromptComposer
{
    public static string Compose<T>(PromptTemplate template, T input)
    {
        var context = JsonSerializer.Serialize(input, JsonDefaults.Options);
        return template.Template.Replace("{{context}}", context, StringComparison.Ordinal);
    }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
