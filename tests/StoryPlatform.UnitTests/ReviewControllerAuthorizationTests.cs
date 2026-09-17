using Microsoft.AspNetCore.Authorization;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ReviewControllerAuthorizationTests
{
    [Theory]
    [InlineData(nameof(ReviewController.Approve))]
    [InlineData(nameof(ReviewController.Archive))]
    public void SensitiveReviewEndpoint_RequiresAdultRole(string action)
    {
        var method = typeof(ReviewController).GetMethod(action)!;
        var authorization = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();

        Assert.Equal("Parent,Teacher,Administrator", authorization.Roles);
    }
}
