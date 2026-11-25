using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Events.Services;

public partial class EventParticipationService
{
    public Task<EventSubmissionDto> SubmitParentStoryAsync(int userId, int eventId, CreatorEventSubmitRequestDto request, CancellationToken ct = default) =>
        InEventResultTransactionAsync(eventId, async () =>
        {
            if (!request.ConsentToEventSharing)
                throw new BadRequestException("Cần đồng ý chia sẻ bản văn dự thi với Creator trong sự kiện.");
            await _uow.AcquireTransactionLockAsync(request.StoryId, ct);
            var campaign = await RequirePublishedCampaignAsync(eventId, ct);
            var schedule = EventVoting.CreatorSchedule(campaign)
                ?? throw new ConflictException("Sự kiện không nhận bài dự thi từ Parent.");
            var (track, participation) = await RequireVotingParticipantAsync(userId, eventId, EventTrackType.Parent, ct);
            var now = DateTime.UtcNow;
            if (now < track.ExecutionStartAt || now > schedule.SubmissionCutoffAt)
                throw new ConflictException("Ngoài thời gian Parent nộp bài dự thi.");

            var story = await _uow.Repository<Story>().GetByIdAsync(request.StoryId, ct);
            if (story == null || story.Scope != StoryScope.Parent || story.AuthorUserId != userId || story.IsDeleted)
                throw new ForbiddenException("Bạn chỉ được nộp truyện do mình tạo.");
            if (story.ChildProfileId is not int childId || !await _uow.Repository<ChildProfile>().ExistsAsync(
                child => child.Id == childId && child.OwnerUserId == userId &&
                    child.Status == ChildProfileStatus.Active && !child.IsDeleted, ct))
                throw new ForbiddenException("Hồ sơ bé của truyện không thuộc quyền quản lý hiện tại.");
            if (story.Status != StoryStatus.Ready || story.ArchivedAt.HasValue)
                throw new BadRequestException("Truyện cần ở trạng thái sẵn sàng trước khi nộp.");
            var version = await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(v => v.StoryId == story.Id && v.IsCurrent, cancellationToken: ct)
                ?? throw new BadRequestException("Truyện chưa có phiên bản nội dung.");
            if (string.IsNullOrWhiteSpace(version.Content) || version.Content.Length > 50_000)
                throw new BadRequestException("Nội dung dự thi phải có từ 1 đến 50.000 ký tự.");

            var submission = await _uow.Repository<EventSubmission>().FirstOrDefaultAsync(
                s => s.EventId == eventId && s.CreatorUserId == userId, cancellationToken: ct);
            if (submission is { Status: EventSubmissionStatus.PendingReview or EventSubmissionStatus.Approved })
                throw new ConflictException("Bài dự thi đang được duyệt hoặc đã được chốt.");
            if (submission != null && submission.StoryId != story.Id)
                throw new ConflictException("Mỗi tài khoản chỉ được nộp một truyện.");

            var isNew = submission == null;
            submission ??= new EventSubmission
            {
                CreatorUserId = userId, EventId = eventId, ParticipationId = participation.Id, StoryId = story.Id, CreatedAt = now
            };
            submission.CurrentStoryVersionId = version.Id;
            submission.ParentSubmissionTitle = string.IsNullOrWhiteSpace(version.Title) ? story.Title : version.Title;
            submission.ParentSubmissionText = version.Content;
            submission.Status = EventSubmissionStatus.PendingReview;
            submission.SubmittedAt = now;
            submission.UpdatedAt = now;
            if (isNew) await _uow.Repository<EventSubmission>().AddAsync(submission, ct);
            else _uow.Repository<EventSubmission>().Update(submission);
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(userId, "ParentEventStorySubmitted", nameof(EventSubmission), submission.Id, null, new { eventId }, ct);
            return MapSubmission(submission, Array.Empty<EventEvaluation>());
        }, ct);

