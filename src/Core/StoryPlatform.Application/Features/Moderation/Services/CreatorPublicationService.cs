using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public sealed class CreatorPublicationService(IUnitOfWork uow, IModerationQueueService queue, IAuditLogWriter audit) : ICreatorPublicationService
{
    public async Task<CreatorPublicationStatus> GetStatusAsync(int userId, int storyId, CancellationToken ct = default)
    {
        var story = await AuthorizeCreator(userId, storyId, ct);
        var version = await Current(storyId, ct);
        var open = (await uow.Repository<ModerationItem>().FindAsync(x => x.StoryId == storyId && !x.IsDeleted &&
            (x.ItemType == ModerationItemType.PublicStoryReview || x.ItemType == ModerationItemType.PublicStoryRemoval) &&
            (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), cancellationToken: ct)).OrderByDescending(x => x.Id).FirstOrDefault();
        return new(storyId, version.Id, story.IsPublished, version.Moderation.ReviewStatus.ToString(), open?.Id, open?.ItemType.ToString(), open?.Status.ToString());
    }

    public Task<CreatorPublicationResult> RequestRemovalAsync(int userId, int storyId, CreatorRemovalRequest input, CancellationToken ct = default) =>
        Locked(storyId, async () =>
        {
            var story = await AuthorizeCreator(userId, storyId, ct);
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000) throw new BadRequestException("REMOVAL_REASON_REQUIRED");
            if (!story.IsPublished) throw new ConflictException("STORY_NOT_PUBLISHED");
            await EnsureNoOpenReview(storyId, ct);
            var version = await Current(storyId, ct);
            if (version.Moderation.ReviewStatus != VersionReviewStatus.Approved) throw new ConflictException("PUBLISHED_VERSION_REQUIRED");
            var item = await queue.EnqueueAsync(new EnqueueModerationItemCommand
            { ItemType = ModerationItemType.PublicStoryRemoval, StoryId = storyId, StoryVersionId = version.Id, CreatedByUserId = userId }, ct);
            item.Flags = JsonSerializer.Serialize(new { requestReason = input.Reason.Trim() });
            uow.Repository<ModerationItem>().Update(item);
            await audit.LogAsync(userId, "CreatorRemovalRequested", nameof(ModerationItem), item.Id, null,
                new { storyId, versionId = version.Id, reason = input.Reason.Trim() }, ct);
            return Result(story, version.Id, item.Id, "Pending");
        }, ct);

    public async Task<CreatorPublicationResult> DecideRemovalAsync(int userId, int itemId, RemovalDecisionRequest input, CancellationToken ct = default)
    {
        var initial = await uow.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken: ct)
            ?? throw new NotFoundException("ModerationItem", itemId);
        if (initial.ItemType != ModerationItemType.PublicStoryRemoval || !initial.StoryId.HasValue)
            throw new BadRequestException("NOT_A_REMOVAL_REQUEST");
        // Enforce staff access, claimed ownership and the queue's claim expiry/administrator fallback rules.
        await queue.OpenAsync(userId, itemId, ct);
        return await Locked(initial.StoryId.Value, async () =>
        {
            var item = await uow.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken: ct)
                ?? throw new NotFoundException("ModerationItem", itemId);
            var actor = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
            if (actor == null || actor.IsDeleted || actor.Status != AccountStatus.Active || actor.LockedUntil > DateTime.UtcNow ||
                actor.Role is not (UserRole.Moderator or UserRole.Administrator) || item.CreatedByUserId == userId)
                throw new ForbiddenException();
            if (item.Status != ModerationItemStatus.Claimed || item.ClaimedByUserId != userId ||
                item.ClaimedAt == null || item.ClaimedAt.Value.Add(ModerationQueuePolicy.ClaimTimeout) <= DateTime.UtcNow ||
                item.RowVersion != input.RowVersion) throw new ConflictException("STALE_REMOVAL_DECISION");
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000) throw new BadRequestException("DECISION_REASON_REQUIRED");
            var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == item.StoryId, cancellationToken: ct)
                ?? throw new NotFoundException("Story");
            var version = await Current(story.Id, ct);
            if (!story.IsPublished || version.Id != item.StoryVersionId) throw new ConflictException("STALE_PUBLISHED_VERSION");
            item.Status = input.Approve ? ModerationItemStatus.Approved : ModerationItemStatus.Rejected;
            item.DecidedByUserId = userId; item.DecidedAt = DateTime.UtcNow;
            item.DecisionReason = input.Reason.Trim(); item.RowVersion++;
            if (input.Approve)
            {
                story.IsPublished = false;
                story.ChildVisibleAt = null;
                story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");
                item.RemovedAt = DateTime.UtcNow;
                uow.Repository<Story>().Update(story);
            }
            uow.Repository<ModerationItem>().Update(item);
            await audit.LogAsync(userId, input.Approve ? "CreatorRemovalApproved" : "CreatorRemovalRejected", nameof(ModerationItem), item.Id,
                null, new { storyId = story.Id, versionId = version.Id, reason = item.DecisionReason }, ct);
            return Result(story, version.Id, item.Id, item.Status.ToString());
        }, ct);
    }

    public Task<CreatorPublicationResult> CreateRevisionAsync(int userId, int storyId, CreatorRevisionRequest input, CancellationToken ct = default) =>
        Locked(storyId, async () =>
        {
            var story = await AuthorizeCreator(userId, storyId, ct);
            if (story.IsPublished || story.Status == StoryStatus.Archived || story.ArchivedAt != null)
                throw new ConflictException("MODERATOR_REMOVAL_REQUIRED");
            await EnsureNoOpenReview(storyId, ct);
            var source = await Current(storyId, ct);
            if (source.Id != input.SourceVersionId) throw new ConflictException("STALE_STORY_VERSION");
            if (source.Moderation.ReviewStatus == VersionReviewStatus.Approved && !await uow.Repository<ModerationItem>().ExistsAsync(x =>
                x.ItemType == ModerationItemType.PublicStoryRemoval && x.StoryId == storyId && x.StoryVersionId == source.Id &&
                x.Status == ModerationItemStatus.Approved && x.RemovedAt != null, ct))
                throw new ConflictException("MODERATOR_REMOVAL_REQUIRED");
            var submitted = await uow.Repository<ModerationItem>().ExistsAsync(x =>
                x.ItemType == ModerationItemType.PublicStoryReview && x.StoryVersionId == source.Id, ct);
            if (!submitted && source.Moderation.ReviewStatus == VersionReviewStatus.None && story.Status != StoryStatus.Ready)
                throw new ConflictException("REVISION_NOT_REQUIRED");
            var versions = await uow.Repository<StoryVersion>().FindAsync(x => x.StoryId == storyId, cancellationToken: ct);
            var revision = new StoryVersion
            {
                StoryId = storyId,
                VersionNo = versions.Max(x => x.VersionNo) + 1,
                BaseVersionId = source.Id,
                EditorUserId = userId,
                EditType = VersionEditType.HumanEdited,
                IsCurrent = true,
                Title = source.Title,
                Content = source.Content,
                Lesson = source.Lesson,
                OutlineOpening = source.OutlineOpening,
                OutlineDevelopment = source.OutlineDevelopment,
                OutlineEnding = source.OutlineEnding,
                ReadabilityFkgl = source.ReadabilityFkgl,
                ReadabilityFre = source.ReadabilityFre,
                SafetyScore = source.SafetyScore,
                OutlineApprovedAt = source.OutlineApprovedAt,
                OutlineApprovedByUserId = source.OutlineApprovedByUserId,
                AudienceRevision = story.AudienceRevision
,
                AuthoringMetadata = new StoryVersionAuthoringMetadata
                {
                    AuthoringOrigin = source.AuthoringMetadata.AuthoringOrigin,
                    AssistanceProvenanceJson = JsonSerializer.Serialize(new StoryPlatform.Application.Features.CreatorAssistance.DTOs.AssistanceProvenance(1, [], source.Id, null), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    ImportFileName = source.AuthoringMetadata.ImportFileName,
                    ImportSourceHash = source.AuthoringMetadata.ImportSourceHash,
                    SourceDeclaration = source.AuthoringMetadata.SourceDeclaration,
                    RightsDeclaration = source.AuthoringMetadata.RightsDeclaration
                }
            };
            source.IsCurrent = false;
            uow.Repository<StoryVersion>().Update(source);
            await uow.SaveChangesAsync(ct); // unique current-version index
            await uow.Repository<StoryVersion>().AddAsync(revision, ct);
            await uow.SaveChangesAsync(ct);
            foreach (var x in await uow.Repository<StoryVocabulary>().FindAsync(x => x.StoryVersionId == source.Id, cancellationToken: ct))
                await uow.Repository<StoryVocabulary>().AddAsync(new() { StoryVersionId = revision.Id, Term = x.Term, Definition = x.Definition }, ct);
            foreach (var x in await uow.Repository<QuizItem>().FindAsync(x => x.StoryVersionId == source.Id, cancellationToken: ct))
                await uow.Repository<QuizItem>().AddAsync(new() { StoryVersionId = revision.Id, Type = x.Type, Question = x.Question, CorrectAnswer = x.CorrectAnswer, Choices = x.Choices }, ct);
            foreach (var x in await uow.Repository<DiscussionQuestion>().FindAsync(x => x.StoryVersionId == source.Id, cancellationToken: ct))
                await uow.Repository<DiscussionQuestion>().AddAsync(new() { StoryVersionId = revision.Id, Question = x.Question, IsMoralLesson = x.IsMoralLesson }, ct);
            // Media remain bound to the historical version; the new version must prepare its own media.
            story.Status = story.Source == StorySource.Ai ? StoryStatus.ContentReview : StoryStatus.Draft;
            story.AuthoringLastVersionNo = revision.VersionNo;
            story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");
            uow.Repository<Story>().Update(story);
            await audit.LogAsync(userId, "CreatorRevisionCreated", nameof(StoryVersion), revision.Id, null,
                new { storyId, baseVersionId = source.Id, versionId = revision.Id }, ct);
            return Result(story, revision.Id, null, story.Status.ToString());
        }, ct);

    private async Task<Story> AuthorizeCreator(int userId, int storyId, CancellationToken ct)
    {
        var user = await uow.Repository<UserAccount>().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken: ct);
        if (user == null || user.IsDeleted || user.Role != UserRole.ContentCreator || user.Status != AccountStatus.Active || user.LockedUntil > DateTime.UtcNow)
            throw new ForbiddenException();
        var story = await uow.Repository<Story>().FirstOrDefaultAsync(x => x.Id == storyId && !x.IsDeleted, cancellationToken: ct);
        if (story == null || story.Scope != StoryScope.Creator || story.AuthorUserId != userId) throw new NotFoundException("Story", storyId);
        return story;
    }
    private async Task EnsureNoOpenReview(int storyId, CancellationToken ct)
    {
        if (await uow.Repository<ModerationItem>().ExistsAsync(x => x.StoryId == storyId && !x.IsDeleted &&
            (x.ItemType == ModerationItemType.PublicStoryReview || x.ItemType == ModerationItemType.PublicStoryRemoval) &&
            (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), ct))
            throw new ConflictException("OPEN_MODERATION_REQUEST");
    }
    private async Task<StoryVersion> Current(int storyId, CancellationToken ct) =>
        await uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct)
        ?? throw new NotFoundException("StoryVersion");
    private static CreatorPublicationResult Result(Story story, int versionId, int? itemId, string status) =>
        new(story.Id, versionId, itemId, status, story.AuthoringRevisionToken);
    private async Task<T> Locked<T>(int storyId, Func<Task<T>> action, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(ct);
        try
        {
            await uow.AcquireTransactionLockAsync(storyId, ct);
            var result = await action(); await uow.SaveChangesAsync(ct); await uow.CommitTransactionAsync(ct); return result;
        }
        catch { await uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }
}
