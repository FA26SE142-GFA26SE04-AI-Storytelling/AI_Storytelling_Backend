using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;

namespace StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;

public interface ICreatorAuthoringService
{
    Task<CreatorDraftDto> CreateDraftAsync(int userId, CreateCreatorDraftRequest request, CancellationToken ct = default);
    Task<PagedResult<CreatorDraftDto>> GetMineAsync(int userId, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<CreatorDraftDto> GetDraftAsync(int userId, int storyId, CancellationToken ct = default);
    Task<CreatorContentSavedDto> SaveContentAsync(int userId, int storyId, SaveCreatorContentRequest request, CancellationToken ct = default);
    Task<CreatorImportPreviewDto> PreviewImportAsync(int userId, int storyId, PreviewCreatorImportRequest request, CancellationToken ct = default);
    Task<CreatorImportPreviewDto> PreviewFileAsync(int userId, int storyId, string revisionToken,
        string operationId, string fileName, Stream content, CancellationToken ct = default);
    Task<CreatorContentSavedDto> ConfirmImportAsync(int userId, int storyId, ConfirmCreatorImportRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CreatorVersionDto>> GetVersionsAsync(int userId, int storyId, CancellationToken ct = default);
    Task<CreatorVersionDto> GetVersionAsync(int userId, int storyId, int versionId, CancellationToken ct = default);
    Task<CreatorContentSavedDto> RestoreVersionAsync(int userId, int storyId, RestoreCreatorVersionRequest request, CancellationToken ct = default);
}
