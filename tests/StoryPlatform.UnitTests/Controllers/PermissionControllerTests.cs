using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class PermissionControllerTests
{
    private readonly Mock<ISupervisionService> _service = new();
    private readonly PermissionController _sut;

    public PermissionControllerTests()
    {
        _sut = new PermissionController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "2") }, "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public void ListSystemPermissions_ReturnsAllEnumNames()
    {
        var response = _sut.ListSystemPermissions();

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var data = Assert.IsType<ApiResponse<List<string>>>(result.Value).Data!;
        Assert.Equal(Enum.GetValues<Permission>().Length, data.Count);
        Assert.Contains(nameof(Permission.ManageSafetySettings), data);
    }

    [Fact]
    public async Task ListRelationshipPermissions_DelegatesWithCurrentUser()
    {
        var expected = new List<string> { nameof(Permission.ViewProgress) };
        _service.Setup(service => service.ListPermissionsAsync(20, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.ListRelationshipPermissions(20, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<List<string>>>(result.Value).Data);
    }

    [Fact]
    public async Task GrantPermission_DelegatesWithCurrentUser()
    {
        var response = await _sut.GrantPermission(20, Permission.ViewProgress, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        _service.Verify(service => service.GrantPermissionAsync(
            20, 2, Permission.ViewProgress, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokePermission_DelegatesWithCurrentUser()
    {
        var response = await _sut.RevokePermission(20, Permission.ViewProgress, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        _service.Verify(service => service.RevokePermissionAsync(
            20, 2, Permission.ViewProgress, It.IsAny<CancellationToken>()), Times.Once);
    }
}
