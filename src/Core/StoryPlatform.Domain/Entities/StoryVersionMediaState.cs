namespace StoryPlatform.Domain.Entities;

/// <summary>Package lifecycle, independent of content versions. Revision zero denotes a legacy completed package.</summary>
public sealed class StoryVersionMediaState
{
    public int StoryVersionId { get; set; }
    public int MediaPackageRevision { get; set; }
    public int? ConfirmedMediaPackageRevision { get; set; }
    public int? MediaConfirmedByUserId { get; set; }
    public DateTime? MediaConfirmedAt { get; set; }
}
