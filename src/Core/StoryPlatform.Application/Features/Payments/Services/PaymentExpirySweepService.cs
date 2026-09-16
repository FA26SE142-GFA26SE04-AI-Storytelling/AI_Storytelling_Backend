using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.Payments.Services;

public class PaymentExpirySweepService : IPaymentExpirySweepService
{
    private readonly IUnitOfWork _unitOfWork;

    public PaymentExpirySweepService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> SweepExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _unitOfWork.Repository<PaymentTransaction>().FindAsync(
            item => item.Status == PaymentStatus.Pending && item.ExpiresAt < now,
            cancellationToken: cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var transaction in expired)
        {
            transaction.Status = PaymentStatus.Expired;
            _unitOfWork.Repository<PaymentTransaction>().Update(transaction);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
