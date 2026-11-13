using StoryPlatform.Application.Features.StoryRatings.DTOs;

namespace StoryPlatform.Application.Features.StoryRatings.Interfaces;

public interface IStoryRatingService
{
    Task<StoryRatingListDto> GetForStoryAsync(
        int parentUserId, int storyId, StoryRatingQueryDto query, CancellationToken cancellationToken = default);

    Task<MyStoryRatingDto> UpsertMineAsync(
        int parentUserId, int storyId, UpsertStoryRatingRequestDto request, CancellationToken cancellationToken = default);

    Task<bool> DeleteMineAsync(int parentUserId, int storyId, CancellationToken cancellationToken = default);

    Task<StoryRatingListDto> GetForCreatorAsync(
        int creatorUserId, int storyId, StoryRatingQueryDto query, CancellationToken cancellationToken = default);

    Task<StoryRatingReportResultDto> ReportAsync(
        int userId, int ratingId, ReportStoryRatingRequestDto request, CancellationToken cancellationToken = default);
}
