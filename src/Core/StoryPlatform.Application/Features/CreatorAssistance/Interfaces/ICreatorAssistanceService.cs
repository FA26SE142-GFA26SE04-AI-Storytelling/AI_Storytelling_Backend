using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAssistance.DTOs;
using StoryPlatform.Contracts.AI.CreatorAssistance;

namespace StoryPlatform.Application.Features.CreatorAssistance.Interfaces;

public interface ICreatorAssistanceAiClient
{
    Task<AssistanceAiResult> GenerateAsync(AssistanceAiRequest request, CancellationToken ct = default);
}
public interface ICreatorAssistanceService
{
    Task<AssistanceTaskDto> CreateAsync(int userId, int storyId, CreateAssistanceTaskRequest request, CancellationToken ct = default);
    Task<AssistanceTaskDto> GetAsync(int userId, int storyId, int taskId, CancellationToken ct = default);
    Task<PagedResult<AssistanceTaskSummary>> ListAsync(int userId, int storyId, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<PagedResult<AssistanceStepDto>> StepsAsync(int userId, int storyId, int taskId, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<AssistanceCommandResult> SuggestAsync(int userId, int storyId, int taskId, SuggestAssistanceRequest request, CancellationToken ct = default);
    Task<AssistanceCommandResult> AnswerAsync(int userId, int storyId, int taskId, AnswerAssistanceRequest request, CancellationToken ct = default);
    Task<AssistanceCommandResult> DirectionAsync(int userId, int storyId, int taskId, ConfirmAssistanceDirectionRequest request, CancellationToken ct = default);
    Task<AssistanceCommandResult> TrialAsync(int userId, int storyId, int taskId, CreateAssistanceTrialRequest request, CancellationToken ct = default);
    Task<AssistanceCommandResult> CancelAsync(int userId, int storyId, int taskId, CancelAssistanceRequest request, CancellationToken ct = default);
    Task<AssistanceCommandResult> CloseAsync(int userId, int storyId, int taskId, AssistanceCommand request, CancellationToken ct = default);
}
