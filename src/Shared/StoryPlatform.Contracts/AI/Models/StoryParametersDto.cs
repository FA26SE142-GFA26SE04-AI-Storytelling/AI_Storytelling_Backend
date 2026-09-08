namespace StoryPlatform.Contracts.AI.Models;

public sealed record StoryParametersDto
{
    public IReadOnlyList<string> Characters { get; init; } = [];
    public string Setting { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string Lesson { get; init; } = string.Empty;
    public int RequestedLength { get; init; } = 500;
}
