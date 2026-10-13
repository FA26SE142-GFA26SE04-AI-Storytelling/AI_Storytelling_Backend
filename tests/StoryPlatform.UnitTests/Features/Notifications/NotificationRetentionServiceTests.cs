using System.Linq.Expressions;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.Notifications;
using StoryPlatform.Application.Features.Notifications.Services;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Notifications;

public class NotificationRetentionServiceTests
{
    private readonly Mock<IGenericRepository<Notification>> _repository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly NotificationRetentionService _sut;

    public NotificationRetentionServiceTests()
    {
        _unitOfWork.Setup(u => u.Repository<Notification>()).Returns(_repository.Object);
        _sut = new NotificationRetentionService(_unitOfWork.Object);
    }

    [Fact]
    public async Task PurgeExpiredAsync_DeletesOnlyNotificationsOlderThan90Days()
    {
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        Expression<Func<Notification, bool>>? captured = null;
        var expired = new List<Notification> { new() { Id = 1 }, new() { Id = 2 } };
        _repository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<Notification, bool>>, string?, CancellationToken>(
                (predicate, _, _) => captured = predicate)
            .ReturnsAsync(expired);

        var count = await _sut.PurgeExpiredAsync(now);

        Assert.Equal(2, count);
        _repository.Verify(r => r.DeleteRange(expired), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        var matches = captured!.Compile();
        Assert.True(matches(new Notification { CreatedAt = now.AddDays(-91) }));
        Assert.False(matches(new Notification { CreatedAt = now.AddDays(-89) }));
    }

    [Fact]
    public async Task PurgeExpiredAsync_NothingExpired_DoesNotSave()
    {
        _repository.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<Notification, bool>>>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification>());

        var count = await _sut.PurgeExpiredAsync(DateTime.UtcNow);

        Assert.Equal(0, count);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
