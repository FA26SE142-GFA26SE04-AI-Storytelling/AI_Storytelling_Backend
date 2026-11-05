using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.MediaGeneration.Models;
using StoryPlatform.Application.Features.MediaStorage.Models;

namespace StoryPlatform.Infrastructure.AI;

public sealed class CreatorMediaUploadValidator : IMediaUploadValidator
{
    public void Validate(GeneratedMedia media, bool image)
    {
        var limit = (image ? 10 : 25) * 1024 * 1024;
        if (media.Content.Length == 0 || media.Content.Length > limit) throw new BadRequestException("MEDIA_FILE_SIZE_INVALID");
        var detected = image ? MagicByteValidators.ValidateImage(media.Content) : MagicByteValidators.ValidateAudio(media.Content);
        if (!detected.IsValid || !string.Equals(detected.DetectedMime, media.MimeType, StringComparison.OrdinalIgnoreCase))
            throw new BadRequestException("MEDIA_FILE_TYPE_INVALID");
    }
}
