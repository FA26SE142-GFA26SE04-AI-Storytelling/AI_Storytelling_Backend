using StoryPlatform.Application.Features.Auth.DTOs;

namespace StoryPlatform.Application.Features.Auth.Interfaces;

/// <summary>
/// Kích hoạt tài khoản Moderator được mời bằng mã mời dùng một lần.
/// </summary>
public interface IStaffActivationService
{
    Task ActivateAsync(
        StaffActivationRequestDto request,
        CancellationToken cancellationToken = default);
}
