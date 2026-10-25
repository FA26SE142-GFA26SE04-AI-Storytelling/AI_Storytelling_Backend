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

public class ChildQuizControllerTests
{
    private readonly Mock<IChildQuizService> _quiz = new(MockBehavior.Strict);

    private ChildQuizController Controller(params Claim[] claims) => new(_quiz.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        }
    };

    [Fact]
    public void Controller_IsOnlyForChildSessions_AndExposesTheThreeActions()
    {
        var type = typeof(ChildQuizController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
        Assert.Equal(new[] { "GetQuiz", "GetResult", "SubmitAnswer" }, methods);
    }

    [Fact]
    public void Controller_UsesDocumentedRoutes()
    {
        var type = typeof(ChildQuizController);
        Assert.Equal("api/v1/child/quiz", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}", type.GetMethod("GetQuiz")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}/answers", type.GetMethod("SubmitAnswer")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("sessions/{sessionId:int}/result", type.GetMethod("GetResult")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task GetQuiz_ForwardsTokenIdentityAndSession()
    {
        using var cts = new CancellationTokenSource();
        var expected = new QuizDto { SessionId = 22 };
        _quiz.Setup(q => q.GetQuizAsync(100, 22, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "100")).GetQuiz(22, cts.Token);

        var response = Assert.IsType<ApiResponse<QuizDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _quiz.VerifyAll();
    }

    [Fact]
    public async Task SubmitAnswer_ForwardsRequest()
    {
        using var cts = new CancellationTokenSource();
        var request = new SubmitQuizAnswerRequestDto { QuizItemId = 3, Answer = "Chú Thỏ" };
        var expected = new QuizAnswerResultDto { QuizItemId = 3, IsCorrect = true };
        _quiz.Setup(q => q.SubmitAnswerAsync(100, 22, request, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "100")).SubmitAnswer(22, request, cts.Token);

        var response = Assert.IsType<ApiResponse<QuizAnswerResultDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _quiz.VerifyAll();
    }

    [Fact]
    public async Task GetResult_ForwardsTokenIdentityAndSession()
    {
        using var cts = new CancellationTokenSource();
        var expected = new QuizResultDto { SessionId = 22 };
        _quiz.Setup(q => q.GetResultAsync(100, 22, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "100")).GetResult(22, cts.Token);

        var response = Assert.IsType<ApiResponse<QuizResultDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _quiz.VerifyAll();
    }

    [Fact]
    public async Task MissingIdentity_DoesNotCallService()
    {
        var controller = Controller();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetQuiz(22, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SubmitAnswer(22, new SubmitQuizAnswerRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetResult(22, CancellationToken.None));

        _quiz.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQuiz_AcceptsSubClaimWhenNameIdentifierIsAbsent()
    {
        var expected = new QuizDto { SessionId = 22 };
        _quiz.Setup(q => q.GetQuizAsync(100, 22, CancellationToken.None)).ReturnsAsync(expected);

        var result = await Controller(new Claim("sub", "100")).GetQuiz(22, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<QuizDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _quiz.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-id")]
    public async Task InvalidIdentity_DoesNotCallService(string identity)
    {
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, identity));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetQuiz(22, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SubmitAnswer(22, new SubmitQuizAnswerRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.GetResult(22, CancellationToken.None));

        _quiz.VerifyNoOtherCalls();
    }
}
