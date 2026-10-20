using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildRoadmaps;

public class ChildRoadmapControllerTests
{
    [Fact]
    public void Controller_IsRestrictedToParentsWithTheFiveActions()
    {
        var type = typeof(ChildRoadmapController);
        var authorize = type.GetCustomAttribute<AuthorizeAttribute>();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

        Assert.NotNull(authorize);
        Assert.Equal("Parent", authorize!.Roles);
        Assert.Equal(new[] { "AddStory", "Clear", "Get", "Remove", "Reorder" }, methods);
    }
}
