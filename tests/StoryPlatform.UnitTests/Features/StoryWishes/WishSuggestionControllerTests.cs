using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class WishSuggestionControllerTests
{
    private static ControllerContext Context(params Claim[] claims) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
    };

    [Fact]
    public void MatchController_IsForModerators_WithManualAndAiRoutes()
    {
        var type = typeof(WishCardMatchManagementController);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.Equal("Moderator,Administrator", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("api/v1/moderation/stories/{storyId:int}/wish-cards", type.GetCustomAttribute<RouteAttribute>(inherit: false)!.Template);
        Assert.Equal(new[] { "Add", "List", "MatchWithAi", "Remove" }, methods);
        Assert.Equal("ai-match", type.GetMethod("MatchWithAi")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{wishCardId:int}", type.GetMethod("Remove")!.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
    }

    [Fact]
    public async Task MatchController_Add_PassesTheModeratorStoryAndCard()
    {
        var service = new Mock<IWishCardMatchService>(MockBehavior.Strict);
        var expected = new WishCardMatchDto { StoryId = 20, WishCardId = 3 };
        service.Setup(s => s.AddAsync(11, 20, 3, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new WishCardMatchManagementController(service.Object)
            { ControllerContext = Context(new Claim(ClaimTypes.NameIdentifier, "11")) };

        var result = await controller.Add(20, new AddWishCardMatchRequestDto { WishCardId = 3 }, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<WishCardMatchDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
    }

    [Fact]
    public void ParentController_IsForParents_WithOneRoute()
    {
        var type = typeof(ParentWishSuggestionsController);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).ToArray();

        Assert.Equal("Parent", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("api/v1/parent/wishes", type.GetCustomAttribute<RouteAttribute>(inherit: false)!.Template);
        Assert.Equal(new[] { "Get" }, methods);
        Assert.Equal("{wishId:int}/suggestions", type.GetMethod("Get")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task ParentController_Get_UsesTheParentFromTheToken()
    {
        var service = new Mock<IStoryWishSuggestionService>(MockBehavior.Strict);
        var expected = new ParentWishSuggestionsDto { WishId = 5 };
        service.Setup(s => s.ForParentAsync(7, 5, It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new ParentWishSuggestionsController(service.Object)
            { ControllerContext = Context(new Claim(ClaimTypes.NameIdentifier, "7")) };

        var result = await controller.Get(5, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<ParentWishSuggestionsDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
    }
}
