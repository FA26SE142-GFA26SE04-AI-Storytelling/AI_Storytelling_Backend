using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public partial class ChildRoadmapService : IChildRoadmapService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;
    private readonly IChildRoadmapEligibility _eligibility;
    private readonly IChildRoadmapInserter _inserter;
    private readonly IChildRoadmapAdvancer _advancer;
    private readonly IChildRoadmapNotifier _notifier;
    private readonly IAuditLogWriter _auditLogWriter;

    public ChildRoadmapService(
        IUnitOfWork unitOfWork,
        IChildProfileAccessGuard guard,
        IChildRoadmapEligibility eligibility,
        IChildRoadmapInserter inserter,
        IChildRoadmapAdvancer advancer,
        IChildRoadmapNotifier notifier,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
        _eligibility = eligibility;
        _inserter = inserter;
        _advancer = advancer;
        _notifier = notifier;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<AddRoadmapStoryResultDto> AddStoryAsync(
        int actorUserId, AddRoadmapStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        var childIds = request.ChildProfileIds?.Distinct().ToList() ?? new List<int>();
        if (childIds.Count == 0 || childIds.Count > ChildRoadmapPolicy.MaxChildrenPerRequest)
        {
            throw new BadRequestException($"Phải chọn từ 1 đến {ChildRoadmapPolicy.MaxChildrenPerRequest} con.");
        }

        if (!Enum.IsDefined(request.Position))
        {
            throw new BadRequestException("Vị trí phải là NextUp (đọc tiếp theo) hoặc End (thêm vào cuối).");
        }

        var days = ValidateExpectedDays(request.ExpectedDays);
        var note = ValidateNote(request.Note);
        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken);
        if (story == null || story.IsDeleted)
        {
            throw new NotFoundException("Truyện", request.StoryId);
        }

        var result = new AddRoadmapStoryResultDto();
        foreach (var childId in childIds)
        {
            result.Results.Add(await AddForChildAsync(
                actorUserId, childId, story, request.Position, days, note, cancellationToken));
        }

        return result;
    }

    public async Task<ChildRoadmapDto> GetAsync(
        int actorUserId, int childProfileId, CancellationToken cancellationToken = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, actorUserId, cancellationToken);
        return await BuildRoadmapAsync(childProfileId, cancellationToken);
    }

    private async Task<ChildAddResultDto> AddForChildAsync(
        int actorUserId, int childId, Story story, RoadmapInsertPosition position, int? days, string? note,
        CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childId, cancellationToken);
        if (child == null || child.IsDeleted || !await CanAssignAsync(childId, actorUserId, cancellationToken))
        {
            return Skipped(childId, ChildRoadmapSkipCodes.NoPermission);
        }

        if (child.Status != ChildProfileStatus.Active)
        {
            return Skipped(childId, ChildRoadmapSkipCodes.ProfileInactive);
        }

        if (!await CanUseStoryAsync(actorUserId, child, story, cancellationToken))
        {
            return Skipped(childId, story.Scope == StoryScope.Parent && !story.ChildProfileId.HasValue
                ? ChildRoadmapSkipCodes.StoryUnavailable
                : ChildRoadmapSkipCodes.NoAccessToStory);
        }

        var unsuitable = await _eligibility.EvaluateForParentSelectionAsync(child, story, cancellationToken);
        if (unsuitable.HasValue)
        {
            return Skipped(childId, SkipCodeFor(unsuitable.Value));
        }

        var warnings = await BuildWarningsAsync(child, story, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var inserted = (await _inserter.InsertAsync(new InsertRequest(
                childId, actorUserId, new[] { new NewRoadmapStep(story.Id, days, note) }, position,
                ChildRoadmapItemSource.Manual, null, MoveExistingToNextUp: true), cancellationToken)).Single();

            if (!inserted.Created && !inserted.Moved)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                return Skipped(childId, ChildRoadmapSkipCodes.AlreadyInRoadmap, inserted.ItemId);
            }

            await _advancer.AdvanceAsync(childId, cancellationToken);
            await _auditLogWriter.LogAsync(
                actorUserId, inserted.Moved ? "ChildRoadmapStoryMoved" : "ChildRoadmapStoryAdded",
                nameof(ChildRoadmapItem), inserted.ItemId,
                beforeState: null,
                afterState: new { childProfileId = childId, storyId = story.Id, position = position.ToString() },
                cancellationToken: cancellationToken);
            await _notifier.NotifyOtherSupervisorsAsync(
                childId, actorUserId, NotificationType.RoadmapStoryAdded,
                new { childProfileId = childId, storyId = story.Id, position = position.ToString() },
                cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new ChildAddResultDto
            {
                ChildProfileId = childId,
                Outcome = inserted.Moved ? "MovedToNext" : "Added",
                ItemId = inserted.ItemId,
                Warnings = warnings
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }
    }

    private Task<bool> CanAssignAsync(int childId, int actorUserId, CancellationToken cancellationToken) =>
        _guard.IsOwnerAsync(childId, actorUserId, cancellationToken);

    private async Task<bool> CanUseStoryAsync(
        int actorUserId, ChildProfile target, Story story, CancellationToken cancellationToken)
    {
        if (story.Scope != StoryScope.Parent)
        {
            return true;
        }

        if (!story.ChildProfileId.HasValue)
        {
            return false;
        }

        if (story.ChildProfileId.Value == target.Id)
        {
            return true;
        }

        return await _guard.IsOwnerAsync(story.ChildProfileId.Value, actorUserId, cancellationToken)
               && await _guard.IsOwnerAsync(target.Id, actorUserId, cancellationToken);
    }

    private async Task<List<string>> BuildWarningsAsync(
        ChildProfile target, Story story, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        if (story.AgeBand != StoryPlatform.Application.Features.RoadmapTemplates.RoadmapAge.ToText(target.AgeBand))
        {
            warnings.Add($"Truyện dành cho nhóm {story.AgeBand} tuổi, khác nhóm tuổi của bé. Đã giao theo lựa chọn của phụ huynh.");
        }
        if (story.Scope == StoryScope.Parent
            && story.ChildProfileId.HasValue
            && story.ChildProfileId.Value != target.Id
            && story.ReadingLevel.HasValue)
        {
            var targetId = target.Id;
            var learning = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
                profile => profile.ChildProfileId == targetId && !profile.IsDeleted,
                cancellationToken: cancellationToken);
            if (learning != null && learning.ReadingLevel != story.ReadingLevel.Value)
            {
                warnings.Add("reading_level_differs");
            }
        }

        return warnings;
    }

    private async Task<ChildRoadmapDto> BuildRoadmapAsync(int childProfileId, CancellationToken cancellationToken)
    {
        var items = await _unitOfWork.Repository<ChildRoadmapItem>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);
        var byId = stories.ToDictionary(story => story.Id);

        ChildRoadmapItemDto Map(ChildRoadmapItem item)
        {
            byId.TryGetValue(item.StoryId, out var story);
            return new ChildRoadmapItemDto
            {
                Id = item.Id,
                StoryId = item.StoryId,
                Title = story?.Title,
                Position = item.Position,
                Status = item.Status.ToString(),
                Source = item.Source.ToString(),
                SkipReason = item.SkipReason?.ToString(),
                Note = item.Note,
                ExpectedDays = item.ExpectedDays,
                ExpectedCompletionAt = item.ExpectedCompletionAt,
                OpenedAt = item.OpenedAt,
                CompletedAt = item.CompletedAt,
                AddedByUserId = item.AddedByUserId,
                IsStoryAvailable = story != null && !story.IsDeleted
                                   && story.Status != StoryStatus.Rejected && story.ArchivedAt == null
            };
        }

        DateTime EndedAt(ChildRoadmapItem item) =>
            item.CompletedAt ?? item.SkippedAt ?? item.RemovedAt ?? item.UpdatedAt ?? item.CreatedAt;

        var completed = items.Count(item => item.Status == ChildRoadmapItemStatus.Completed);
        var open = items.FirstOrDefault(item => item.Status == ChildRoadmapItemStatus.Open);
        var waiting = items.Where(item => item.Status == ChildRoadmapItemStatus.Waiting)
            .OrderBy(item => item.Position).ThenBy(item => item.Id).ToList();

        return new ChildRoadmapDto
        {
            ChildProfileId = childProfileId,
            Open = open == null ? null : Map(open),
            Waiting = waiting.Select(Map).ToList(),
            History = items
                .Where(item => item.Status is ChildRoadmapItemStatus.Completed or ChildRoadmapItemStatus.Skipped
                    or ChildRoadmapItemStatus.Removed)
                .OrderByDescending(EndedAt).ThenByDescending(item => item.Id)
                .Take(ChildRoadmapPolicy.HistoryLimit).Select(Map).ToList(),
            CompletedCount = completed,
            TotalCount = completed + waiting.Count + (open == null ? 0 : 1)
        };
    }

    private static ChildAddResultDto Skipped(int childId, string code, int? itemId = null) => new()
    {
        ChildProfileId = childId,
        Outcome = "Skipped",
        SkipCode = code,
        Message = MessageFor(code),
        ItemId = itemId
    };

    private static string SkipCodeFor(ChildRoadmapSkipReason reason) => reason switch
    {
        ChildRoadmapSkipReason.BlockedTopic => ChildRoadmapSkipCodes.BlockedTopic,
        ChildRoadmapSkipReason.WrongAge => ChildRoadmapSkipCodes.WrongAge,
        ChildRoadmapSkipReason.TooLong => ChildRoadmapSkipCodes.TooLong,
        _ => ChildRoadmapSkipCodes.StoryUnavailable
    };

    private static string MessageFor(string code) => code switch
    {
        ChildRoadmapSkipCodes.NoPermission => "Bạn không có quyền giao truyện cho bé này.",
        ChildRoadmapSkipCodes.ProfileInactive => "Hồ sơ của bé đang không hoạt động.",
        ChildRoadmapSkipCodes.NoAccessToStory => "Bạn cần quyền duyệt truyện với cả hai bé để dùng truyện riêng của bé khác.",
        ChildRoadmapSkipCodes.BlockedTopic => "Truyện thuộc chủ đề đã bị chặn với bé này.",
        ChildRoadmapSkipCodes.WrongAge => "Truyện không đúng độ tuổi của bé này.",
        ChildRoadmapSkipCodes.TooLong => "Truyện dài hơn mức cho phép của bé này.",
        ChildRoadmapSkipCodes.StoryUnavailable => "Truyện hiện không còn đọc được.",
        ChildRoadmapSkipCodes.AlreadyInRoadmap => "Truyện đã có trong lộ trình của con.",
        _ => "Không thêm được truyện cho bé này."
    };

    private static int? ValidateExpectedDays(int? days)
    {
        if (days.HasValue
            && (days.Value < ChildRoadmapPolicy.MinExpectedDays || days.Value > ChildRoadmapPolicy.MaxExpectedDays))
        {
            throw new BadRequestException(
                $"Số ngày mong đọc xong từ {ChildRoadmapPolicy.MinExpectedDays} đến {ChildRoadmapPolicy.MaxExpectedDays}.");
        }

        return days;
    }

    private static string? ValidateNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > ChildRoadmapPolicy.MaxNoteLength)
        {
            throw new BadRequestException($"Lời nhắn tối đa {ChildRoadmapPolicy.MaxNoteLength} ký tự.");
        }

        return trimmed;
    }
}
