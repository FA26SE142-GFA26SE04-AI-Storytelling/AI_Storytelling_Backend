using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public class ChildRoadmapInserter : IChildRoadmapInserter
{
    private readonly IUnitOfWork _unitOfWork;

    public ChildRoadmapInserter(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<List<InsertedStep>> InsertAsync(
        InsertRequest request, CancellationToken cancellationToken = default)
    {
        var repository = _unitOfWork.Repository<ChildRoadmapItem>();
        var childId = request.ChildProfileId;
        var current = await repository.FindAsync(
            item => item.ChildProfileId == childId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open),
            cancellationToken: cancellationToken);

        var waiting = new List<ChildRoadmapItem>();
        foreach (var candidate in current.Where(item => item.Status == ChildRoadmapItemStatus.Waiting)
                     .OrderBy(item => item.Position).ThenBy(item => item.Id))
        {
            var tracked = await repository.GetByIdAsync(candidate.Id, cancellationToken);
            if (tracked != null)
            {
                waiting.Add(tracked);
            }
        }

        var fresh = new List<ChildRoadmapItem>();
        var moved = new List<ChildRoadmapItem>();
        var outcomes = new List<(int StoryId, ChildRoadmapItem? Item, bool Created, bool Moved)>();
        var seenFresh = new Dictionary<int, ChildRoadmapItem>();

        foreach (var step in request.Steps)
        {
            if (seenFresh.TryGetValue(step.StoryId, out var duplicate))
            {
                outcomes.Add((step.StoryId, duplicate, false, false));
                continue;
            }

            var existing = current.FirstOrDefault(item => item.StoryId == step.StoryId);
            if (existing != null)
            {
                var trackedExisting = waiting.FirstOrDefault(item => item.Id == existing.Id);
                var move = request.MoveExistingToNextUp
                           && request.Position == RoadmapInsertPosition.NextUp
                           && trackedExisting != null
                           && !moved.Contains(trackedExisting);
                if (move)
                {
                    moved.Add(trackedExisting!);
                }

                outcomes.Add((step.StoryId, trackedExisting ?? existing, false, move));
                continue;
            }

            var note = step.Note?.Trim();
            var row = new ChildRoadmapItem
            {
                ChildProfileId = childId,
                StoryId = step.StoryId,
                Status = ChildRoadmapItemStatus.Waiting,
                Source = request.Source,
                SourceReadingRoadmapId = request.SourceReadingRoadmapId,
                AddedByUserId = request.ActorUserId,
                Note = string.IsNullOrEmpty(note) ? null : note,
                ExpectedDays = step.ExpectedDays
            };
            fresh.Add(row);
            seenFresh[step.StoryId] = row;
            outcomes.Add((step.StoryId, row, true, false));
        }

        var rest = waiting.Where(item => !moved.Contains(item)).ToList();
        var ordered = request.Position == RoadmapInsertPosition.NextUp
            ? moved.Concat(fresh).Concat(rest).ToList()
            : rest.Concat(fresh).ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Position = index + 1;
        }

        foreach (var item in waiting)
        {
            repository.Update(item);
        }

        if (fresh.Count > 0)
        {
            await repository.AddRangeAsync(fresh, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return outcomes.Select(outcome => new InsertedStep(
            outcome.StoryId, outcome.Item?.Id ?? 0, outcome.Created, outcome.Moved)).ToList();
    }
}
