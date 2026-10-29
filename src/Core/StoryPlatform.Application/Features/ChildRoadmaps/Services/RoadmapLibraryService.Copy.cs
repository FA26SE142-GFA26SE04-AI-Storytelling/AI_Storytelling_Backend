using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildRoadmaps.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildRoadmaps.Services;

public static class RoadmapLibrarySkipCodes
{
    public const string NoValidStory = "no_valid_story";
}

public partial class RoadmapLibraryService
{
    private sealed record CopyContext(
        ReadingRoadmap Roadmap,
        IReadOnlyList<ReadingRoadmapItem> Items,
        IReadOnlyDictionary<int, Story> Stories,
        int? DaysPerStory,
        IReadOnlyList<int> ChildIds);

    public async Task<RoadmapCopyPlanDto> PreviewAsync(
        int actorUserId,
        int roadmapId,
        RoadmapCopyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await RequireParentAsync(actorUserId, cancellationToken);
        var context = await BuildContextAsync(roadmapId, request, cancellationToken);
        var plan = new RoadmapCopyPlanDto
        {
            RoadmapId = context.Roadmap.Id,
            RoadmapName = context.Roadmap.Name
        };
        foreach (var childId in context.ChildIds)
        {
            plan.Children.Add(await PlanForChildAsync(actorUserId, childId, context, cancellationToken));
        }

        return plan;
    }

    public async Task<RoadmapCopyPlanDto> CopyAsync(
        int actorUserId,
        int roadmapId,
        RoadmapCopyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        await RequireParentAsync(actorUserId, cancellationToken);
        var roadmap = await _unitOfWork.Repository<ReadingRoadmap>().GetByIdAsync(roadmapId, cancellationToken);
        if (roadmap == null || roadmap.IsDeleted)
        {
            throw new NotFoundException("Bộ truyện mẫu", roadmapId);
        }

        if (roadmap.Status != RoadmapTemplateStatus.Published || roadmap.ArchivedAt != null)
        {
            throw new ConflictException("Bộ truyện mẫu này không còn trong Kho nên không chép được.");
        }

        var context = await BuildContextAsync(roadmapId, request, cancellationToken);
        var result = new RoadmapCopyPlanDto { RoadmapId = roadmap.Id, RoadmapName = roadmap.Name };
        foreach (var childId in context.ChildIds)
        {
            result.Children.Add(await CopyForChildAsync(
                actorUserId, childId, context, request.Position, cancellationToken));
        }

        return result;
    }

    private async Task<CopyContext> BuildContextAsync(
        int roadmapId, RoadmapCopyRequestDto request, CancellationToken cancellationToken)
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

        if (request.DaysPerStory.HasValue
            && (request.DaysPerStory.Value < ChildRoadmapPolicy.MinExpectedDays
                || request.DaysPerStory.Value > ChildRoadmapPolicy.MaxExpectedDays))
        {
            throw new BadRequestException(
                $"Số ngày mỗi truyện từ {ChildRoadmapPolicy.MinExpectedDays} đến {ChildRoadmapPolicy.MaxExpectedDays}.");
        }

