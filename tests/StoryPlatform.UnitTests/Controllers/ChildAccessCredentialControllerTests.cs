using System.Net;
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
                    Connection = { RemoteIpAddress = IPAddress.Parse("203.0.113.7") },
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

    [Fact]
    public async Task CreateOrRegenerateEasyLogin_DelegatesWithCurrentUserId()
    {
        var expected = new EasyLoginSecretDto
        {
            Secret = "abc123",
            CreatedAt = DateTime.UtcNow
        };
        _service.Setup(s => s.CreateOrRegenerateEasyLoginAsync(7, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.CreateOrRegenerateEasyLogin(7, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<EasyLoginSecretDto>>(result.Value);
        Assert.Same(expected, apiResponse.Data);
    }

    [Fact]
    public async Task LoginWithEasyLogin_PassesSecretAndRemoteIpAsClientKey()
    {
        var expected = new ChildSessionDto
        {
            ChildProfileId = 7,
            AvatarId = "fox",
            AccessToken = "jwt",
            ExpiresInSeconds = 14400
        };
        _service.Setup(s => s.LoginWithEasyLoginAsync(
                "qr-secret", "203.0.113.7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.LoginWithEasyLogin(
            new LoginWithEasyLoginRequestDto { Secret = "qr-secret" }, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<ChildSessionDto>>(result.Value);
        Assert.Same(expected, apiResponse.Data);
    }

    [Theory]
    [InlineData("SetPin")]
    [InlineData("LoginWithPin")]
    public void PinEndpoints_AreRemoved(string methodName)
    {
        Assert.Null(typeof(ChildAccessCredentialController).GetMethod(methodName));
    }

    [Fact]
    public void LoginWithEasyLogin_IsAnonymous()
    {
        var method = typeof(ChildAccessCredentialController)
            .GetMethod(nameof(ChildAccessCredentialController.LoginWithEasyLogin))!;

        Assert.NotEmpty(method.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false));
    }

    [Fact]
    public async Task StartSupervisedSession_DelegatesWithCurrentUserIdAndRefreshToken()
    {
        var expected = new ChildSessionDto { ChildProfileId = 5, AccessToken = "child-jwt" };
        _service.Setup(service => service.StartSupervisedSessionAsync(
                5, 42, "supervisor-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.StartSupervisedSession(
            5, new StartSupervisedChildSessionRequestDto
            {
                SupervisorRefreshToken = "supervisor-refresh"
            }, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<ChildSessionDto>>(result.Value);
        Assert.Same(expected, apiResponse.Data);
    }

    [Fact]
    public void StartSupervisedSession_OnlyParent()
    {
        var method = typeof(ChildAccessCredentialController)
            .GetMethod(nameof(ChildAccessCredentialController.StartSupervisedSession))!;
        var authorize = Assert.Single(method.GetCustomAttributes(
            typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());

        Assert.Equal("Parent", authorize.Roles);
    }
}
