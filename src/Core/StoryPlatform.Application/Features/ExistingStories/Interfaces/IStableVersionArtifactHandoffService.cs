using System.Threading;
using System.Threading.Tasks;

namespace StoryPlatform.Application.Features.ExistingStories.Interfaces;

/// <summary>
/// Entry point duy nhất để đưa một <see cref="Domain.Entities.StoryVersion"/> đã ổn định
/// vào chuỗi artifact Phase 3 (Vocabulary → Quiz → Discussion).
///
/// Hai nhánh đều dùng chung:
///   - AI Story: sau khi <c>GenerateContent</c> promote stable version.
///   - Existing Story: sau khi Import/Evaluate/Adapt có stable version.
///
/// Service này không phân biệt nguồn. Chỉ yêu cầu một stable version có Content
/// và safety đã pass.
/// </summary>
public interface IStableVersionArtifactHandoffService
{
    /// <summary>
    /// Enqueue <c>GenerateVocabulary</c> cho <paramref name="storyVersionId"/>.
    /// Idempotent: nếu job đã tồn tại cho cùng (storyId, versionId, operation),
    /// method trả về job hiện có mà không tạo trùng.
    /// </summary>
    /// <param name="storyId">ID story.</param>
    /// <param name="storyVersionId">ID StoryVersion đã ổn định (IsCurrent = true, Content != null).</param>
    /// <param name="requestedByUserId">User yêu cầu handoff.</param>
    /// <param name="generationRequestId">
    /// ID của <c>StoryGenerationRequest</c> (ảo cho Existing Story, có sẵn cho AI Story).
    /// Nếu null, handoff service tự tạo request ảo từ LearningProfile + SafetyPolicy.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>ID của StoryGenerationJob (Vocabulary hoặc job đã tồn tại).</returns>
    Task<int> QueueArtifactsAsync(
        int storyId,
        int storyVersionId,
        int requestedByUserId,
        int? generationRequestId,
        CancellationToken cancellationToken = default);
}