        var roadmap = await LoadPublishedRoadmapAsync(roadmapId, cancellationToken);
        var items = (await _unitOfWork.Repository<ReadingRoadmapItem>().FindAsync(
                item => item.ReadingRoadmapId == roadmapId, cancellationToken: cancellationToken))
            .OrderBy(item => item.Position).ToList();
        var storyIds = items.Select(item => item.StoryId).Distinct().ToList();
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            story => storyIds.Contains(story.Id), cancellationToken: cancellationToken);

        return new CopyContext(
            roadmap,
            items,
            stories.ToDictionary(story => story.Id),
            request.DaysPerStory,
            childIds);
    }

    private async Task<RoadmapChildPlanDto> PlanForChildAsync(
        int actorUserId, int childId, CopyContext context, CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childId, cancellationToken);
        if (child == null || child.IsDeleted || !await CanAssignAsync(childId, actorUserId, cancellationToken))
        {
            return SkippedChild(childId, ChildRoadmapSkipCodes.NoPermission);
        }

        if (child.Status != ChildProfileStatus.Active)
        {
            return SkippedChild(childId, ChildRoadmapSkipCodes.ProfileInactive);
        }

        var existing = await _unitOfWork.Repository<ChildRoadmapItem>().FindAsync(
            item => item.ChildProfileId == childId
                    && (item.Status == ChildRoadmapItemStatus.Waiting || item.Status == ChildRoadmapItemStatus.Open),
            cancellationToken: cancellationToken);
        var inRoadmap = existing.Select(item => item.StoryId).ToHashSet();
        var plan = new RoadmapChildPlanDto { ChildProfileId = childId };

        foreach (var item in context.Items)
        {
            context.Stories.TryGetValue(item.StoryId, out var story);
            var step = new RoadmapStepPlanDto
            {
                StoryId = item.StoryId,
                Title = story?.Title,
                Position = item.Position,
                Days = context.DaysPerStory ?? item.Days ?? context.Roadmap.DefaultDaysPerStory,
                Action = "Copy"
            };

            var unsuitable = await _eligibility.EvaluateAsync(child, story, cancellationToken);
            if (unsuitable.HasValue)
            {
                step.Action = "Skip";
                step.SkipCode = SkipCodeFor(unsuitable.Value);
            }
            else if (inRoadmap.Contains(item.StoryId))
            {
                step.Action = "Skip";
                step.SkipCode = ChildRoadmapSkipCodes.AlreadyInRoadmap;
            }

            step.Message = step.SkipCode == null ? null : MessageFor(step.SkipCode);
            plan.Steps.Add(step);
        }

        var learning = await _unitOfWork.Repository<LearningProfile>().FirstOrDefaultAsync(
            profile => profile.ChildProfileId == childId && !profile.IsDeleted,
            cancellationToken: cancellationToken);
        if (learning != null
            && (learning.ReadingLevel < context.Roadmap.ReadingLevelMin
                || learning.ReadingLevel > context.Roadmap.ReadingLevelMax))
        {
            plan.Warnings.Add("reading_level_outside_range");
        }

        if (plan.Steps.All(step => step.Action == "Skip"))
        {
            plan.Outcome = "Skipped";
            plan.SkipCode = RoadmapLibrarySkipCodes.NoValidStory;
            plan.Message = "Bộ này không có truyện nào chép được cho bé.";
        }
        else
        {
            plan.Outcome = "Copy";
        }

        return plan;
    }

    private async Task<RoadmapChildPlanDto> CopyForChildAsync(
        int actorUserId,
        int childId,
        CopyContext context,
        RoadmapInsertPosition position,
        CancellationToken cancellationToken)
    {
        var plan = await PlanForChildAsync(actorUserId, childId, context, cancellationToken);
        if (plan.Outcome != "Copy")
        {
            return plan;
        }

        var steps = plan.Steps.Where(step => step.Action == "Copy")
            .Select(step => new NewRoadmapStep(step.StoryId, step.Days, null)).ToList();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _inserter.InsertAsync(new InsertRequest(
                childId,
                actorUserId,
                steps,
                position,
                ChildRoadmapItemSource.TemplateCopy,
                context.Roadmap.Id,
                MoveExistingToNextUp: false), cancellationToken);
            await _advancer.AdvanceAsync(childId, cancellationToken);
            await _auditLogWriter.LogAsync(
                actorUserId,
                "ChildRoadmapTemplateCopied",
                nameof(ChildProfile),
                childId,
                beforeState: null,
                afterState: new
                {
                    roadmapId = context.Roadmap.Id,
                    copied = steps.Select(step => step.StoryId).ToList(),
                    skipped = plan.Steps.Where(step => step.Action == "Skip")
                        .Select(step => new { step.StoryId, step.SkipCode }).ToList(),
                    position = position.ToString()
                },
                cancellationToken: cancellationToken);
            await _notifier.NotifyOtherSupervisorsAsync(
                childId,
                actorUserId,
                NotificationType.RoadmapStoryAdded,
                new
                {
                    childProfileId = childId,
                    roadmapId = context.Roadmap.Id,
                    roadmapName = context.Roadmap.Name,
                    count = steps.Count
                },
                cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

        plan.Outcome = "Copied";
        return plan;
    }

    private async Task<bool> CanAssignAsync(int childId, int actorUserId, CancellationToken cancellationToken)
    {
        try
        {
            await _guard.EnsureOwnerAsync(childId, actorUserId, cancellationToken);
            return true;
        }
        catch (ForbiddenException)
        {
            return false;
        }
    }

    private static RoadmapChildPlanDto SkippedChild(int childId, string code) => new()
    {
        ChildProfileId = childId,
        Outcome = "Skipped",
        SkipCode = code,
        Message = MessageFor(code)
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
        ChildRoadmapSkipCodes.BlockedTopic => "Truyện thuộc chủ đề đã bị chặn với bé này.",
        ChildRoadmapSkipCodes.WrongAge => "Truyện không đúng độ tuổi của bé này.",
        ChildRoadmapSkipCodes.TooLong => "Truyện dài hơn mức cho phép của bé này.",
        ChildRoadmapSkipCodes.StoryUnavailable => "Truyện đã bị gỡ khỏi thư viện hoặc không còn đọc được.",
        ChildRoadmapSkipCodes.AlreadyInRoadmap => "Truyện đã có trong lộ trình của con.",
        _ => "Không chép được truyện này cho bé."
    };
}
