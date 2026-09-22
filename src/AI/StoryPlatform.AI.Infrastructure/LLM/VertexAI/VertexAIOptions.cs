namespace StoryPlatform.AI.Infrastructure.LLM.VertexAI;

public sealed class VertexAIOptions
{
    public const string SectionName = "AI:Google";

    public string AuthMode { get; set; } = "Adc";
    public string ProjectId { get; set; } = "gen-lang-client-0675088605";
    public string Location { get; set; } = "global";
    public string Model { get; set; } = "gemini-3.8-flash";
    public int TimeoutSeconds { get; set; } = 120;
}
