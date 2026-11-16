using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public partial class ParentReadingFeedbackService : IParentReadingFeedbackService
{
    private const int MaxPageSize = 50;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildProfileAccessGuard _guard;

    public ParentReadingFeedbackService(IUnitOfWork unitOfWork, IChildProfileAccessGuard guard)
    {
        _unitOfWork = unitOfWork;
        _guard = guard;
    }

    public async Task<PagedResult<ParentReadingFeedbackItemDto>> ListAsync(
        int parentUserId, int childProfileId, ParentReadingFeedbackQueryDto query, CancellationToken ct = default)
    {
        await _guard.EnsureOwnerAsync(childProfileId, parentUserId, ct);
        var unfinished = ParentReadingFeedbackFilter.IsUnfinished(query.Status);
        ParentReadingFeedbackFilter.EnsureRange(query.From, query.To);

        var pageIndex = Math.Max(query.PageIndex, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        Func<IQueryable<ReadingSession>, IOrderedQueryable<ReadingSession>> orderBy = unfinished
            ? rows => rows.OrderByDescending(session => session.StartedAt).ThenByDescending(session => session.Id)
            : rows => rows.OrderByDescending(session => session.CompletedAt).ThenByDescending(session => session.Id);
        var (sessions, total) = await _unitOfWork.Repository<ReadingSession>().GetPagedAsync(
            pageIndex,
            pageSize,
            ParentReadingFeedbackFilter.Sessions(childProfileId, unfinished, query.From, query.To),
            orderBy: orderBy,
            cancellationToken: ct);

        var versionIds = sessions.Where(session => session.StoryVersionId.HasValue)
            .Select(session => session.StoryVersionId!.Value).Distinct().ToList();
        var versions = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            version => versionIds.Contains(version.Id), cancellationToken: ct)).ToDictionary(version => version.Id);
        var pageCounts = (await _unitOfWork.Repository<StoryScene>().FindAsync(
                scene => versionIds.Contains(scene.StoryVersionId) && !scene.IsDeleted, cancellationToken: ct))
            .GroupBy(scene => scene.StoryVersionId)
            .ToDictionary(group => group.Key, group => group.Count());

        var storyIds = sessions.Select(session => session.StoryId).Distinct().ToList();
        var storySessions = (await _unitOfWork.Repository<ReadingSession>().FindAsync(
                session => session.ChildProfileId == childProfileId
                           && storyIds.Contains(session.StoryId)
                           && !session.IsDeleted,
                cancellationToken: ct)).ToList();
        var childSessionIds = storySessions.Select(session => session.Id).ToList();
        var completedByStory = storySessions
            .Where(session => session.Status == SessionStatus.Completed)
            .GroupBy(session => session.StoryId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(session => session.CompletedAt).ThenBy(session => session.Id).ToList());
        var quiz = await LoadQuizAsync(versionIds, childSessionIds, ct);

        var sessionIds = sessions.Select(session => session.Id).ToList();
        var conversation = await LoadConversationAnswersAsync(sessionIds, ct);
        var activity = await LoadActivityLogsAsync(sessionIds, ct);
        var terms = await LoadTermsAsync(activity.Values
            .SelectMany(logs => ReadingActivityTelemetry.CollectedVocabularyIds(logs))
            .Distinct().ToList(), ct);
        var retells = await LoadRetellsAsync(sessionIds, ct);
        var rows = sessions.Select(session =>
        {
            var versionId = session.StoryVersionId;
            var quizCount = quiz.ItemsOf(versionId).Count;
            var runs = quiz.RunsOf(versionId);
            var completedOfStory = completedByStory.GetValueOrDefault(session.StoryId) ?? new List<ReadingSession>();
            var ordinal = completedOfStory.FindIndex(done => done.Id == session.Id);
            var logs = activity.GetValueOrDefault(session.Id) ?? new List<TelemetryLog>();
            return new ParentReadingFeedbackItemDto
            {
                SessionId = session.Id,
                StoryId = session.StoryId,
                Title = versionId.HasValue && versions.TryGetValue(versionId.Value, out var version)
                    ? version.Title
                    : string.Empty,
                CompletedAt = session.CompletedAt,
                Status = session.Status.ToString(),
                StartedAt = session.StartedAt,
                TimeSpentSeconds = session.TimeSpentSeconds,
                PagesCompleted = session.PagesCompleted,
                TotalPages = versionId.HasValue ? pageCounts.GetValueOrDefault(versionId.Value) : 0,
                ReadCount = completedOfStory.Count,
                ReadOrdinal = ordinal < 0 ? null : ordinal + 1,
                LaterCompleted = session.Status != SessionStatus.Completed
                                 && completedOfStory.Any(done => done.CompletedAt > session.StartedAt),
                ReadingMode = session.ReadingMode?.ToString(),
                Feeling = session.Feeling?.ToString(),
                FeelingLabel = session.Feeling.HasValue ? StoryFeelingLabels.Label(session.Feeling.Value) : null,
                QuizTotalQuestions = quizCount,
                QuizBestPercent = quizCount == 0 ? null : QuizRunCalculator.BestPercent(runs, quizCount),
                QuizRunCount = quizCount == 0 ? 0 : runs.Count,
                QuizLatestPercent = quizCount == 0 || runs.Count == 0
                    ? null
                    : QuizRunCalculator.Percent(runs[^1].Correct, quizCount),
                ConversationAnswers = conversation.GetValueOrDefault(session.Id) ?? new(),
                ReadAloud = ReadingActivityTelemetry.ReadAloud(logs),
                ReadAloudPractice = ReadingActivityTelemetry.ReadAloudPractice(logs),
                ExpEarned = ReadingActivityTelemetry.Exp(logs),
                WordsCollected = ReadingActivityTelemetry.CollectedVocabularyIds(logs)
                    .Where(terms.ContainsKey)
                    .Select(id => new ParentCollectedWordDto { VocabularyId = id, Term = terms[id] })
                    .ToList(),
                Retell = retells.GetValueOrDefault(session.Id)
            };
        }).ToList();

        return new PagedResult<ParentReadingFeedbackItemDto>(rows, total, pageIndex, pageSize);
    }
    // Chỉ Checkpoint cảm xúc/cảm nhận: các loại có đáp án không đưa cho phụ huynh như bài kiểm tra (spec 9).
    private async Task<Dictionary<int, List<ConversationAnswerDto>>> LoadConversationAnswersAsync(
        List<int> sessionIds, CancellationToken ct)
    {
        if (sessionIds.Count == 0) return new();
        var attempts = await _unitOfWork.Repository<ListeningInteractionAttempt>().FindAsync(
            attempt => sessionIds.Contains(attempt.ReadingSessionId) && attempt.SelectedOptionId != null && !attempt.IsDeleted,
            cancellationToken: ct);
        if (attempts.Count == 0) return new();

        var interactionIds = attempts.Select(attempt => attempt.ListeningInteractionId).Distinct().ToList();
        var interactions = (await _unitOfWork.Repository<ListeningInteraction>().FindAsync(
                interaction => interactionIds.Contains(interaction.Id) && !interaction.IsDeleted
                               && interaction.Type == ListeningInteractionType.Checkpoint
                               && (interaction.Kind == CheckpointKind.Emotion || interaction.Kind == CheckpointKind.Opinion),
                cancellationToken: ct))
            .ToDictionary(interaction => interaction.Id);
        var optionIds = attempts.Select(attempt => attempt.SelectedOptionId!.Value).Distinct().ToList();
        var options = (await _unitOfWork.Repository<ListeningInteractionOption>().FindAsync(
                option => optionIds.Contains(option.Id), cancellationToken: ct))
            .ToDictionary(option => option.Id);
        var sceneIds = interactions.Values.Where(interaction => interaction.StorySceneId.HasValue)
            .Select(interaction => interaction.StorySceneId!.Value).Distinct().ToList();
        var scenes = (await _unitOfWork.Repository<StoryScene>().FindAsync(
                scene => sceneIds.Contains(scene.Id), cancellationToken: ct))
            .ToDictionary(scene => scene.Id);

        return attempts
            .Where(attempt => interactions.ContainsKey(attempt.ListeningInteractionId)
                              && options.ContainsKey(attempt.SelectedOptionId!.Value))
            .OrderBy(attempt => attempt.AttemptedAt).ThenBy(attempt => attempt.Id)
            .GroupBy(attempt => attempt.ReadingSessionId)
            .ToDictionary(group => group.Key, group => group.Select(attempt =>
            {
                var interaction = interactions[attempt.ListeningInteractionId];
                return new ConversationAnswerDto
                {
                    Question = interaction.Prompt ?? string.Empty,
                    Kind = interaction.Kind!.Value.ToString(),
                    AnswerText = options[attempt.SelectedOptionId!.Value].Text,
                    SceneIndex = interaction.StorySceneId is int sceneId && scenes.TryGetValue(sceneId, out var scene)
                        ? scene.SceneIndex : null,
                    AnsweredAt = attempt.AttemptedAt
                };
            }).ToList());
    }
}
