namespace StoryPlatform.Domain.Entities;

/// <summary>Source, rights and assistance provenance for one content snapshot.</summary>
public sealed class StoryVersionAuthoringMetadata
{
    public int StoryVersionId { get; set; }
    public int StoryId { get; set; }
    public string? AuthoringOrigin { get; set; }
    public string? AssistanceProvenanceJson { get; set; }
    public string SourceDeclaration { get; set; } = string.Empty;
    public string RightsDeclaration { get; set; } = string.Empty;
    public string? ImportFileName { get; set; }
    public string? ImportSourceHash { get; set; }
    public string? ImportPreviewHash { get; set; }
}
