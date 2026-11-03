using System.Net;
using System.Net.Http.Json;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class StoryPackageConfirmationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Path = "/api/v1/stories/42/review/confirm-package";

    [Fact]
    public async Task Anonymous_cannot_confirm_package()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(Path, new { versionId = 82, operationId = "confirm-package-01" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Moderator_cannot_confirm_as_the_author()
    {
        var user = await factory.AddUserAsync(UserRole.Moderator);
        var response = await factory.ClientFor(user).PostAsJsonAsync(Path, new { versionId = 82, operationId = "confirm-package-01" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(0, "confirm-package-01")]
    [InlineData(82, "")]
    public async Task Version_and_operation_id_are_required(int version, string operation)
    {
        var user = await factory.AddUserAsync(UserRole.User);
        var response = await factory.ClientFor(user).PostAsJsonAsync(Path, new { versionId = version, operationId = operation });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
