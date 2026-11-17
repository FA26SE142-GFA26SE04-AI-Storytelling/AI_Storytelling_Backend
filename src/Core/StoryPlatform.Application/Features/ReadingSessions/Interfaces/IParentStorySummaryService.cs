using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IParentStorySummaryService
{
    Task<PagedResult<ParentStorySummaryItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentStorySummaryQueryDto query, CancellationToken ct = default);

    Task DeleteAsync(int parentUserId, int childProfileId, int summaryId, CancellationToken ct = default);

    /// <summary>Chủ hồ sơ đánh dấu hoặc bỏ đánh dấu đã trả lời một câu bé hỏi.</summary>
    Task<StorySummaryAnsweredDto> SetAnsweredAsync(
        int parentUserId, int childProfileId, int summaryId, int questionId, bool answered, CancellationToken ct = default);

    /// <summary>Dọn mọi bản ghi âm (kể lại và câu hỏi) của trẻ khi xóa tài khoản chủ hồ sơ (không kiểm quyền — gọi từ hệ thống).</summary>
    Task PurgeChildAsync(int childProfileId, CancellationToken ct = default);
}
