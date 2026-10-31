using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.WishCards;
using StoryPlatform.Application.Features.WishCards.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.WishCards;

public class WishCardControllerTests
{
    private static ControllerContext Context(params Claim[] claims) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
    };

    // ---- Moderator ----

    [Fact]
    public void ManagementController_IsForModeratorsAndAdministrators_WithFiveActions()
    {
        var type = typeof(WishCardManagementController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal("api/v1/moderation/wish-cards", type.GetCustomAttribute<RouteAttribute>(inherit: false)!.Template);
        Assert.Equal(new[] { "Create", "Get", "List", "SetActive", "Update" }, methods);
    }

    [Fact]
    public void ManagementController_UsesDocumentedRoutes()
    {
        var type = typeof(WishCardManagementController);
        Assert.Null(type.GetMethod("List")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{id:int}", type.GetMethod("Get")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Null(type.GetMethod("Create")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{id:int}", type.GetMethod("Update")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{id:int}/active", type.GetMethod("SetActive")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
    }

    [Fact]
    public async Task Create_PassesTheModeratorAndReturns201()
    {
        var service = new Mock<IWishCardManagementService>(MockBehavior.Strict);
        var request = new CreateWishCardRequestDto { Group = "Character", Label = "Chú mèo", ImageUrl = "https://cdn.test/a.png" };
        var created = new WishCardDto { Id = 5, Label = "Chú mèo" };
        service.Setup(s => s.CreateAsync(11, request, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        var controller = new WishCardManagementController(service.Object) { ControllerContext = Context(new Claim(ClaimTypes.NameIdentifier, "11")) };

        var result = await controller.Create(request, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(201, status.StatusCode);
        Assert.Same(created, Assert.IsType<ApiResponse<WishCardDto>>(status.Value).Data);
    }

    [Fact]
    public async Task SetActive_PassesModeratorIdAndFlag()
    {
        var service = new Mock<IWishCardManagementService>(MockBehavior.Strict);
        var hidden = new WishCardDto { Id = 5, IsActive = false };
        service.Setup(s => s.SetActiveAsync(11, 5, false, It.IsAny<CancellationToken>())).ReturnsAsync(hidden);
        var controller = new WishCardManagementController(service.Object) { ControllerContext = Context(new Claim(ClaimTypes.NameIdentifier, "11")) };

        var result = await controller.SetActive(5, new SetWishCardActiveRequestDto { IsActive = false }, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<WishCardDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(hidden, response.Data);
        service.VerifyAll();
    }
}
