namespace StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;

/// <summary>
/// Mã hóa secret QR Easy Login để lưu DB, cho phụ huynh xem lại mã QR đang dùng của bé.
/// </summary>
public interface IEasyLoginSecretProtector
{
    string Protect(string secret);

    /// <summary>Giải mã; trả null khi không giải được (khóa đã mất, dữ liệu hỏng) thay vì ném lỗi.</summary>
    string? TryUnprotect(string protectedSecret);
}
