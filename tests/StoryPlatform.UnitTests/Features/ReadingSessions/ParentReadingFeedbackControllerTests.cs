using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.ReadingSessions;
using StoryPlatform.Application.Features.ReadingSessions.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ParentReadingFeedbackControllerTests
{
    private readonly Mock<IParentReadingFeedbackService> _service = new(MockBehavior.Strict);

    private ParentReadingFeedbackController Controller(params Claim[] claims) => new(_service.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        }
    };

    [Fact]
    public void Controller_IsOnlyForParents_AndExposesOneAction()
    {
        var type = typeof(ParentReadingFeedbackController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("User", authorize!.Roles);
        Assert.Equal(new[] { "List" }, methods);
        Assert.Equal("{childProfileId:int}", type.GetMethod("List")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task List_ForwardsParentChildAndQuery()
    {
        using var cts = new CancellationTokenSource();
        var query = new ParentReadingFeedbackQueryDto { PageIndex = 2, PageSize = 10 };
        var expected = new PagedResult<ParentReadingFeedbackItemDto>(
            new List<ParentReadingFeedbackItemDto>(), 0, 2, 10);
        _service.Setup(s => s.ListAsync(7, 100, query, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "7")).List(100, query, cts.Token);

        var response = Assert.IsType<ApiResponse<PagedResult<ParentReadingFeedbackItemDto>>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _service.VerifyAll();
    }

    [Fact]
    public async Task List_MissingIdentity_DoesNotCallService()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Controller().List(100, new ParentReadingFeedbackQueryDto(), CancellationToken.None));

        _service.VerifyNoOtherCalls();
    }
}
