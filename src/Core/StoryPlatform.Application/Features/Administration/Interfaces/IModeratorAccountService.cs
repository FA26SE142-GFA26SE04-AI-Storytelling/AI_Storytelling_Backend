using StoryPlatform.Application.Features.Administration.DTOs;

namespace StoryPlatform.Application.Features.Administration.Interfaces;

/// <summary>
/// Administrator quản lý vòng đời tài khoản Moderator: mời, gửi lại lời mời,
/// xem danh sách, vô hiệu hóa và kích hoạt lại.
/// </summary>
public interface IModeratorAccountService
{
    Task<ModeratorAccountDto> InviteAsync(
        int actorAdminUserId, InviteModeratorRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ModeratorAccountDto> ResendInviteAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default);

    Task<List<ModeratorAccountDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ModeratorAccountDto> DeactivateAsync(
        int actorAdminUserId, int targetUserId, string reason,
        CancellationToken cancellationToken = default);

    Task<ModeratorAccountDto> ReactivateAsync(
        int actorAdminUserId, int targetUserId,
        CancellationToken cancellationToken = default);
}
