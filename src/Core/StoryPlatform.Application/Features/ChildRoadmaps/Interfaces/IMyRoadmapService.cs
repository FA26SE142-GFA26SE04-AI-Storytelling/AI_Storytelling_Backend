namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

/// <summary>Mục "Bố mẹ chọn cho em" trên Journey Map, phía trẻ.</summary>
public interface IMyRoadmapService
{
    Task<MyRoadmapDto> GetAsync(int childProfileId, CancellationToken cancellationToken = default);
}
