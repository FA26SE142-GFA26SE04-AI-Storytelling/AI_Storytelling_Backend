using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.MediaGeneration.Services;

public static class MediaPackageConfirmation
{
    public static bool IsConfirmed(StoryVersion version) => version.MediaState.MediaPackageRevision == 0 ||
        version.MediaState.ConfirmedMediaPackageRevision == version.MediaState.MediaPackageRevision &&
        version.MediaState.MediaConfirmedByUserId.HasValue && version.MediaState.MediaConfirmedAt.HasValue;

    public static void Invalidate(StoryVersion version)
    {
        version.MediaState.MediaPackageRevision = checked(version.MediaState.MediaPackageRevision + 1);
        version.MediaState.ConfirmedMediaPackageRevision = null;
        version.MediaState.MediaConfirmedByUserId = null;
        version.MediaState.MediaConfirmedAt = null;
    }
}
