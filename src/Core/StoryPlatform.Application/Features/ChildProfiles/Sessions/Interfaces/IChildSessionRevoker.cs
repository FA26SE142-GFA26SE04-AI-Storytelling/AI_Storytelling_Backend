namespace StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;

/// <summary>
/// Thu hồi Child Session phía server (Mục 7c). Chỉ đánh dấu phiên đã đóng;
/// caller chịu trách nhiệm gọi SaveChangesAsync trong transaction của mình.
/// </summary>
public interface IChildSessionRevoker
{
    /// <summary>Đóng mọi phiên của một trẻ, kể cả phiên Easy Login (tạm dừng / lưu trữ hồ sơ).</summary>
    Task<int> RevokeByChildProfileAsync(
        int childProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đóng phiên trẻ vào qua ProfileSwitcher / "Cho bé đọc truyện này" của một người giám sát
    /// (phiên có SupervisorSession). childProfileId = null → mọi trẻ. Phiên Easy Login không bị ảnh hưởng.
    /// </summary>
    Task<int> RevokeBySupervisorAsync(
        int supervisorUserId, int? childProfileId, CancellationToken cancellationToken = default);
}
