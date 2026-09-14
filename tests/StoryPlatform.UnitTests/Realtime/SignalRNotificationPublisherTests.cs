using Microsoft.AspNetCore.SignalR;
using Moq;
using StoryPlatform.Api.Hubs;
using StoryPlatform.Api.Realtime;
using StoryPlatform.Application.Features.Notifications.DTOs;
using Xunit;

namespace StoryPlatform.UnitTests.Realtime;

public class SignalRNotificationPublisherTests
{
    [Fact]
    public async Task PublishAsync_SendsReceiveNotificationToRecipientUser()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(proxy => proxy.SendCoreAsync(
                "ReceiveNotification", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(value => value.User("7")).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<NotificationHub>>();
        hubContext.Setup(value => value.Clients).Returns(clients.Object);
        var sut = new SignalRNotificationPublisher(hubContext.Object);
        var dto = new NotificationDto { Id = 1, Type = "SupervisionInvite" };

        await sut.PublishAsync(7, dto);

        clientProxy.Verify(proxy => proxy.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object?[]>(arguments => arguments.Length == 1 && ReferenceEquals(arguments[0], dto)),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
