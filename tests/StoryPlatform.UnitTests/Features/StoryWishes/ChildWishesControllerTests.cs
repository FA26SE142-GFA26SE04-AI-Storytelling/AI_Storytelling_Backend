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

public class ChildWishesControllerTests
{
    private readonly Mock<IStoryWishService> _wishes = new(MockBehavior.Strict);
    private readonly Mock<IChildWishCardService> _cards = new(MockBehavior.Strict);

    private ChildWishesController Controller(params Claim[] claims) => new(_wishes.Object, _cards.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        }
    };

    private static Claim Child() => new(ClaimTypes.NameIdentifier, "100");

    [Fact]
    public void Controller_IsOnlyForChildSessions_WithTheFiveDocumentedRoutes()
    {
        var type = typeof(ChildWishesController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
        Assert.Equal("api/v1/child/wishes", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { "ListCards", "ListMine", "SelectCard", "StartDraft", "Submit" }, methods);
        Assert.Equal("/api/v1/child/wish-cards", type.GetMethod("ListCards")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("draft", type.GetMethod("StartDraft")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("draft/cards", type.GetMethod("SelectCard")!.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("draft/submit", type.GetMethod("Submit")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Null(type.GetMethod("ListMine")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task Submit_ReturnsTheWish_WithTheDocumentedMessage()
    {
        var expected = new ChildWishDto { WishId = 5, Status = "Pending" };
        _wishes.Setup(w => w.SubmitAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await Controller(Child()).Submit(CancellationToken.None);

        var response = Assert.IsType<ApiResponse<ChildWishDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        Assert.Equal(StoryWishPolicy.SubmittedMessage, response.Message);
    }

    [Fact]
    public async Task SelectCard_ForwardsTheRequest()
    {
        var request = new SelectWishCardRequestDto { Group = "Character", CardId = 3 };
        var expected = new WishDraftDto { WishId = 5 };
        _wishes.Setup(w => w.SelectCardAsync(100, request, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await Controller(Child()).SelectCard(request, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<WishDraftDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
        _wishes.VerifyAll();
    }

    [Fact]
    public async Task StartDraftAndListMine_UseTheChildFromTheToken()
    {
        _wishes.Setup(w => w.StartDraftAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(new WishDraftDto());
        _wishes.Setup(w => w.ListMineAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ChildWishDto>)new List<ChildWishDto>());
        var controller = Controller(Child());

        await controller.StartDraft(CancellationToken.None);
        await controller.ListMine(CancellationToken.None);

        _wishes.VerifyAll();
    }

    [Fact]
    public async Task ListCards_UsesTheChildFromTheTokenAndTheGroupQuery()
    {
        IReadOnlyList<ChildWishCardDto> expected = new List<ChildWishCardDto> { new() { Id = 1, Label = "Chú mèo" } };
        _cards.Setup(c => c.ListAsync(100, "Character", It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await Controller(Child()).ListCards("Character", CancellationToken.None);

        var response = Assert.IsType<ApiResponse<IReadOnlyList<ChildWishCardDto>>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
    }

    [Fact]
    public async Task MissingIdentity_DoesNotCallService()
    {
        var controller = Controller();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.ListCards("Character", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.StartDraft(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.SelectCard(new SelectWishCardRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.Submit(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.ListMine(CancellationToken.None));

        _wishes.VerifyNoOtherCalls();
        _cards.VerifyNoOtherCalls();
    }
}
