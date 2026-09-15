using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.AccessCredentials.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class ChildAccessCredentialControllerTests
{
    private readonly Mock<IChildAccessCredentialService> _service = new();
    private readonly ChildAccessCredentialController _sut;

    public ChildAccessCredentialControllerTests()
    {
        _sut = new ChildAccessCredentialController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "42"),
                            new Claim("token_type", "child")
                        }, "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task GetMySession_DelegatesWithChildProfileIdFromToken()
    {
        var expected = new ChildSessionProfileDto
        {
            ChildProfileId = 42,
            Nickname = "Bé An",
            AgeBand = "Age_6_8"
        };
        _service.Setup(service => service.GetMySessionProfileAsync(
                42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.GetMySession(CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<ChildSessionProfileDto>>(result.Value);
        Assert.Same(expected, apiResponse.Data);
        _service.Verify(service => service.GetMySessionProfileAsync(
            42, It.IsAny<CancellationToken>()), Times.Once);
    }
}
