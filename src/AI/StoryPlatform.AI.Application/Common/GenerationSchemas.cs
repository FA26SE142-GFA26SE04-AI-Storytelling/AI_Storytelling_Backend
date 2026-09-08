using System.Text.Json;

namespace StoryPlatform.AI.Application.Common;

internal static class GenerationSchemas
{
    public static JsonElement Outline => Parse("""
        {
          "type":"object",
          "properties":{
            "title":{"type":"string"},
            "outline":{"type":"object","properties":{"opening":{"type":"string"},"development":{"type":"string"},"ending":{"type":"string"}},"required":["opening","development","ending"],"additionalProperties":false}
          },
          "required":["title","outline"],
          "additionalProperties":false
        }
        """);

    public static JsonElement StoryPackage => Parse("""
        {
          "type":"object",
          "properties":{
            "title":{"type":"string"},"ageBand":{"type":"string"},"readingLevel":{"type":"string"},"vocabularyLevel":{"type":"string"},
            "storySections":{"type":"array","items":{"type":"object","properties":{"order":{"type":"integer"},"heading":{"type":"string"},"content":{"type":"string"}},"required":["order","heading","content"],"additionalProperties":false}},
            "lesson":{"type":"string"},
            "vocabulary":{"type":"array","items":{"type":"object","properties":{"word":{"type":"string"},"meaning":{"type":"string"},"example":{"type":"string"}},"required":["word","meaning","example"],"additionalProperties":false}},
            "quiz":{"type":"array","items":{"type":"object","properties":{"question":{"type":"string"},"options":{"type":"array","items":{"type":"string"}},"correctOptionIndex":{"type":"integer"},"explanation":{"type":"string"}},"required":["question","options","correctOptionIndex","explanation"],"additionalProperties":false}},
            "discussionQuestions":{"type":"array","items":{"type":"object","properties":{"question":{"type":"string"}},"required":["question"],"additionalProperties":false}}
          },
          "required":["title","ageBand","readingLevel","vocabularyLevel","storySections","lesson","vocabulary","quiz","discussionQuestions"],
          "additionalProperties":false
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
