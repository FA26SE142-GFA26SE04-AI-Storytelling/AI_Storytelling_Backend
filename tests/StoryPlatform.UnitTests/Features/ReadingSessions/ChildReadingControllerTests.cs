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
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ReadingSessions;

public class ChildReadingControllerTests
{
    private readonly Mock<IChildStoryBrowseService> _browse = new(MockBehavior.Strict);
    private readonly Mock<IChildReadingService> _reading = new(MockBehavior.Strict);
    private readonly Mock<IChildNotebookService> _notebook = new(MockBehavior.Strict);

    private ChildReadingController Controller(params Claim[] claims) => new(_browse.Object, _reading.Object, _notebook.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        }
    };

    [Fact]
    public void Controller_IsOnlyForChildSessions()
    {
        var authorize = typeof(ChildReadingController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
    }

    [Fact]
    public void ReadingController_ExposesTheDocumentedActions()
    {
        var methods = typeof(ChildReadingController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.Equal(new[] { "Complete", "GetContent", "GetDefinition", "GetSignal", "LogEvent", "SaveProgress", "SearchStories", "SetBookmark", "SetFavorite", "Start" }, methods);
    }

    [Fact]
    public void Controller_UsesDocumentedRoutes()
    {
        var type = typeof(ChildReadingController);
        Assert.Equal("api/v1/child/reading", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("stories", type.GetMethod("SearchStories")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("sessions", type.GetMethod("Start")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}/content", type.GetMethod("GetContent")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}/progress", type.GetMethod("SaveProgress")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
    }

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public async Task Start_UsesChildIdentityAndSessionKeyFromClaims(string claimType)
    {
        using var cts = new CancellationTokenSource();
        var request = new StartReadingRequestDto { StoryId = 10 };
        var expected = new ReadingSessionStartedDto { SessionId = 22, StoryId = 10 };
        _reading.Setup(r => r.StartAsync(100, "key", request, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(claimType, "100"), new Claim(ChildSession.SessionClaimType, "key"));

        var result = await controller.Start(request, cts.Token);

        var response = Assert.IsType<ApiResponse<ReadingSessionStartedDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.Success);
        Assert.Same(expected, response.Data);
        _reading.VerifyAll();
    }

    [Fact]
    public async Task SearchStories_UsesChildIdentityAndQuery()
    {
        using var cts = new CancellationTokenSource();
        var query = new ChildStoryQueryDto { Search = "thỏ", Source = "mine" };
        var expected = new ChildStoryPageDto();
        _browse.Setup(b => b.SearchAsync(100, query, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        var result = await controller.SearchStories(query, cts.Token);

        var response = Assert.IsType<ApiResponse<ChildStoryPageDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _browse.VerifyAll();
    }

    [Fact]
    public async Task Start_MissingSessionClaim_DoesNotCallService()
    {
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Start(new StartReadingRequestDto { StoryId = 10 }, CancellationToken.None));
        _reading.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task SearchStories_InvalidIdentity_DoesNotCallService(string? identifier)
    {
        var controller = identifier == null ? Controller() : Controller(new Claim(ClaimTypes.NameIdentifier, identifier));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SearchStories(new ChildStoryQueryDto(), CancellationToken.None));
        _browse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetContent_ForwardsTokenIdentitySessionAndCancellation()
    {
        using var cts = new CancellationTokenSource();
        var expected = new ReadingContentDto { SessionId = 22, StoryId = 10 };
        _reading.Setup(r => r.GetContentAsync(100, 22, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        var result = await controller.GetContent(22, cts.Token);

        var response = Assert.IsType<ApiResponse<ReadingContentDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.Success);
        Assert.Same(expected, response.Data);
        _reading.VerifyAll();
    }

    [Fact]
    public async Task SaveProgress_ForwardsTokenIdentitySessionRequestAndCancellation()
    {
        using var cts = new CancellationTokenSource();
        var request = new SaveProgressRequestDto { Page = 3, ReadingMode = "ReadFirst", AddedSeconds = 30 };
        var expected = new ReadingProgressDto { SessionId = 22, Page = 3, ReadingMode = "ReadFirst", TimeSpentSeconds = 30 };
        _reading.Setup(r => r.SaveProgressAsync(100, 22, request, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        var result = await controller.SaveProgress(22, request, cts.Token);

        var response = Assert.IsType<ApiResponse<ReadingProgressDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.Success);
        Assert.Same(expected, response.Data);
        _reading.VerifyAll();
    }

    [Fact]
    public void CompletionAndFlagActions_UseDocumentedRoutes()
    {
        var type = typeof(ChildReadingController);
        Assert.Equal("sessions/{sessionId:int}/complete", type.GetMethod("Complete")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}/bookmark", type.GetMethod("SetBookmark")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}/favorite", type.GetMethod("SetFavorite")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
    }

    [Fact]
    public async Task Complete_ForwardsTokenIdentitySessionAndCancellation()
    {
        using var cts = new CancellationTokenSource();
        var expected = new ReadingCompletedDto { SessionId = 22, StoryId = 10, RoadmapStepCompleted = true };
        _reading.Setup(r => r.CompleteAsync(100, 22, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        var result = await controller.Complete(22, cts.Token);

        var response = Assert.IsType<ApiResponse<ReadingCompletedDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.True(response.Success);
        Assert.Same(expected, response.Data);
        _reading.VerifyAll();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetBookmark_ForwardsValue(bool value)
    {
        using var cts = new CancellationTokenSource();
        var expected = new ReadingFlagsDto { IsBookmarked = value };
        _reading.Setup(r => r.SetBookmarkAsync(100, 22, value, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        var result = await controller.SetBookmark(22, new SetFlagRequestDto { Value = value }, cts.Token);

        var response = Assert.IsType<ApiResponse<ReadingFlagsDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _reading.VerifyAll();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetFavorite_ForwardsValue(bool value)
    {
        using var cts = new CancellationTokenSource();
        var expected = new ReadingFlagsDto { IsFavorited = value };
        _reading.Setup(r => r.SetFavoriteAsync(100, 22, value, cts.Token)).ReturnsAsync(expected);
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "100"));

        var result = await controller.SetFavorite(22, new SetFlagRequestDto { Value = value }, cts.Token);

        var response = Assert.IsType<ApiResponse<ReadingFlagsDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _reading.VerifyAll();
    }

    [Fact]
    public async Task CompletionAndFlags_MissingIdentity_DoNotCallService()
    {
        var controller = Controller();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Complete(22, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SetBookmark(22, new SetFlagRequestDto { Value = true }, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SetFavorite(22, new SetFlagRequestDto { Value = true }, CancellationToken.None));

        _reading.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContentAndProgress_MissingIdentity_DoNotCallService()
    {
        var controller = Controller();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetContent(22, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SaveProgress(22, new SaveProgressRequestDto { Page = 1 }, CancellationToken.None));

        _reading.VerifyNoOtherCalls();
    }

    [Fact]
    public void NotebookController_IsOnlyForChildSessions_AndExposesCollectListRemove()
    {
        var type = typeof(ChildNotebookController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Equal(new[] { "Collect", "List", "Remove" }, methods);
    }
}
