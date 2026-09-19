namespace StoryPlatform.AI.Infrastructure.LLM.Gemini;

public sealed class GeminiOptions
{
    public const string SectionName = "AI:Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string Model { get; set; } = "gemini-2.5-flash";
    public int TimeoutSeconds { get; set; } = 120;
}
