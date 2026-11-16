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
    public void Controller_IsOnlyForParents_AndExposesListQuizAndOverview()
    {
        var type = typeof(ParentReadingFeedbackController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("User", authorize!.Roles);
        Assert.Equal(new[] { "List", "Overview", "Quiz" }, methods);
        Assert.Equal("{childProfileId:int}", type.GetMethod("List")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{childProfileId:int}/sessions/{sessionId:int}/quiz",
            type.GetMethod("Quiz")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{childProfileId:int}/overview",
            type.GetMethod("Overview")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task Overview_ForwardsParentChildAndQuery()
    {
        using var cts = new CancellationTokenSource();
        var query = new ParentReadingOverviewQueryDto { From = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) };
        var expected = new ParentReadingOverviewDto { CompletedSessions = 3 };
        _service.Setup(s => s.GetOverviewAsync(7, 100, query, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "7")).Overview(100, query, cts.Token);

        var response = Assert.IsType<ApiResponse<ParentReadingOverviewDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _service.VerifyAll();
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

    [Fact]
    public async Task Quiz_ForwardsParentChildAndSession()
    {
        using var cts = new CancellationTokenSource();
        var expected = new ParentQuizDetailDto { SessionId = 5 };
        _service.Setup(s => s.GetQuizAsync(7, 100, 5, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "7")).Quiz(100, 5, cts.Token);

        var response = Assert.IsType<ApiResponse<ParentQuizDetailDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _service.VerifyAll();
    }
}
