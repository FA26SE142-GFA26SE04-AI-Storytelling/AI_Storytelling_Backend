using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Administration.Interfaces;

/// <summary>Administrator xem, vô hiệu hoá và kích hoạt lại tài khoản Content Creator (Luồng 5, M5).</summary>
public interface ICreatorAccountService
{
    /// <summary>Danh sách Creator, mới nhất trước; lọc theo trạng thái và tìm theo tên, username hoặc email.</summary>
    Task<PagedResult<CreatorAccountDto>> ListAsync(
        AccountStatus? status, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    Task<CreatorAccountDto> GetAsync(int creatorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vô hiệu hoá Creator đang Active (lý do bắt buộc): khoá đăng nhập, thu hồi phiên, rút yêu cầu gửi duyệt đang chờ
    /// (hoàn lượt) và đóng mục Moderator đã nhận. Truyện đã Published giữ nguyên.
    /// </summary>
    Task<DeactivateCreatorResultDto> DeactivateAsync(
        int adminUserId, int creatorUserId, DeactivateCreatorRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>Kích hoạt lại Creator đã bị vô hiệu hoá.</summary>
    Task<CreatorAccountDto> ReactivateAsync(int adminUserId, int creatorUserId, CancellationToken cancellationToken = default);
}
