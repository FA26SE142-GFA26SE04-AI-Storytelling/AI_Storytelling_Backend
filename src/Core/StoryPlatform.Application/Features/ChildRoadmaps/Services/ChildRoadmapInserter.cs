using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
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
        var live = await repository.FindAsync(
            item => item.ChildProfileId == childId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open
                        || item.Status == ChildRoadmapItemStatus.Completed),
            cancellationToken: cancellationToken);
        var current = RoadmapStages.Current(live);
        var hasOpen = live.Any(item => item.Status == ChildRoadmapItemStatus.Open);

        // FindAsync trả bản không theo dõi: lấy bản đang theo dõi của mọi truyện chờ trước khi sửa.
        var stages = new List<List<ChildRoadmapItem>>();
        // Position (số chặng) của từng chặng chờ, lấy trước khi đánh số lại; song song với `stages`.
        var stagePositions = new List<int>();
        foreach (var group in RoadmapStages.WaitingStages(live))
        {
            stagePositions.Add(group[0].Position);
            var tracked = new List<ChildRoadmapItem>();
            foreach (var candidate in group)
            {
                var item = await repository.GetByIdAsync(candidate.Id, cancellationToken);
                if (item != null)
                {
                    tracked.Add(item);
                }
            }

            stages.Add(tracked);
        }

        var waiting = stages.SelectMany(stage => stage).ToList();

        var joinsOpen = false;
        List<ChildRoadmapItem>? target = null;
        if (request.Position == RoadmapInsertPosition.Stage)
        {
            var requested = request.StageNumber ?? 0;
            var index = stagePositions.IndexOf(requested);
            if (hasOpen && requested == current)
            {
                joinsOpen = true;
            }
            else if (index >= 0 && index < stages.Count)
            {
                target = stages[index];
            }
            else
            {
                throw new BadRequestException($"Chặng {requested} không còn nhận thêm truyện.");
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

            var existing = live.FirstOrDefault(item =>
                item.StoryId == step.StoryId && item.Status != ChildRoadmapItemStatus.Completed);
            if (existing != null)
            {
                var trackedExisting = waiting.FirstOrDefault(item => item.Id == existing.Id);
                var move = request.MoveExisting
                           && request.Position != RoadmapInsertPosition.End
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

        foreach (var stage in stages)
        {
            stage.RemoveAll(moved.Contains);
        }

        var incoming = moved.Concat(fresh).ToList();
        if (joinsOpen)
        {
            var now = DateTime.UtcNow;
            foreach (var item in incoming)
            {
                item.Status = ChildRoadmapItemStatus.Open;
                item.Position = current;
                item.OpenedAt = now;
                item.ExpectedCompletionAt = item.ExpectedDays.HasValue ? now.AddDays(item.ExpectedDays.Value) : null;
            }
        }
        else if (target != null)
        {
            target.AddRange(incoming);
        }
        else if (request.Position == RoadmapInsertPosition.NextUp)
        {
            stages.InsertRange(0, incoming.Select(item => new List<ChildRoadmapItem> { item }));
        }
        else
        {
            stages.AddRange(incoming.Select(item => new List<ChildRoadmapItem> { item }));
        }

        var stageNumber = current;
        foreach (var stage in stages.Where(stage => stage.Count > 0))
        {
            stageNumber += 1;
            foreach (var item in stage)
            {
                item.Position = stageNumber;
            }
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
