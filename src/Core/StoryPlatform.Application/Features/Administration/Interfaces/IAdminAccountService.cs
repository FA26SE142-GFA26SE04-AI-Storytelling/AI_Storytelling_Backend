using StoryPlatform.Application.Features.Administration.DTOs;

namespace StoryPlatform.Application.Features.Administration.Interfaces;

/// <summary>
/// Cấp/thu hồi quyền Administrator cho tài khoản người dùng (Bước 5.1b).
/// </summary>
public interface IAdminAccountService
{
    Task<AdministratorAccountDto> GrantAsync(
        int actorAdminUserId, GrantAdministratorRequestDto request, CancellationToken cancellationToken = default);

    Task<AdministratorAccountDto> RevokeAsync(
        int actorAdminUserId, int targetUserId, CancellationToken cancellationToken = default);
}
