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
            "quiz":{"type":"array","minItems":3,"items":{"type":"object","properties":{"type":{"type":"string","enum":["multiple_choice","true_false","short_answer"]},"question":{"type":"string"},"options":{"type":"array","items":{"type":"string"}},"correctOptionIndex":{"type":"integer"},"correctAnswer":{"type":"string"},"explanation":{"type":"string"}},"required":["type","question","options","correctOptionIndex","correctAnswer","explanation"],"additionalProperties":false}},
            "discussionQuestions":{"type":"array","items":{"type":"object","properties":{"question":{"type":"string"}},"required":["question"],"additionalProperties":false}}
          },
          "required":["title","ageBand","readingLevel","vocabularyLevel","storySections","lesson","vocabulary","quiz","discussionQuestions"],
          "additionalProperties":false
        }
        """);

    public static JsonElement StoryContent => Parse("""
        {
          "type":"object",
          "properties":{
            "title":{"type":"string"},
            "description":{"type":"string"},
            "storySections":{"type":"array","minItems":1,"items":{"type":"object","properties":{"order":{"type":"integer"},"heading":{"type":"string"},"content":{"type":"string"}},"required":["order","heading","content"],"additionalProperties":false}},
            "lesson":{"type":"string"}
          },
          "required":["title","storySections","lesson"],
          "additionalProperties":false
        }
        """);

    public static JsonElement Vocabulary => Parse("""
        {
          "type":"object",
          "properties":{
            "items":{"type":"array","minItems":1,"items":{"type":"object","properties":{"term":{"type":"string"},"definition":{"type":"string"}},"required":["term","definition"],"additionalProperties":false}}
          },
          "required":["items"],
          "additionalProperties":false
        }
        """);

    public static JsonElement Quiz => Parse("""
        {
          "type":"object",
          "properties":{
            "items":{"type":"array","minItems":3,"items":{"type":"object","properties":{"type":{"type":"string","enum":["multiple_choice","true_false","short_answer"]},"question":{"type":"string"},"options":{"type":"array","items":{"type":"string"}},"correctOptionIndex":{"type":"integer"},"correctAnswer":{"type":"string"},"explanation":{"type":"string"}},"required":["type","question","options","correctOptionIndex","correctAnswer","explanation"],"additionalProperties":false}}
          },
          "required":["items"],
          "additionalProperties":false
        }
        """);

    public static JsonElement Discussion => Parse("""
        {
          "type":"object",
          "properties":{
            "items":{"type":"array","minItems":1,"items":{"type":"object","properties":{"question":{"type":"string"}},"required":["question"],"additionalProperties":false}}
          },
          "required":["items"],
          "additionalProperties":false
        }
        """);

    public static JsonElement ContentSafety => Parse("""
        {
          "type":"object",
          "properties":{
            "isAllowed":{"type":"boolean"},
            "canRefine":{"type":"boolean"},
            "reasonCode":{"type":"string"},
            "violations":{"type":"array","items":{"type":"string"}}
          },
          "required":["isAllowed","canRefine","reasonCode","violations"],
          "additionalProperties":false
        }
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
