using StoryPlatform.Application.Features.Administration.DTOs;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Administration.Interfaces;

/// <summary>
/// Administrator quản lý vòng đời tài khoản vận hành (Moderator và Administrator): mời,
/// gửi lại lời mời, hủy lời mời, xem danh sách, vô hiệu hóa và kích hoạt lại.
/// Vai trò chọn lúc mời; các thao tác theo userId tự đọc vai trò của tài khoản.
/// </summary>
public interface IStaffAccountService
{
    Task<List<StaffAccountDto>> ListAsync(
        UserRole? role, CancellationToken cancellationToken = default);

    Task<StaffAccountDto> InviteAsync(
        int actorAdminUserId, InviteStaffRequestDto request,
        CancellationToken cancellationToken = default);

    Task<StaffAccountDto> ResendInviteAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default);

    Task<StaffAccountDto> CancelInviteAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default);

    Task<StaffAccountDto> DeactivateAsync(
        int actorAdminUserId, int targetUserId, string reason,
        CancellationToken cancellationToken = default);

    Task<StaffAccountDto> ReactivateAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default);
}
