using System.Text.Json;
using StoryPlatform.Api.Extensions;
using Xunit;

namespace StoryPlatform.UnitTests.Authorization;

/// <summary>
/// Ma trận "ai gọi được action nào" phải khớp snapshot đã chụp trước khi chuyển sang permission.
/// Đổi quyền có chủ đích: chạy với UPDATE_AUTHZ_SNAPSHOT=1 rồi xem diff của file snapshot.
/// </summary>
public class EndpointAuthorizationMatrixTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Matrix_matches_snapshot()
    {
        var actual = await AuthorizationMatrix.ComputeAsync(
            AuthorizationMatrix.BuildServices(services => services.AddAppAuthorization()));

        if (Environment.GetEnvironmentVariable("UPDATE_AUTHZ_SNAPSHOT") == "1")
        {
            File.WriteAllText(SnapshotPath(), JsonSerializer.Serialize(actual, JsonOptions) + Environment.NewLine);
            return;
        }

        var expected = new SortedDictionary<string, string[]>(
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(SnapshotPath()))!,
            StringComparer.Ordinal);
        Assert.Equal(expected.Keys, actual.Keys);
        var mismatches = expected.Keys
            .Where(key => !expected[key].SequenceEqual(actual[key]))
            .Select(key => $"{key}: expected [{string.Join(",", expected[key])}] got [{string.Join(",", actual[key])}]")
            .ToList();
        Assert.Empty(mismatches);
    }

    private static string SnapshotPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StoryPlatform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root!.FullName, "tests", "StoryPlatform.UnitTests", "Authorization", "endpoint-authorization.snapshot.json");
    }
}
