using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ChildRewardService : IChildRewardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ChildRewardService(IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<RewardOutcome> OnStoryCompletedAsync(
        ReadingSession session, bool isRetake, CancellationToken ct = default)
    {
        var childId = session.ChildProfileId;
        var (achievement, isNew) = await GetOrCreateAsync(childId, ct);

        var exp = isRetake ? 0 : RewardPolicy.StoryCompletedExp;
        achievement.ExpTotal += exp;
        ApplyStreak(achievement);

        var completedStories = await CountCompletedStoriesAsync(childId, session.StoryId, ct);
        var wanted = new List<string> { BadgeCatalog.FirstStory };
        if (completedStories >= RewardPolicy.BookwormStoryCount)
        {
            wanted.Add(BadgeCatalog.Bookworm);
        }

        if (achievement.StreakDays >= RewardPolicy.ShortStreakDays)
        {
            wanted.Add(BadgeCatalog.Streak3);
        }

        if (achievement.StreakDays >= RewardPolicy.LongStreakDays)
        {
            wanted.Add(BadgeCatalog.Streak7);
        }

        var newBadges = await AwardBadgesAsync(childId, wanted, ct);
        await SaveAchievementAsync(achievement, isNew, ct);
        await LogAsync(session.Id, exp, "StoryCompleted", newBadges, ct);
        return new RewardOutcome(exp, newBadges);
    }

    public async Task<RewardOutcome> OnQuizAnsweredAsync(
        ReadingSession session, bool firstTimeCorrect, bool perfectRunJustCompleted, CancellationToken ct = default)
    {
        if (!firstTimeCorrect && !perfectRunJustCompleted)
        {
            return RewardOutcome.None;
        }

        var childId = session.ChildProfileId;
        var (achievement, isNew) = await GetOrCreateAsync(childId, ct);
        var exp = firstTimeCorrect ? RewardPolicy.QuizItemCorrectExp : 0;
        achievement.ExpTotal += exp;

        var newBadges = perfectRunJustCompleted
            ? await AwardBadgesAsync(childId, new[] { BadgeCatalog.QuizChampion }, ct)
            : new List<string>();
        await SaveAchievementAsync(achievement, isNew, ct);
        await LogAsync(session.Id, exp, "QuizAnswer", newBadges, ct);
        return new RewardOutcome(exp, newBadges);
    }

    public async Task<RewardOutcome> OnWordCollectedAsync(
        ReadingSession session, int wordsInNotebook, CancellationToken ct = default)
    {
        var childId = session.ChildProfileId;
        var (achievement, isNew) = await GetOrCreateAsync(childId, ct);
        achievement.ExpTotal += RewardPolicy.WordCollectedExp;

        var newBadges = wordsInNotebook >= RewardPolicy.WordCollectorCount
            ? await AwardBadgesAsync(childId, new[] { BadgeCatalog.WordCollector50 }, ct)
            : new List<string>();
        await SaveAchievementAsync(achievement, isNew, ct);
        await LogAsync(session.Id, RewardPolicy.WordCollectedExp, "WordCollected", newBadges, ct);
        return new RewardOutcome(RewardPolicy.WordCollectedExp, newBadges);
    }

    public async Task<RewardOutcome> OnRoadmapFinishedAsync(ReadingSession session, CancellationToken ct = default)
    {
        var newBadges = await AwardBadgesAsync(session.ChildProfileId, new[] { BadgeCatalog.RoadmapFinisher }, ct);
        await LogAsync(session.Id, 0, "RoadmapFinished", newBadges, ct);
        return new RewardOutcome(0, newBadges);
    }

    public async Task<ChildRewardsDto> GetSummaryAsync(int childProfileId, CancellationToken ct = default)
    {
        var achievement = await _unitOfWork.Repository<Achievement>().FirstOrDefaultAsync(
            row => row.ChildProfileId == childProfileId && !row.IsDeleted, cancellationToken: ct);
        var earned = (await _unitOfWork.Repository<Badge>().FindAsync(
                badge => badge.ChildProfileId == childProfileId && !badge.IsDeleted, cancellationToken: ct))
            .GroupBy(badge => badge.BadgeCode)
            .ToDictionary(group => group.Key, group => group.Min(badge => badge.EarnedAt));

        var today = RewardPolicy.LocalToday(_clock);
        var last = achievement?.LastStreakDate;
        var alive = last.HasValue && (last.Value == today || last.Value == today.AddDays(-1));
        var tree = await FindTreeAsync(childProfileId, ct) ?? new KnowledgeTree { ChildProfileId = childProfileId };

        return new ChildRewardsDto
        {
            ExpTotal = achievement?.ExpTotal ?? 0,
            StreakDays = alive ? achievement!.StreakDays : 0,
            KnowledgeTree = ToTreeDto(tree, achievement?.ExpTotal ?? 0),
            Badges = BadgeCatalog.All.Select(info => new ChildBadgeDto
            {
                Code = info.Code,
                Name = info.Name,
                Description = info.Description,
                Earned = earned.ContainsKey(info.Code),
                EarnedAt = earned.TryGetValue(info.Code, out var at) ? at : null
            }).ToList()
        };
    }

    public async Task<WaterKnowledgeTreeResultDto> WaterKnowledgeTreeAsync(int childProfileId, CancellationToken ct = default)
    {
        var expTotal = (await _unitOfWork.Repository<Achievement>().FirstOrDefaultAsync(
            row => row.ChildProfileId == childProfileId && !row.IsDeleted, cancellationToken: ct))?.ExpTotal ?? 0;
        var existing = await FindTreeAsync(childProfileId, ct);
        var tree = existing ?? new KnowledgeTree { ChildProfileId = childProfileId };
        var previousStage = KnowledgeTreePolicy.StageOf(tree.CurrentTreeExp).Code;
        var available = Math.Max(0, expTotal - tree.ExpInvested);
        if (available == 0)
        {
            return new WaterKnowledgeTreeResultDto { PreviousStage = previousStage, Tree = ToTreeDto(tree, expTotal) };
        }

        // Cây đã ra hoa ở lại cho trẻ ngắm tới lần tưới này; giờ đưa vào vườn rồi trồng hạt mới.
        var newTreePlanted = false;
        if (tree.CurrentTreeExp >= KnowledgeTreePolicy.FlowerAt)
        {
            tree.FloweredTreeCount += 1;
            tree.CurrentTreeExp = 0;
            newTreePlanted = true;
        }

        // Ghi trên bảng riêng, không đụng dòng achievements đang được cộng EXP ở request khác.
        var poured = Math.Min(available, KnowledgeTreePolicy.FlowerAt - tree.CurrentTreeExp);
        tree.CurrentTreeExp += poured;
        tree.ExpInvested += poured;
        tree.LastWateredAt = DateTime.UtcNow;
        tree.UpdatedAt = tree.LastWateredAt;

        var repository = _unitOfWork.Repository<KnowledgeTree>();
        if (existing == null)
        {
            await repository.AddAsync(tree, ct);
        }
        else
        {
            repository.Update(tree);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return new WaterKnowledgeTreeResultDto
        {
            ExpPoured = poured,
            NewTreePlanted = newTreePlanted,
            PreviousStage = previousStage,
            Tree = ToTreeDto(tree, expTotal)
        };
    }

    private Task<KnowledgeTree?> FindTreeAsync(int childProfileId, CancellationToken ct) =>
        _unitOfWork.Repository<KnowledgeTree>().FirstOrDefaultAsync(
            row => row.ChildProfileId == childProfileId && !row.IsDeleted, cancellationToken: ct);

    private static KnowledgeTreeDto ToTreeDto(KnowledgeTree tree, int expTotal)
    {
        var stage = KnowledgeTreePolicy.StageOf(tree.CurrentTreeExp);
        return new KnowledgeTreeDto
        {
            Stage = stage.Code,
            StageName = stage.Name,
            CurrentTreeExp = tree.CurrentTreeExp,
            NextStageAt = stage.NextAt,
            FlowerAt = KnowledgeTreePolicy.FlowerAt,
            FloweredTreeCount = tree.FloweredTreeCount,
            ExpToWater = Math.Max(0, expTotal - tree.ExpInvested)
        };
    }

    private async Task<(Achievement Achievement, bool IsNew)> GetOrCreateAsync(int childId, CancellationToken ct)
    {
        var existing = await _unitOfWork.Repository<Achievement>().FirstOrDefaultAsync(
            row => row.ChildProfileId == childId && !row.IsDeleted, cancellationToken: ct);
        return existing != null
            ? (existing, false)
            : (new Achievement { ChildProfileId = childId }, true);
    }

    private async Task SaveAchievementAsync(Achievement achievement, bool isNew, CancellationToken ct)
    {
        achievement.UpdatedAt = DateTime.UtcNow;
        var repository = _unitOfWork.Repository<Achievement>();
        if (isNew)
        {
            await repository.AddAsync(achievement, ct);
        }
        else
        {
            repository.Update(achievement);
        }
    }

    private void ApplyStreak(Achievement achievement)
    {
        var today = RewardPolicy.LocalToday(_clock);
        if (achievement.LastStreakDate == today)
        {
            return;
        }

        achievement.StreakDays = achievement.LastStreakDate == today.AddDays(-1)
            ? achievement.StreakDays + 1
            : 1;
        achievement.LastStreakDate = today;
    }

    /// <summary>Số truyện khác nhau đã hoàn thành, tính cả phiên vừa hoàn thành (có thể chưa được lưu).</summary>
    private async Task<int> CountCompletedStoriesAsync(int childId, int currentStoryId, CancellationToken ct)
    {
        var sessions = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            session => session.ChildProfileId == childId
                       && session.Status == SessionStatus.Completed
                       && !session.IsDeleted,
            cancellationToken: ct);
        return sessions.Select(session => session.StoryId).Append(currentStoryId).Distinct().Count();
    }

    private async Task<List<string>> AwardBadgesAsync(int childId, IEnumerable<string> wanted, CancellationToken ct)
    {
        var owned = (await _unitOfWork.Repository<Badge>().FindAsync(
                badge => badge.ChildProfileId == childId && !badge.IsDeleted, cancellationToken: ct))
            .Select(badge => badge.BadgeCode).ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        foreach (var code in wanted)
        {
            if (!owned.Add(code))
            {
                continue;
            }

            await _unitOfWork.Repository<Badge>().AddAsync(new Badge
            {
                ChildProfileId = childId,
                BadgeCode = code,
                EarnedAt = DateTime.UtcNow
            }, ct);
            added.Add(code);
        }

        return added;
    }

    private async Task LogAsync(
        int sessionId, int exp, string reason, IReadOnlyList<string> newBadges, CancellationToken ct)
    {
        var logs = _unitOfWork.Repository<TelemetryLog>();
        if (exp > 0)
        {
            await logs.AddAsync(NewLog(sessionId, TelemetryEventType.RewardGranted, new { exp, reason }), ct);
        }

        foreach (var code in newBadges)
        {
            await logs.AddAsync(NewLog(sessionId, TelemetryEventType.BadgeUnlocked, new { badge = code }), ct);
        }
    }

    private static TelemetryLog NewLog(int sessionId, TelemetryEventType type, object payload) => new()
    {
        ReadingSessionId = sessionId,
        EventType = type,
        EventPayload = JsonSerializer.Serialize(payload),
        OccurredAt = DateTime.UtcNow
    };
}
