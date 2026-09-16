namespace StoryPlatform.Application.Abstractions.Payments;

/// <summary>
/// Dựng URL ảnh QR VietQR công khai của SePay — không cần gọi API ngoài để tạo QR.
/// </summary>
public interface ISePayQrUrlBuilder
{
    string BuildQrCodeUrl(int amount, string transferContent);
}
