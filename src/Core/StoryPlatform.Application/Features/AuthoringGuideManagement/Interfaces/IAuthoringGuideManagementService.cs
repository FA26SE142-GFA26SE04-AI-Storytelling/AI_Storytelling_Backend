using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AuthoringGuideManagement.DTOs;

namespace StoryPlatform.Application.Features.AuthoringGuideManagement.Interfaces;

/// <summary>Moderator / Administrator quản lý bộ hướng dẫn sáng tác của Parent: bản nháp chung, xem trước, phát hành cả bộ.</summary>
public interface IAuthoringGuideManagementService
{
    Task<GuideManagementOverviewDto> GetOverviewAsync(int actorUserId, CancellationToken ct = default);
    Task<PagedResult<GuideVersionSummaryDto>> ListVersionsAsync(int actorUserId, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<GuideVersionDetailDto> GetVersionAsync(int actorUserId, int versionId, CancellationToken ct = default);
    Task<GuideDraftChangeDto> CreateDraftAsync(int actorUserId, CreateGuideDraftRequest request, CancellationToken ct = default);
    Task<GuideDraftChangeDto> UpsertItemAsync(int actorUserId, string guideId, UpsertGuideItemRequest request, CancellationToken ct = default);
    Task<GuideDraftChangeDto> RemoveItemAsync(int actorUserId, string guideId, string? revisionToken, CancellationToken ct = default);
    /// <summary>Hủy bản nháp hiện tại bằng xóa mềm; bản đã phát hành không bao giờ bị hủy.</summary>
    Task<GuideDraftDiscardDto> DiscardDraftAsync(int actorUserId, string? revisionToken, CancellationToken ct = default);
    Task<GuideDraftPreviewDto> PreviewAsync(int actorUserId, PreviewGuideDraftRequest request, CancellationToken ct = default);
    Task<GuidePublishResultDto> PublishAsync(int actorUserId, PublishGuideDraftRequest request, CancellationToken ct = default);
}
