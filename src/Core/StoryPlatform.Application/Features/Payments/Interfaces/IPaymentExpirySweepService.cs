namespace StoryPlatform.Application.Features.Payments.Interfaces;

/// <summary>
/// Chuyển các payment_transactions đang Pending đã vượt ExpiresAt sang Expired (Bước 5.6.3).
/// </summary>
public interface IPaymentExpirySweepService
{
    Task<int> SweepExpiredAsync(CancellationToken cancellationToken = default);
}
