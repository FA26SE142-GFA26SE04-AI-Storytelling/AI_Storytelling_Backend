using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.DTOs;

namespace StoryPlatform.Application.Features.ChildProfiles.ParentalGate.Interfaces;

/// <summary>
/// Verifies a supervisor's real email and password before the UI leaves a child session for a
/// guardian or administrator area. This is a transient identity check and never creates a session.
/// </summary>
public interface IParentalGateService
{
    Task VerifyAsync(
        VerifyParentalGateRequestDto request,
        CancellationToken cancellationToken = default);
}
