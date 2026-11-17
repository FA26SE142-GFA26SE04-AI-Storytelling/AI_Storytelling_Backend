namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

/// <summary>Cảm nhận sau khi đọc, phía trẻ.</summary>
public interface IChildStoryReflectionService
{
    Task<ChildStoryReflectionDto> GetAsync(int childProfileId, int sessionId, CancellationToken ct = default);
    Task<ChildStoryReflectionDto> SetEmotionsAsync(int childProfileId, int sessionId, SetStoryEmotionsRequestDto request, CancellationToken ct = default);
    Task<ChildStoryReflectionDto> SetFavoriteSceneAsync(int childProfileId, int sessionId, SetFavoriteSceneRequestDto request, CancellationToken ct = default);
    Task<ChildStoryReflectionDto> SendNoteAsync(int childProfileId, int sessionId, StoryReflectionNoteRequest request, CancellationToken ct = default);
}
