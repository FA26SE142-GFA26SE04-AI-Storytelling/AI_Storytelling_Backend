using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Payments.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Payments;

public class PaymentExpirySweepServiceTests
{
    private readonly Mock<IGenericRepository<PaymentTransaction>> _transactionRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly PaymentExpirySweepService _sut;

    public PaymentExpirySweepServiceTests()
    {
        _unitOfWork.Setup(work => work.Repository<PaymentTransaction>()).Returns(_transactionRepository.Object);
        _sut = new PaymentExpirySweepService(_unitOfWork.Object);
    }

    [Fact]
    public async Task SweepExpiredAsync_NoExpiredPending_ReturnsZeroAndDoesNotSave()
    {
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PaymentTransaction>());

        var count = await _sut.SweepExpiredAsync();

        Assert.Equal(0, count);
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SweepExpiredAsync_ExpiredPendingTransactions_MarksExpiredAndSaves()
    {
        var expired1 = new PaymentTransaction { Id = 1, Status = PaymentStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        var expired2 = new PaymentTransaction { Id = 2, Status = PaymentStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(-5) };
        _transactionRepository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<PaymentTransaction, bool>>>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expired1, expired2 });

        var count = await _sut.SweepExpiredAsync();

        Assert.Equal(2, count);
        Assert.Equal(PaymentStatus.Expired, expired1.Status);
        Assert.Equal(PaymentStatus.Expired, expired2.Status);
        _transactionRepository.Verify(repo => repo.Update(It.IsAny<PaymentTransaction>()), Times.Exactly(2));
        _unitOfWork.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
