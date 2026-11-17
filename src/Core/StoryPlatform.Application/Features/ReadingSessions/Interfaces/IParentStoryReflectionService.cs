namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

/// <summary>Chủ hồ sơ xem cảm nhận sau khi đọc của con, nghe/xóa lời con.</summary>
public interface IParentStoryReflectionService
{
    Task<ParentStoryReflectionDto> GetAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default);
    Task DeleteNoteAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default);
    /// <summary>Xóa mọi file lời con của một hồ sơ (gọi khi xóa tài khoản); không kiểm quyền.</summary>
    Task PurgeChildAsync(int childProfileId, CancellationToken ct = default);
}
