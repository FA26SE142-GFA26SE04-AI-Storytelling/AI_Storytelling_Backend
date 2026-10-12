using StoryPlatform.Application.Features.Auth.DTOs;

namespace StoryPlatform.Application.Features.Auth.Interfaces;

public interface IAccountDeletionService
{
    Task<DeleteAccountPreviewDto> GetPreviewAsync(
        int userId, CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(
        int userId, DeleteAccountRequestDto request,
        CancellationToken cancellationToken = default);
}
