using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Notifications.DTOs;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class NotificationControllerTests
{
    private readonly Mock<INotificationService> _service = new();
    private readonly NotificationController _sut;

    public NotificationControllerTests()
    {
        _sut = new NotificationController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task ListNotifications_DelegatesWithCurrentUser()
    {
        var expected = new List<NotificationDto> { new() { Id = 1 } };
        _service.Setup(service => service.ListAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var response = await _sut.ListNotifications(CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<List<NotificationDto>>>(result.Value).Data);
    }

    [Fact]
    public async Task MarkAsRead_DelegatesWithCurrentUser()
    {
        var response = await _sut.MarkAsRead(5, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        _service.Verify(service => service.MarkAsReadAsync(
            5, 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteNotification_DelegatesWithCurrentUser()
    {
        var response = await _sut.DeleteNotification(5, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        _service.Verify(service => service.DeleteAsync(
            5, 7, It.IsAny<CancellationToken>()), Times.Once);
    }
}
