using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IParentStorySummaryService
{
    Task<PagedResult<ParentStorySummaryItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentStorySummaryQueryDto query, CancellationToken ct = default);

    Task DeleteAsync(int parentUserId, int childProfileId, int summaryId, CancellationToken ct = default);

    /// <summary>Dọn mọi bản ghi âm của trẻ khi xóa tài khoản chủ hồ sơ (không kiểm quyền — gọi từ hệ thống).</summary>
    Task PurgeChildAsync(int childProfileId, CancellationToken ct = default);
}
