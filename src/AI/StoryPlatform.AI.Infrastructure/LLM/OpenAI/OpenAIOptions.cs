namespace StoryPlatform.AI.Infrastructure.LLM.OpenAI;

public sealed class OpenAIOptions
{
    public const string SectionName = "AI:OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://api.openai.com/v1/responses";
    public string Model { get; set; } = "gpt-5-mini";
    public int TimeoutSeconds { get; set; } = 120;
}
