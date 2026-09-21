using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Features.StoryReview.DTOs;

namespace StoryPlatform.Application.Features.StoryReview.Interfaces;

/// <summary>
/// Quản lý việc sinh và đồng bộ hóa các Learning Artifacts (Vocabulary, Quiz, Discussion)
/// thuộc trách nhiệm Phase 4 của chuẩn kiến trúc 5 Phase.
/// Đảm bảo version-binding chính xác vào StoryVersion hiện tại và hỗ trợ sinh độc lập từng artifact.
/// </summary>
public interface ILearningArtifactService
{
    Task<ArtifactGenerationResultDto> GenerateArtifactsAsync(
        int userId,
        int storyId,
        GenerateArtifactsRequestDto? request = null,
        CancellationToken cancellationToken = default);

    Task<int> GenerateVocabularyAsync(
        int userId,
        int storyId,
        int? storyVersionId = null,
        CancellationToken cancellationToken = default);

    Task<int> GenerateQuizAsync(
        int userId,
        int storyId,
        int? storyVersionId = null,
        CancellationToken cancellationToken = default);

    Task<int> GenerateDiscussionAsync(
        int userId,
        int storyId,
        int? storyVersionId = null,
        CancellationToken cancellationToken = default);
}
