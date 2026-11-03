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

public class ChildRewardsControllerTests
{
    private readonly Mock<IChildRewardService> _rewards = new(MockBehavior.Strict);

    private ChildRewardsController Controller(params Claim[] claims) => new(_rewards.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        }
    };

    [Fact]
    public void Controller_IsOnlyForChildSessions_AndExposesTwoActions()
    {
        var type = typeof(ChildRewardsController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
        Assert.Equal(new[] { "Get", "WaterKnowledgeTree" }, methods);
        Assert.Equal("api/v1/child/rewards", type.GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public async Task Get_UsesTheChildFromTheToken()
    {
        using var cts = new CancellationTokenSource();
        var expected = new ChildRewardsDto { ExpTotal = 12 };
        _rewards.Setup(r => r.GetSummaryAsync(100, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "100")).Get(cts.Token);

        var response = Assert.IsType<ApiResponse<ChildRewardsDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _rewards.VerifyAll();
    }

    [Fact]
    public async Task Get_MissingIdentity_DoesNotCallService()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().Get(CancellationToken.None));

        _rewards.VerifyNoOtherCalls();
    }

    [Fact]
    public void WaterKnowledgeTree_IsAPostOnTheTreeRoute()
    {
        var method = typeof(ChildRewardsController).GetMethod(nameof(ChildRewardsController.WaterKnowledgeTree))!;

        Assert.Equal("knowledge-tree/water", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task WaterKnowledgeTree_UsesTheChildFromTheToken()
    {
        using var cts = new CancellationTokenSource();
        var expected = new WaterKnowledgeTreeResultDto { ExpPoured = 12 };
        _rewards.Setup(r => r.WaterKnowledgeTreeAsync(100, cts.Token)).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "100")).WaterKnowledgeTree(cts.Token);

        var response = Assert.IsType<ApiResponse<WaterKnowledgeTreeResultDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _rewards.VerifyAll();
    }

    [Fact]
    public async Task WaterKnowledgeTree_MissingIdentity_DoesNotCallService()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Controller().WaterKnowledgeTree(CancellationToken.None));

        _rewards.VerifyNoOtherCalls();
    }
}
