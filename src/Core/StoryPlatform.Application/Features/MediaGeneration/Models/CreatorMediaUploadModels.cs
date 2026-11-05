using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Application.Features.MediaGeneration.Models;

public sealed record CreatorMediaCommand(
    [Required, StringLength(100, MinimumLength = 1)] string OperationId,
    [Range(1, int.MaxValue)] int StoryVersionId,
    [Range(1, int.MaxValue)] int PackageRevision,
    [Required] bool? ExpectedConfirmed);
public sealed record CreatorMediaUploadCommand(CreatorMediaCommand Command,
    string SourceDeclaration, string RightsDeclaration);
public sealed record CreatorMediaCommandResult(int AssetId, int StoryVersionId, int PackageRevision, string Status, string ProgressUrl, string PackageUrl);
public interface IMediaUploadValidator
{
    void Validate(GeneratedMedia media, bool image);
}
