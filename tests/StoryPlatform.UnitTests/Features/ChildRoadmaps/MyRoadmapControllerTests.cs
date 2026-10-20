using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class MyRoadmapControllerTests
{
    [Fact]
    public void Controller_IsOnlyForChildSessionsAndExposesOneAction()
    {
        var type = typeof(MyRoadmapController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
        Assert.Equal(new[] { "Get" }, methods);
    }
}
