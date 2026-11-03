using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions.Services;

public class ChildNotebookService : IChildNotebookService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IChildRewardService _rewards;

    public ChildNotebookService(IUnitOfWork unitOfWork, IChildRewardService rewards)
    {
        _unitOfWork = unitOfWork;
        _rewards = rewards;
    }

    public async Task<VocabularyDefinitionDto> GetDefinitionAsync(
        int childProfileId, int sessionId, int vocabularyId, CancellationToken ct = default)
    {
        var (session, word) = await RequireWordAsync(childProfileId, sessionId, vocabularyId, ct);
        var collected = await _unitOfWork.Repository<VocabularyNotebookEntry>().ExistsAsync(
            entry => entry.ChildProfileId == childProfileId && entry.StoryVocabularyId == vocabularyId
                     && entry.RemovedAt == null, ct);
        var audio = await VocabularyAudioLookup.FindUrlsAsync(_unitOfWork, new[] { word.Id }, ct);

        await _unitOfWork.Repository<TelemetryLog>().AddAsync(NewLog(session.Id, TelemetryEventType.VocabularyOpened, word.Id), ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return new VocabularyDefinitionDto
        {
            VocabularyId = word.Id,
            Term = word.Term,
            Definition = word.Definition,
            IsCollected = collected,
            AudioUrl = audio.GetValueOrDefault(word.Id)
        };
    }

    public async Task<NotebookEntryDto> CollectAsync(
        int childProfileId, int sessionId, int vocabularyId, CancellationToken ct = default)
    {
        var (session, word) = await RequireWordAsync(childProfileId, sessionId, vocabularyId, ct);
        var repository = _unitOfWork.Repository<VocabularyNotebookEntry>();
        // Dòng đã bỏ khỏi sổ (RemovedAt) vẫn được tìm thấy, để lưu lại chỉ hiện lại, không cộng EXP lần nữa.
        var entry = await repository.FirstOrDefaultAsync(
            existing => existing.ChildProfileId == childProfileId && existing.StoryVocabularyId == vocabularyId,
            cancellationToken: ct);
        var reward = RewardOutcome.None;
        if (entry == null)
        {
            var visibleBefore = await repository.CountAsync(
                existing => existing.ChildProfileId == childProfileId && existing.RemovedAt == null, ct);
            entry = await repository.AddAsync(new VocabularyNotebookEntry
            {
                ChildProfileId = childProfileId,
                StoryVocabularyId = vocabularyId,
                CollectedAt = DateTime.UtcNow
            }, ct);
            await _unitOfWork.Repository<TelemetryLog>().AddAsync(
                NewLog(session.Id, TelemetryEventType.VocabularyCollected, word.Id), ct);
            reward = await _rewards.OnWordCollectedAsync(session, visibleBefore + 1, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else if (entry.RemovedAt != null)
        {
            entry.RemovedAt = null;
            entry.UpdatedAt = DateTime.UtcNow;
            repository.Update(entry);
            await _unitOfWork.SaveChangesAsync(ct);
        }

        var result = (await BuildAsync(new[] { entry }, ct)).Single();
        result.ExpGained = reward.ExpGained;
        result.NewBadges = reward.NewBadgeCodes.ToList();
        return result;
    }

    public async Task<IReadOnlyList<NotebookEntryDto>> ListAsync(int childProfileId, CancellationToken ct = default)
    {
        var entries = (await _unitOfWork.Repository<VocabularyNotebookEntry>().FindAsync(
                entry => entry.ChildProfileId == childProfileId && !entry.IsDeleted && entry.RemovedAt == null,
                cancellationToken: ct))
            .OrderByDescending(entry => entry.CollectedAt).ThenByDescending(entry => entry.Id).ToList();
        return await BuildAsync(entries, ct);
    }

    public async Task RemoveAsync(int childProfileId, int entryId, CancellationToken ct = default)
    {
        var repository = _unitOfWork.Repository<VocabularyNotebookEntry>();
        var entry = await repository.GetByIdAsync(entryId, ct);
        if (entry == null || entry.IsDeleted || entry.RemovedAt != null || entry.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Từ trong Sổ tay", entryId);
        }

        // Chỉ ẩn khỏi sổ: không trừ EXP, không thu hồi huy hiệu, lưu lại thì hiện lại.
        entry.RemovedAt = DateTime.UtcNow;
        entry.UpdatedAt = entry.RemovedAt;
        repository.Update(entry);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<(ReadingSession Session, StoryVocabulary Word)> RequireWordAsync(
        int childProfileId, int sessionId, int vocabularyId, CancellationToken ct)
    {
        var session = await _unitOfWork.Repository<ReadingSession>().GetByIdAsync(sessionId, ct);
        if (session == null || session.IsDeleted || session.ChildProfileId != childProfileId)
        {
            throw new NotFoundException("Phiên đọc", sessionId);
        }

        var word = await _unitOfWork.Repository<StoryVocabulary>().GetByIdAsync(vocabularyId, ct);
        if (word == null || word.IsDeleted || word.StoryVersionId != session.StoryVersionId)
        {
            throw new NotFoundException("Từ vựng", vocabularyId);
        }

        return (session, word);
    }

    private async Task<IReadOnlyList<NotebookEntryDto>> BuildAsync(
        IReadOnlyList<VocabularyNotebookEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return new List<NotebookEntryDto>();
        }

        var wordIds = entries.Select(entry => entry.StoryVocabularyId).Distinct().ToList();
        var words = (await _unitOfWork.Repository<StoryVocabulary>().FindAsync(
            word => wordIds.Contains(word.Id), cancellationToken: ct)).ToDictionary(word => word.Id);
        var audio = await VocabularyAudioLookup.FindUrlsAsync(_unitOfWork, wordIds, ct);
        var versionIds = words.Values.Select(word => word.StoryVersionId).Distinct().ToList();
        var versions = (await _unitOfWork.Repository<StoryVersion>().FindAsync(
            version => versionIds.Contains(version.Id), cancellationToken: ct)).ToDictionary(version => version.Id);
        var scenes = await _unitOfWork.Repository<StoryScene>().FindAsync(
            scene => versionIds.Contains(scene.StoryVersionId) && !scene.IsDeleted, cancellationToken: ct);

        var result = new List<NotebookEntryDto>();
        foreach (var entry in entries)
        {
            if (!words.TryGetValue(entry.StoryVocabularyId, out var word)
                || !versions.TryGetValue(word.StoryVersionId, out var version))
            {
                continue;
            }

            var firstScene = scenes
                .Where(scene => scene.StoryVersionId == word.StoryVersionId
                                && scene.SceneText.Contains(word.Term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(scene => scene.SceneIndex).FirstOrDefault();

            result.Add(new NotebookEntryDto
            {
                EntryId = entry.Id,
                VocabularyId = word.Id,
                Term = word.Term,
                Definition = word.Definition,
                AudioUrl = audio.GetValueOrDefault(word.Id),
                StoryId = version.StoryId,
                StoryTitle = version.Title,
                SceneIndex = firstScene?.SceneIndex,
                CollectedAt = entry.CollectedAt
            });
        }

        return result;
    }

    private static TelemetryLog NewLog(int sessionId, TelemetryEventType type, int vocabularyId) => new()
    {
        ReadingSessionId = sessionId,
        EventType = type,
        EventPayload = JsonSerializer.Serialize(new { vocabularyId }),
        OccurredAt = DateTime.UtcNow
    };
}
