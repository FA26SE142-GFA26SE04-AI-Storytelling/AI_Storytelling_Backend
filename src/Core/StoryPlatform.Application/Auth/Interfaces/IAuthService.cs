using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Auth.DTOs;

namespace StoryPlatform.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<UserProfileDto> GetCurrentUserProfileAsync(int userId, CancellationToken cancellationToken = default);
}
