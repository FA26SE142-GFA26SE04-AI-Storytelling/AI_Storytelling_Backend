using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.AIStoryInput.DTOs;
using StoryPlatform.Application.Features.StoryWishes;
using StoryPlatform.Application.Features.StoryWishes.Interfaces;
using Xunit;

namespace StoryPlatform.UnitTests.Features.StoryWishes;

public class ParentWishesControllerTests
{
    private readonly Mock<IParentStoryWishService> _service = new(MockBehavior.Strict);

    private ParentWishesController Controller(params Claim[] claims) => new(_service.Object)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        }
    };

    [Fact]
    public void Controller_IsForParents_WithTheSixDocumentedRoutes()
    {
        var type = typeof(ParentWishesController);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.Equal("Parent", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("api/v1/parent/wishes", type.GetCustomAttribute<RouteAttribute>(inherit: false)!.Template);
        Assert.Equal(new[] { "CreateStory", "Decline", "Get", "GetGenerationForm", "List", "UseExisting" }, methods);
        Assert.Null(type.GetMethod("List")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{wishId:int}", type.GetMethod("Get")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{wishId:int}/generation-form", type.GetMethod("GetGenerationForm")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{wishId:int}/create-story", type.GetMethod("CreateStory")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{wishId:int}/use-existing", type.GetMethod("UseExisting")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{wishId:int}/decline", type.GetMethod("Decline")!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task List_UsesTheParentFromTheToken()
    {
        IReadOnlyList<ParentWishDto> expected = new List<ParentWishDto>();
        _service.Setup(s => s.ListAsync(7, 100, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await Controller(new Claim(ClaimTypes.NameIdentifier, "7")).List(100, CancellationToken.None);

        var response = Assert.IsType<ApiResponse<IReadOnlyList<ParentWishDto>>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Same(expected, response.Data);
    }

    [Fact]
    public async Task Decisions_ForwardTheirBodies()
    {
        var decline = new DeclineWishRequestDto { Reason = "x" };
        var use = new UseExistingStoryRequestDto { SuggestionRank = 2 };
        var form = new SubmitAIStoryInputRequestDto { ChildProfileId = 100 };
        _service.Setup(s => s.DeclineAsync(7, 5, decline, It.IsAny<CancellationToken>())).ReturnsAsync(new ParentWishDto());
        _service.Setup(s => s.UseExistingAsync(7, 5, use, It.IsAny<CancellationToken>())).ReturnsAsync(new ParentWishDto());
        _service.Setup(s => s.CreateStoryAsync(7, 5, form, It.IsAny<CancellationToken>())).ReturnsAsync(new ParentWishCreateStoryResultDto());
        var controller = Controller(new Claim(ClaimTypes.NameIdentifier, "7"));

        await controller.Decline(5, decline, CancellationToken.None);
        await controller.UseExisting(5, use, CancellationToken.None);
        await controller.CreateStory(5, form, CancellationToken.None);

        _service.VerifyAll();
    }

    [Fact]
    public async Task MissingIdentity_DoesNotCallService()
    {
        var controller = Controller();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => controller.List(100, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            controller.Decline(5, new DeclineWishRequestDto(), CancellationToken.None));

        _service.VerifyNoOtherCalls();
    }
}
