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

    // ---- Trẻ ----

    [Fact]
    public void ChildController_IsOnlyForChildSessions_WithOneAction()
    {
        var type = typeof(ChildWishCardsController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).ToArray();

        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
        Assert.Equal("api/v1/child/wish-cards", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { "List" }, methods);
    }

    [Fact]
    public async Task ChildList_UsesTheChildFromTheTokenAndTheGroupQuery()
    {
        var service = new Mock<IChildWishCardService>(MockBehavior.Strict);
        IReadOnlyList<ChildWishCardDto> expected = new List<ChildWishCardDto> { new() { Id = 1, Label = "Chú mèo" } };
        service.Setup(s => s.ListAsync(100, "Character", It.IsAny<CancellationToken>())).ReturnsAsync(expected);
        var controller = new ChildWishCardsController(service.Object) { ControllerContext = Context(new Claim(ClaimTypes.NameIdentifier, "100")) };

        var result = await controller.List("Character", CancellationToken.None);

        var response = Assert.IsType<ApiResponse<IReadOnlyList<ChildWishCardDto>>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
    }

    [Fact]
    public async Task ChildList_MissingIdentity_DoesNotCallService()
    {
        var service = new Mock<IChildWishCardService>(MockBehavior.Strict);
        var controller = new ChildWishCardsController(service.Object) { ControllerContext = Context() };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.List("Character", CancellationToken.None));

        service.VerifyNoOtherCalls();
    }
}
