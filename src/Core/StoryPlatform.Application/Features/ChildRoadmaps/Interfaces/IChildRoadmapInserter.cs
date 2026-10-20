using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;

public sealed record NewRoadmapStep(int StoryId, int? ExpectedDays, string? Note);

public sealed record InsertRequest(
    int ChildProfileId,
    int ActorUserId,
    IReadOnlyList<NewRoadmapStep> Steps,
    RoadmapInsertPosition Position,
    ChildRoadmapItemSource Source,
    int? SourceReadingRoadmapId,
    bool MoveExistingToNextUp);

public sealed record InsertedStep(int StoryId, int ItemId, bool Created, bool Moved);

public interface IChildRoadmapInserter
{
    Task<List<InsertedStep>> InsertAsync(InsertRequest request, CancellationToken cancellationToken = default);
}
