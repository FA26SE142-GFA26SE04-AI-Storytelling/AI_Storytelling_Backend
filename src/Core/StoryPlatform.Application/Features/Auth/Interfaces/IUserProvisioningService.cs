using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Auth.Interfaces;

/// <summary>
/// Creates an account that must set its password before first login.
/// The caller owns persistence of the complete business transaction and delivery of the setup token.
/// </summary>
public interface IUserProvisioningService
{
    Task<(UserAccount Account, string RawSetPasswordToken)> CreatePendingAccountAsync(
        string username, string email, string fullName, string? phoneNumber, UserRole role,
        CancellationToken cancellationToken = default);
}
