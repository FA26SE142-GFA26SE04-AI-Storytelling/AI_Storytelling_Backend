namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

public interface IChildNotebookService
{
    Task<VocabularyDefinitionDto> GetDefinitionAsync(int childProfileId, int sessionId, int vocabularyId, CancellationToken ct = default);
    Task<NotebookEntryDto> CollectAsync(int childProfileId, int sessionId, int vocabularyId, CancellationToken ct = default);
    Task<IReadOnlyList<NotebookEntryDto>> ListAsync(int childProfileId, CancellationToken ct = default);
    Task RemoveAsync(int childProfileId, int entryId, CancellationToken ct = default);
}
