using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ParentalGate.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class ParentalGateControllerTests
{
    private readonly Mock<IParentalGateService> _service = new();
    private readonly ParentalGateController _sut;

    public ParentalGateControllerTests()
    {
        _sut = new ParentalGateController(_service.Object);
    }

    [Fact]
    public async Task Verify_DelegatesRequestToService()
    {
        var request = new VerifyParentalGateRequestDto
        {
            Email = "parent@example.com",
            Password = "correct-pass"
        };
        _service.Setup(service => service.VerifyAsync(request, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await _sut.Verify(request, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<object?>>(result.Value);
        Assert.True(apiResponse.Success);
        _service.Verify(
            service => service.VerifyAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }
}
