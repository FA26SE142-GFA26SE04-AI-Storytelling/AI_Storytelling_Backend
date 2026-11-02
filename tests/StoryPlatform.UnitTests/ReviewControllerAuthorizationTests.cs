using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ReviewControllerAuthorizationTests
{
    [Theory]
    [InlineData(nameof(ReviewController.Approve))]
    [InlineData(nameof(ReviewController.Reject))]
    public void SensitiveReviewEndpoint_RequiresAdultRole(string action)
    {
        var method = typeof(ReviewController).GetMethod(action)!;
        var authorization = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();

        Assert.Equal("User,Administrator", authorization.Roles);
    }

    [Fact]
    public void Reject_UsesNewRouteWithoutArchiveAlias()
    {
        var method = typeof(ReviewController).GetMethod(nameof(ReviewController.Reject))!;
        var route = method.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>().Single();

        Assert.Equal("reject", route.Template);
        Assert.Null(typeof(ReviewController).GetMethod("Archive"));
    }
}
