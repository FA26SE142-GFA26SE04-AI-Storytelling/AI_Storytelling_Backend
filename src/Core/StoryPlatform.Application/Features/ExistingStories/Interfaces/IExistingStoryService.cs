using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Features.ExistingStories.DTOs;

namespace StoryPlatform.Application.Features.ExistingStories.Interfaces;

/// <summary>
/// Service trung tâm cho luồng Existing Story (Luồng 2).
/// Gồm Import, Evaluate (qua <see cref="IExistingStoryEvaluationService"/>),
/// AI Adapt, Manual Edit, Keep Original và Archive.
///
/// Lưu ý:
///   - Không bao giờ overwrite StoryVersion cũ.
///   - Mọi mutation đều chạy trong transaction với lock theo storyId.
/// </summary>
public interface IExistingStoryService
{
    /// <summary>
    /// Phase 1: Import nội dung truyện thô từ Parent/Teacher.
    /// Tạo Story + StoryVersion v1 (EditType = Initial).
    /// </summary>
    Task<ImportStoryResponseDto> ImportAsync(
        int userId,
        ImportStoryRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ImportStoryResponseDto> ImportDocumentAsync(
        int userId,
        ImportStoryDocumentRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 3: Gọi AI để sinh version mới (AiRefined) từ base version.
    /// Không xoá base version. Trả về version mới hoặc throw nếu base không còn current.
    /// </summary>
    Task<VersionMutationResponseDto> AdaptAsync(
        int userId,
        AdaptExistingStoryRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 3: Supervisor sửa trực tiếp -> tạo version mới (HumanEdited).
    /// </summary>
    Task<VersionMutationResponseDto> UpdateContentAsync(
        int userId,
        ManualEditRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 3: Supervisor giữ nguyên version gốc với lý do override.
    /// Bắt buộc có override reason. Không được phép nếu decision = Blocked.
    /// </summary>
    Task<VersionMutationResponseDto> KeepOriginalAsync(
        int userId,
        KeepOriginalRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 3: Archive story (Reject hoặc huỷ).
    /// </summary>
    Task<bool> ArchiveAsync(
        int userId,
        ArchiveExistingStoryRequestDto request,
        CancellationToken cancellationToken = default);
}
