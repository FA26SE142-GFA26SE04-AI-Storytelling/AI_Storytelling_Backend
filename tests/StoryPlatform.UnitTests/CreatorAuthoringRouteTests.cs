using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAuthoringRouteTests
{
    [Theory]
    [InlineData(10, "revision", true)]
    [InlineData(0, "revision", false)]
    [InlineData(10, "", false)]
    public void ManualConfirmRequestUsesMvcRecordValidationMetadata(int versionId, string revision, bool valid)
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddControllers();
        using var provider = services.BuildServiceProvider();
        var action = new Microsoft.AspNetCore.Mvc.ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = provider },
            new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        provider.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.Validation.IObjectModelValidator>().Validate(action, null, "",
            new StoryPlatform.Application.Features.CreatorAuthoring.Services.ConfirmManualContentRequest(versionId, revision));
        Assert.Equal(valid, action.ModelState.IsValid);
    }

    [Fact]
    public void ManualCreateAndSave_AreExplicitRoutes_SeparateFromAiInput()
    {
        // Inspect MVC's actual route model, including its handling of BaseApiController.
        // No application host, controller instance, database or provider is started.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(CreatorAuthoringController).Assembly);
        using var provider = services.BuildServiceProvider();
        var actions = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().ToList();
        var creator = actions.Where(x => x.ControllerTypeInfo.AsType() == typeof(CreatorAuthoringController)).ToList();
        Assert.Equal("api/v1/creator/stories/manual", Assert.Single(creator, x => x.ActionName == "Create").AttributeRouteInfo!.Template);
        Assert.Equal("api/v1/creator/stories/{storyId:int}/manual/versions", Assert.Single(creator, x => x.ActionName == "Save").AttributeRouteInfo!.Template);
        Assert.Equal("api/v1/creator/stories/{storyId:int}/manual/confirm-content", Assert.Single(creator, x => x.ActionName == "ConfirmContent").AttributeRouteInfo!.Template);
        Assert.DoesNotContain(creator, x => x.AttributeRouteInfo!.Template!.Contains("products"));
        Assert.All(actions.Where(x => x.ControllerTypeInfo.AsType() == typeof(AIStoryInputController)),
            x => Assert.StartsWith("api/v1/ai-story-input/", x.AttributeRouteInfo!.Template));
    }
}
