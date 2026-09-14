using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ContentCategories.DTOs;
using StoryPlatform.Application.Features.ContentCategories.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Controllers;

public class ContentCategoryControllerTests
{
    private readonly Mock<IContentCategoryService> _service = new();
    private readonly ContentCategoryController _sut;

    public ContentCategoryControllerTests()
    {
        _sut = new ContentCategoryController(_service.Object)
        {
            ControllerContext = ControllerContextForUser(9)
        };
    }

    [Fact]
    public async Task CreateContentCategory_Returns201_WithCurrentUserId()
    {
        var request = new CreateContentCategoryRequestDto { Code = "animals", DisplayName = "Động vật" };
        var expected = new ContentCategoryDto { Id = 1, Code = "animals" };
        _service.Setup(service => service.CreateAsync(9, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.CreateContentCategory(request, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
        Assert.Same(expected, Assert.IsType<ApiResponse<ContentCategoryDto>>(result.Value).Data);
    }

    [Fact]
    public async Task UpdateContentCategory_DelegatesToService()
    {
        var request = new UpdateContentCategoryRequestDto { DisplayName = "Động vật" };
        var expected = new ContentCategoryDto { Id = 1, Code = "animals" };
        _service.Setup(service => service.UpdateAsync(1, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.UpdateContentCategory(1, request, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<ContentCategoryDto>>(result.Value).Data);
    }

    [Fact]
    public async Task DeleteContentCategory_DelegatesToService()
    {
        var response = await _sut.DeleteContentCategory(1, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
        _service.Verify(service => service.DeleteAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetContentCategory_ReturnsServiceResult()
    {
        var expected = new ContentCategoryDto { Id = 1, Code = "animals" };
        _service.Setup(service => service.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var response = await _sut.GetContentCategory(1, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<ContentCategoryDto>>(result.Value).Data);
    }

    [Fact]
    public async Task ListContentCategories_ReturnsServiceResult()
    {
        var expected = new List<ContentCategoryDto> { new() { Id = 1, Code = "animals" } };
        _service.Setup(service => service.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var response = await _sut.ListContentCategories(CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(expected, Assert.IsType<ApiResponse<List<ContentCategoryDto>>>(result.Value).Data);
    }

    private static ControllerContext ControllerContextForUser(int userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "TestAuth"))
        }
    };
}
