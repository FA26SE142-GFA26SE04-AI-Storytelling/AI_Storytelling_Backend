using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class CreatorAuthoringRouteTests
{
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
        Assert.DoesNotContain(creator, x => x.AttributeRouteInfo!.Template!.Contains("products"));
        Assert.All(actions.Where(x => x.ControllerTypeInfo.AsType() == typeof(AIStoryInputController)),
            x => Assert.StartsWith("api/v1/ai-story-input/", x.AttributeRouteInfo!.Template));
    }
}
