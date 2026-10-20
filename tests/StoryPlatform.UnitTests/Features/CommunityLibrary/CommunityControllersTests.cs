using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests.Features.CommunityLibrary;

public class CommunityControllersTests
{
    private static string[] Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).OrderBy(name => name).ToArray();

    [Fact]
    public void ReviewController_IsRestrictedToModeratorsAndAdministrators()
    {
        var authorize = typeof(CommunityReviewController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Moderator,Administrator", authorize!.Roles);
        Assert.Equal(
            new[] { "Approve", "GetPackage", "Reject", "SaveChecklist", "SetLabels" },
            Methods(typeof(CommunityReviewController)));
    }

    [Fact]
    public void LibraryController_IsOnlyForChildSessions()
    {
        var authorize = typeof(CommunityLibraryController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("ChildSession", authorize!.Policy);
        Assert.Null(authorize.Roles);
        Assert.Equal(new[] { "Get", "List" }, Methods(typeof(CommunityLibraryController)));
    }
}
