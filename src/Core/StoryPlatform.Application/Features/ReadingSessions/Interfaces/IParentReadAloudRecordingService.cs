namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

/// <summary>Chủ hồ sơ nghe / xóa bản ghi đọc to của con.</summary>
public interface IParentReadAloudRecordingService
{
    Task<ParentReadAloudRecordingsDto> ListAsync(int parentUserId, int childProfileId, int sessionId, CancellationToken ct = default);
    Task DeleteAsync(int parentUserId, int childProfileId, int sessionId, int recordingId, CancellationToken ct = default);
    /// <summary>Xóa mọi file + dòng của một hồ sơ (gọi khi xóa tài khoản); không kiểm quyền.</summary>
    Task PurgeChildAsync(int childProfileId, CancellationToken ct = default);
}
