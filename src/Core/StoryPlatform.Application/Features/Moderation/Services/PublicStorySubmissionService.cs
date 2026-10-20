using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.MediaGeneration.Interfaces;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Application.Features.Moderation.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Moderation.Services;

public class PublicStorySubmissionService : IPublicStorySubmissionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IModerationQueueService _queueService;
    private readonly IMediaReadinessService _mediaReadinessService;
    private readonly IAuditLogWriter _auditLogWriter;

    public PublicStorySubmissionService(
        IUnitOfWork unitOfWork,
        IModerationQueueService queueService,
        IMediaReadinessService mediaReadinessService,
        IAuditLogWriter auditLogWriter)
    {
        _unitOfWork = unitOfWork;
        _queueService = queueService;
        _mediaReadinessService = mediaReadinessService;
        _auditLogWriter = auditLogWriter;
    }

    public Task<PublicStorySubmissionResultDto> SubmitAsync(int actorUserId, SubmitPublicStoryRequestDto request, CancellationToken cancellationToken = default) =>
        WithStoryLockAsync(request.StoryId, () => SubmitLockedAsync(actorUserId, request, cancellationToken), cancellationToken);

    private async Task<PublicStorySubmissionResultDto> SubmitLockedAsync(
        int actorUserId,
        SubmitPublicStoryRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireCreatorAsync(actorUserId, cancellationToken);
        var story = await _unitOfWork.Repository<Story>().FirstOrDefaultAsync(x => x.Id == request.StoryId, cancellationToken: cancellationToken);
        if (story == null || story.Scope != StoryScope.Creator)
        {
            throw new NotFoundException("Truyện", request.StoryId);
        }
        if (story.AuthorUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được gửi duyệt truyện do chính mình tạo.");
        }
        if (story.Status == StoryStatus.Archived || story.ArchivedAt != null)
        {
            throw new BadRequestException("Truyện đã bị Archive, không thể gửi duyệt.");
        }
        if (story.Status != StoryStatus.Ready)
        {
            throw new BadRequestException(
                "Truyện chưa sẵn sàng (chưa tạo xong media) nên chưa thể gửi duyệt.");
        }

        var version = await _unitOfWork.Repository<StoryVersion>()
                          .FirstOrDefaultAsync(x => x.Id == request.StoryVersionId, cancellationToken: cancellationToken)
                      ?? throw new NotFoundException("Phiên bản truyện", request.StoryVersionId);
        if (version.StoryId != story.Id)
        {
            throw new BadRequestException("Phiên bản không thuộc truyện này.");
        }

        if (!version.IsCurrent || story.IsPublished) throw new ConflictException("CURRENT_UNPUBLISHED_VERSION_REQUIRED");
        if (await _unitOfWork.Repository<StoryGenerationJob>().ExistsAsync(x => x.StoryId == story.Id &&
            (x.Status == GenerationJobStatus.Pending || x.Status == GenerationJobStatus.Processing), cancellationToken))
            throw new ConflictException("GENERATION_JOB_ACTIVE");
        if (await _unitOfWork.Repository<ModerationItem>().ExistsAsync(x => x.ItemType == ModerationItemType.PublicStoryRemoval &&
            x.StoryId == story.Id && (x.Status == ModerationItemStatus.Pending || x.Status == ModerationItemStatus.Claimed), cancellationToken))
            throw new ConflictException("REMOVAL_REQUEST_PENDING");
        switch (version.ReviewStatus)
        {
            case VersionReviewStatus.Pending:
                throw new ConflictException("Phiên bản này đang chờ duyệt.");
            case VersionReviewStatus.Approved:
                throw new BadRequestException("Phiên bản này đã được duyệt.");
            case VersionReviewStatus.ChangesRequested:
            case VersionReviewStatus.Rejected:
                throw new BadRequestException(
                    "Phiên bản này đã bị trả về. Hãy sửa thành phiên bản mới rồi gửi lại.");
        }

        await CreatorPublicationGuard.EnsureWritableAsync(_unitOfWork, story.Id, version.Id, cancellationToken);
        var itemRepository = _unitOfWork.Repository<ModerationItem>();
        var openItems = await itemRepository.CountAsync(
            item => item.ItemType == ModerationItemType.PublicStoryReview
                    && item.StoryId == story.Id
                    && (item.Status == ModerationItemStatus.Pending
                        || item.Status == ModerationItemStatus.Claimed),
            cancellationToken);
        if (openItems > 0)
        {
            throw new ConflictException("Truyện đang có yêu cầu duyệt chưa xong.");
        }

        var readiness = await _mediaReadinessService.CheckAsync(version.Id, null, cancellationToken);
        if (!readiness.IsReady)
        {
            throw new BadRequestException("Media của phiên bản chưa hoàn tất nên chưa thể gửi duyệt.");
        }

        var returned = await FindReturnedItemAsync(story.Id, cancellationToken);
        if (returned != null)
        {
            await EnsureNotIdenticalToReturnedAsync(returned, version, cancellationToken);
        }

        var now = DateTime.UtcNow;
        ModerationItem item;
        item = await _queueService.EnqueueAsync(new EnqueueModerationItemCommand
        {
            ItemType = ModerationItemType.PublicStoryReview,
            StoryId = story.Id,
            StoryVersionId = version.Id,
            CreatedByUserId = actor.Id,
            IsResubmission = returned != null,
            PreviousItemId = returned?.Id
        }, cancellationToken);

        version.ReviewStatus = VersionReviewStatus.Pending;
        version.ReviewSubmittedAt = now;
        _unitOfWork.Repository<StoryVersion>().Update(version);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            actor.Id,
            "PublicStorySubmitted",
            nameof(ModerationItem),
            item.Id,
            beforeState: null,
            afterState: new
            {
                storyId = story.Id,
                storyVersionId = version.Id,
                isResubmission = returned != null,
                previousItemId = returned?.Id
            },
            cancellationToken);

        return ToResult(item);
    }

    public async Task<PublicStorySubmissionResultDto> WithdrawAsync(int actorUserId, int itemId, CancellationToken cancellationToken = default)
    {
        var item = await _unitOfWork.Repository<ModerationItem>().FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken: cancellationToken)
            ?? throw new NotFoundException("ModerationItem", itemId);
        if (!item.StoryId.HasValue) throw new BadRequestException("STORY_REQUIRED");
        return await WithStoryLockAsync(item.StoryId.Value, () => WithdrawLockedAsync(actorUserId, itemId, cancellationToken), cancellationToken);
    }

    private async Task<PublicStorySubmissionResultDto> WithdrawLockedAsync(
        int actorUserId,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var actor = await RequireCreatorAsync(actorUserId, cancellationToken);
        var itemRepository = _unitOfWork.Repository<ModerationItem>();
        var item = await itemRepository.FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken: cancellationToken)
                   ?? throw new NotFoundException("Mục kiểm duyệt", itemId);

        if (item.ItemType != ModerationItemType.PublicStoryReview)
        {
            throw new BadRequestException("Mục này không phải yêu cầu duyệt truyện Public Library.");
        }
        if (item.CreatedByUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn chỉ được rút yêu cầu do chính mình gửi.");
        }
        if (item.Status != ModerationItemStatus.Pending)
        {
            throw new ConflictException("Mục đã có người nhận hoặc đã xử lý xong nên không thể rút.");
        }

        item.Status = ModerationItemStatus.Withdrawn;
        item.WithdrawnAt = DateTime.UtcNow;
        item.RowVersion += 1;
        itemRepository.Update(item);

        if (item.StoryVersionId.HasValue)
        {
            var version = await _unitOfWork.Repository<StoryVersion>()
                .GetByIdAsync(item.StoryVersionId.Value, cancellationToken);
            if (version != null && version.ReviewStatus == VersionReviewStatus.Pending)
            {
                version.ReviewStatus = VersionReviewStatus.None;
                version.ReviewSubmittedAt = null;
                _unitOfWork.Repository<StoryVersion>().Update(version);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogWriter.LogAsync(
            actor.Id,
            "PublicStoryWithdrawn",
            nameof(ModerationItem),
            item.Id,
            beforeState: new { status = ModerationItemStatus.Pending.ToString() },
            afterState: new
            {
                status = ModerationItemStatus.Withdrawn.ToString(),
                storyVersionId = item.StoryVersionId
            },
            cancellationToken);

        return ToResult(item);
    }

    private async Task<T> WithStoryLockAsync<T>(int storyId, Func<Task<T>> action, CancellationToken ct)
    {
        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(storyId, ct);
            var result = await action();
            await _unitOfWork.CommitTransactionAsync(ct);
            return result;
        }
        catch { await _unitOfWork.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    private async Task<UserAccount> RequireCreatorAsync(
        int actorUserId, CancellationToken cancellationToken)
    {
        var actor = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(actorUserId, cancellationToken);
        if (actor == null
            || actor.IsDeleted
            || actor.Status != AccountStatus.Active
            || actor.Role != UserRole.ContentCreator
            || actor.LockedUntil > DateTime.UtcNow)
        {
            throw new ForbiddenException(
                "Chỉ Content Creator đang hoạt động mới gửi duyệt truyện Public Library.");
        }

        return actor;
    }

    private async Task<ModerationItem?> FindReturnedItemAsync(
        int storyId, CancellationToken cancellationToken)
    {
        var decided = await _unitOfWork.Repository<ModerationItem>().FindAsync(
            item => item.ItemType == ModerationItemType.PublicStoryReview
                    && item.StoryId == storyId
                    && item.DecidedAt != null
                    && (item.Status == ModerationItemStatus.Approved
                        || item.Status == ModerationItemStatus.ChangesRequested
                        || item.Status == ModerationItemStatus.Rejected),
            cancellationToken: cancellationToken);

        var latest = decided.OrderByDescending(item => item.DecidedAt)
            .ThenByDescending(item => item.Id).FirstOrDefault();
        return latest != null
               && latest.Status is ModerationItemStatus.ChangesRequested or ModerationItemStatus.Rejected
            ? latest
            : null;
    }

    private async Task EnsureNotIdenticalToReturnedAsync(
        ModerationItem returned,
        StoryVersion version,
        CancellationToken cancellationToken)
    {
        if (!returned.StoryVersionId.HasValue)
        {
            return;
        }

        var returnedVersion = await _unitOfWork.Repository<StoryVersion>()
            .GetByIdAsync(returned.StoryVersionId.Value, cancellationToken);
        if (returnedVersion == null)
        {
            return;
        }

        var current = await PublicVersionSnapshot.LoadAsync(_unitOfWork, version, cancellationToken);
        var before = await PublicVersionSnapshot.LoadAsync(_unitOfWork, returnedVersion, cancellationToken);
        if (current.Fingerprint() == before.Fingerprint())
        {
            throw new BadRequestException(
                "Không thể gửi lại bản y nguyên. Hãy sửa nội dung, artifact hoặc media rồi tạo phiên bản mới.");
        }
    }

    private static PublicStorySubmissionResultDto ToResult(ModerationItem item) => new()
    {
        ItemId = item.Id,
        StoryId = item.StoryId ?? 0,
        StoryVersionId = item.StoryVersionId ?? 0,
        Status = item.Status.ToString(),
        IsResubmission = item.IsResubmission,
        PreviousItemId = item.PreviousItemId
    };
}
