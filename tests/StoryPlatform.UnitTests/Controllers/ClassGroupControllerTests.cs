using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class ClassGroupControllerTests
{
    private readonly Mock<IClassGroupService> _service = new();
    private readonly ClassGroupController _sut;

    public ClassGroupControllerTests()
    {
        _sut = new ClassGroupController(_service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "2")], "TestAuth"))
                }
            }
        };
    }

    [Fact]
    public async Task BulkEnroll_ValidFile_DelegatesFileBytesToService()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "Nickname,AgeBand,Language,InviteeEmail\nBé An,Age_6_8,vi,");
        var formFile = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "students.csv");
        var expected = new BulkEnrollResultDto { TotalRows = 1, SuccessCount = 1 };
        _service.Setup(service => service.BulkEnrollAsync(
                1, 2, It.Is<byte[]>(value => value.SequenceEqual(bytes)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.BulkEnroll(1, formFile, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var apiResponse = Assert.IsType<ApiResponse<BulkEnrollResultDto>>(result.Value);
        Assert.Same(expected, apiResponse.Data);
    }

    [Fact]
    public async Task BulkEnroll_NoFile_ReturnsBadRequestWithoutCallingService()
    {
        var response = await _sut.BulkEnroll(1, null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
        _service.Verify(service => service.BulkEnrollAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
