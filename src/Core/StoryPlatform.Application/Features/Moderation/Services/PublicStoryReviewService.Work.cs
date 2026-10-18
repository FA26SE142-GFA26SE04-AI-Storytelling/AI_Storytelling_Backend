using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public partial class PublicStoryReviewService
{
    private const int MaxFlagReasonLength = 1000;

    public async Task<PublicReviewProgressDto> MarkViewedAsync(
        int actorUserId, int itemId, MarkPartViewedRequestDto request, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var content = await LoadContentAsync(context.Version.Id, cancellationToken);

        switch (request.PartType)
        {
            case PublicReviewRequirements.ScenePart:
                if (!content.Scenes.Any(scene => scene.Id == request.PartId))
                {
                    throw new BadRequestException("Scene không thuộc phiên bản đang duyệt.");
                }
                break;

            case PublicReviewRequirements.AudioPart:
                var segment = content.Segments.FirstOrDefault(x => x.Id == request.PartId);
                if (segment == null || FindAudio(content, segment.Id) == null)
                {
                    throw new BadRequestException(
                        "Đoạn audio không tồn tại hoặc chưa có file trong phiên bản đang duyệt.");
                }
                break;

            default:
                throw new BadRequestException("PartType phải là scene hoặc audio.");
        }

        var item = context.Item;
        item.ReviewProgress = PublicReviewState.AddViewed(item.ReviewProgress, request.PartType, request.PartId);
        await SaveItemAsync(item, cancellationToken);
        return BuildProgress(item, content);
    }

    public async Task<PublicReviewFlagsResultDto> ResolveFlagAsync(
        int actorUserId, int itemId, ResolveFlagRequestDto request, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Phải ghi lý do khi đánh dấu \"Đã xem — an toàn\".");
        }
        if (reason.Length > MaxFlagReasonLength)
        {
            throw new BadRequestException($"Lý do tối đa {MaxFlagReasonLength} ký tự.");
        }

        var item = context.Item;
        var version = context.Version;
        var flags = PublicReviewState.ParseVersionFlags(version.GrayZoneFlags);
        if (!flags.Any(flag => flag.Id == request.FlagId))
        {
            throw new BadRequestException("Không có cờ này trong phiên bản đang duyệt.");
        }

        var alreadyResolved = PublicReviewState.GetResolvedFlags(item.Flags)
            .Any(x => x.FlagId == request.FlagId);
        if (!alreadyResolved)
        {
            var now = DateTime.UtcNow;
            item.Flags = PublicReviewState.AddResolvedFlag(
                item.Flags, new ResolvedFlag(request.FlagId, reason, context.Actor.Id, now));

            var remaining = BuildFlags(version, item).Any(flag => !flag.Resolved);
            if (!remaining && !version.GrayZoneCleared)
            {
                version.GrayZoneCleared = true;
                version.GrayZoneClearedByUserId = context.Actor.Id;
                version.GrayZoneClearedAt = now;
                _unitOfWork.Repository<StoryVersion>().Update(version);
            }

            await SaveItemAsync(item, cancellationToken);
            await _auditLogWriter.LogAsync(
                context.Actor.Id, "PublicStoryReviewFlagResolved", nameof(ModerationItem), item.Id,
                beforeState: null,
                afterState: new { flagId = request.FlagId, reason, storyVersionId = version.Id },
                cancellationToken: cancellationToken);
        }

        var result = BuildFlags(version, item);
        return new PublicReviewFlagsResultDto
        {
            Flags = result,
            AllFlagsResolved = result.All(flag => flag.Resolved),
            VersionCleared = version.GrayZoneCleared
        };
    }

    public async Task<PublicReviewChecklistDto> SaveChecklistAsync(
        int actorUserId, int itemId, PublicReviewChecklistDto input, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var changes = new Dictionary<string, bool>();

        void Collect(string key, bool? value)
        {
            if (value.HasValue)
            {
                changes[key] = value.Value;
            }
        }

        Collect(PublicReviewRequirements.Safety, input.Safety);
        Collect(PublicReviewRequirements.AgeFit, input.AgeFit);
        Collect(PublicReviewRequirements.Education, input.Education);
        Collect(PublicReviewRequirements.Language, input.Language);
        Collect(PublicReviewRequirements.Copyright, input.Copyright);
        Collect(PublicReviewRequirements.Image, input.Image);
        Collect(PublicReviewRequirements.Audio, input.Audio);
        Collect(PublicReviewRequirements.Interaction, input.Interaction);

        if (changes.Count == 0)
        {
            throw new BadRequestException("Phải chấm ít nhất một tiêu chí.");
        }

        var item = context.Item;
        var before = BuildChecklist(item);
        item.Checklist = PublicReviewState.MergeChecklist(item.Checklist, changes);
        await SaveItemAsync(item, cancellationToken);

        var after = BuildChecklist(item);
        await _auditLogWriter.LogAsync(
            context.Actor.Id, "PublicStoryReviewChecklistSaved", nameof(ModerationItem), item.Id,
            beforeState: before, afterState: after, cancellationToken: cancellationToken);
        return after;
    }

    public async Task<List<PublicReviewLabelDto>> SetContentLabelsAsync(
        int actorUserId, int itemId, SetContentLabelsRequestDto request, CancellationToken cancellationToken = default)
    {
        var context = await LoadClaimedContextAsync(actorUserId, itemId, cancellationToken);
        var wanted = request.ContentCategoryIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            throw new BadRequestException("Truyện phải có ít nhất một nhãn nội dung.");
        }

        var categories = await _unitOfWork.Repository<ContentCategory>().FindAsync(
            x => wanted.Contains(x.Id), cancellationToken: cancellationToken);
        var invalid = wanted.Where(id => !categories.Any(c => c.Id == id && c.IsActive)).ToList();
        if (invalid.Count > 0)
        {
            throw new BadRequestException(
                $"Nhãn không tồn tại hoặc không còn hoạt động: {string.Join(", ", invalid)}.");
        }

        var before = await LoadLabelsAsync(context.Story.Id, cancellationToken);
        var repository = _unitOfWork.Repository<StoryCategory>();
        var existing = await repository.FindAsync(
            x => x.StoryId == context.Story.Id, cancellationToken: cancellationToken);

        foreach (var link in existing.Where(link => !wanted.Contains(link.ContentCategoryId)))
        {
            repository.Delete(link);
        }
        foreach (var categoryId in wanted.Where(id => !existing.Any(link => link.ContentCategoryId == id)))
        {
            await repository.AddAsync(new StoryCategory
            {
                StoryId = context.Story.Id,
                ContentCategoryId = categoryId,
                Source = TagSource.Moderator
            }, cancellationToken);
        }

        await SaveItemAsync(context.Item, cancellationToken);

        var after = await LoadLabelsAsync(context.Story.Id, cancellationToken);
        await _auditLogWriter.LogAsync(
            context.Actor.Id, "PublicStoryReviewLabelsChanged", nameof(ModerationItem), context.Item.Id,
            beforeState: new { labels = before.Select(x => x.ContentCategoryId) },
            afterState: new { labels = after.Select(x => x.ContentCategoryId) },
            cancellationToken: cancellationToken);
        return after;
    }

    private async Task SaveItemAsync(ModerationItem item, CancellationToken cancellationToken)
    {
        item.RowVersion += 1;
        _unitOfWork.Repository<ModerationItem>().Update(item);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
