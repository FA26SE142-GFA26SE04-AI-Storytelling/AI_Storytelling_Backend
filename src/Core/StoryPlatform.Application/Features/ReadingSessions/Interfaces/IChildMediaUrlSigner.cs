namespace StoryPlatform.Application.Features.ReadingSessions.Interfaces;

/// <summary>
/// Đổi đường dẫn media trong kho (ví dụ "908/versions/911/v1/scene-0-….png") thành link có ký để trình duyệt của bé mở được,
/// giống cách gói media của phụ huynh ký link khi xem trước.
/// </summary>
public interface IChildMediaUrlSigner
{
    /// <summary>
    /// Ký một lô đường dẫn (đường dẫn trùng chỉ ký một lần). Trả hàm tra cứu: đường dẫn → link đã ký;
    /// link https đầy đủ giữ nguyên; đường dẫn không ký được → null để giao diện dùng ảnh mặc định.
    /// </summary>
    Task<Func<string?, string?>> SignAsync(IEnumerable<string?> paths, CancellationToken cancellationToken = default);
}