    public Task<EventSubmissionDto> ReviewParentStoryAsync(int userId, int eventId, int submissionId, ReviewParentEventSubmissionDto request, CancellationToken ct = default) =>
        InEventResultTransactionAsync(eventId, async () =>
        {
            var campaign = await RequirePublishedCampaignAsync(eventId, ct);
            await EventModeratorAccess.RequireOperatorAsync(_uow, campaign, userId, ct);
            var now = DateTime.UtcNow;
            if (campaign.ConfigVersion == EventConfigVersion.ActivityBased)
            {
                var subAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                    a => a.EventId == eventId && a.ActivityType == EventActivityType.Submission, cancellationToken: ct);
                var voteAct = await _uow.Repository<EventActivity>().FirstOrDefaultAsync(
                    a => a.EventId == eventId && a.ActivityType == EventActivityType.Voting, cancellationToken: ct);
                var cutoff = subAct?.ReviewCutoffAt ?? subAct?.EndAt;
                if (subAct == null || (cutoff.HasValue && now > cutoff.Value) || (voteAct != null && now >= voteAct.StartAt))
                    throw new ConflictException("Đã hết thời gian duyệt bài dự thi của Parent.");
            }
            else
            {
                var schedule = EventVoting.CreatorSchedule(campaign);
                if (schedule == null || now > schedule.ReviewCutoffAt || now >= schedule.VotingStartAt)
                    throw new ConflictException("Đã hết thời gian duyệt bài dự thi của Parent.");
            }
            var submission = await _uow.Repository<EventSubmission>().GetByIdAsync(submissionId, ct);
            if (submission == null || submission.EventId != eventId || submission.ParentSubmissionText == null)
                throw new NotFoundException(nameof(EventSubmission), submissionId);
            // PostgreSQL timestamps retain microseconds, while a submit response can contain 100 ns ticks.
            if (submission.Status != EventSubmissionStatus.PendingReview || submission.CurrentStoryVersionId != request.StoryVersionId ||
                submission.SubmittedAt?.Ticks / TimeSpan.TicksPerMicrosecond != request.SubmittedAt?.Ticks / TimeSpan.TicksPerMicrosecond)
                throw new ConflictException("Bài dự thi đã thay đổi, hãy tải lại trước khi duyệt.");

            submission.Status = request.Approved ? EventSubmissionStatus.Approved : EventSubmissionStatus.ChangesRequested;
            submission.UpdatedAt = now;
            if (request.Approved)
            {
                submission.LockedStoryVersionId = submission.CurrentStoryVersionId;
                await _uow.Repository<EventStory>().AddAsync(new EventStory
                {
                    EventId = eventId, StoryId = submission.StoryId, StoryVersionId = submission.CurrentStoryVersionId,
                    EventSubmissionId = submission.Id, Source = EventStorySource.ParentSubmission, IsActive = true
                }, ct);
            }
            _uow.Repository<EventSubmission>().Update(submission);
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAsync(userId, "ParentEventStoryReviewed", nameof(EventSubmission), submissionId, null, new { request.Approved }, ct);
            return MapSubmission(submission, Array.Empty<EventEvaluation>());
        }, ct);

    private async Task<Event> RequirePublishedCampaignAsync(int eventId, CancellationToken ct)
    {
        var campaign = await _uow.Repository<Event>().GetByIdAsync(eventId, ct)
            ?? throw new NotFoundException(nameof(Event), eventId);
        if (campaign.Status != EventStatus.Published || campaign.ResultsFinalizedAt.HasValue)
            throw new ConflictException("Sự kiện hiện không mở hoạt động này.");
        return campaign;
    }

    private async Task<(EventTrack Track, EventParticipation Participation)> RequireVotingParticipantAsync(
        int userId, int eventId, EventTrackType type, CancellationToken ct)
    {
        var track = await _uow.Repository<EventTrack>().FirstOrDefaultAsync(
            t => t.EventId == eventId && t.TrackType == type, cancellationToken: ct)
            ?? throw new ForbiddenException("Bạn không thuộc nhóm được phép tham gia hoạt động.");
        var participation = await _uow.Repository<EventParticipation>().FirstOrDefaultAsync(
            p => p.EventId == eventId && p.TrackId == track.Id && p.UserId == userId && !p.IsDeleted, cancellationToken: ct);
        if (participation == null || participation.Status == EventParticipationStatus.Disqualified)
            throw new ForbiddenException("Bạn không có đăng ký hợp lệ cho hoạt động.");
        return (track, participation);
    }

    private async Task<List<EventStoryLibraryItemDto>> LoadCreatorVotingStoriesAsync(Event campaign, EventParticipation participant, CancellationToken ct)
    {
        var schedule = EventVoting.CreatorSchedule(campaign);
        var now = DateTime.UtcNow;
        if (campaign.Status != EventStatus.Published || schedule == null || now < schedule.VotingStartAt ||
            now > schedule.VotingEndAt || participant.Status == EventParticipationStatus.Disqualified)
            return [];
        var approved = await _uow.Repository<EventSubmission>().FindAsync(
            s => s.EventId == campaign.Id && s.ParentSubmissionText != null && s.Status == EventSubmissionStatus.Approved &&
                 s.LockedStoryVersionId != null && s.CreatorUserId != participant.UserId, cancellationToken: ct);
        var votes = await _uow.Repository<EventVote>().FindAsync(
            vote => vote.EventId == campaign.Id && vote.ParticipationId == participant.Id && !vote.IsDeleted && !vote.IsDisqualified,
            cancellationToken: ct);
        var byStory = votes.ToDictionary(vote => vote.StoryId);
        return approved.OrderBy(s => s.Id).Select(s => new EventStoryLibraryItemDto
        {
            StoryId = s.StoryId,
            StoryVersionId = s.LockedStoryVersionId!.Value,
            Source = EventStorySource.ParentSubmission,
            Title = s.ParentSubmissionTitle ?? "Truyện dự thi",
            VotingText = s.ParentSubmissionText,
            MyVoteStars = byStory.GetValueOrDefault(s.StoryId)?.Stars,
            MyVoteComment = byStory.GetValueOrDefault(s.StoryId)?.Comment
        }).ToList();
    }
}
