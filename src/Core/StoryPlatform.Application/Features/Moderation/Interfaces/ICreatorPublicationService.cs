using StoryPlatform.Application.Features.Moderation.DTOs;
namespace StoryPlatform.Application.Features.Moderation.Interfaces;
public interface ICreatorPublicationService
{
    Task<CreatorPublicationStatus> GetStatusAsync(int userId, int storyId, CancellationToken ct = default);
    Task<CreatorPublicationResult> RequestRemovalAsync(int userId, int storyId, CreatorRemovalRequest input, CancellationToken ct = default);
    Task<CreatorPublicationResult> DecideRemovalAsync(int userId, int itemId, RemovalDecisionRequest input, CancellationToken ct = default);
    Task<CreatorPublicationResult> CreateRevisionAsync(int userId, int storyId, CreatorRevisionRequest input, CancellationToken ct = default);
}
