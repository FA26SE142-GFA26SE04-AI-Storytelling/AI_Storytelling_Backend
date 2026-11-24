using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Luồng 1, 3, 4: mỗi hồ sơ trẻ, truyện gia đình, điều ước thuộc một phụ huynh; truyện Creator thuộc một Creator.
/// Quét mọi endpoint phụ huynh / Creator có id tài nguyên trên route (lấy từ bảng route của ứng dụng thật) và gọi bằng
/// tài khoản cùng role nhưng không phải chủ: không endpoint nào được trả thành công, endpoint đọc phải trả 403 hoặc 404.
/// </summary>
public sealed class CrossOwnerAccessApiTests(ApiFactory factory, ITestOutputHelper output) : IClassFixture<ApiFactory>
{
    private const int MissingId = 987654;

    /// <summary>Endpoint công khai theo thiết kế: không có chủ sở hữu để kiểm.</summary>
    private static readonly IReadOnlyDictionary<string, string> PublicByDesign = new Dictionary<string, string>
    {
        ["api/v1/authoring-guides/{guideId}/render"] = "Hướng dẫn sáng tác là danh mục chung cho mọi phụ huynh",
        ["api/v1/Story/community/{id:int}/media"] = "Chỉ trả media của truyện Creator đã xuất bản",
    };

    private static readonly string[] OwnedGroups =
        [PermissionCatalog.ParentGroup, PermissionCatalog.SharedGroup, PermissionCatalog.CreatorGroup];

    private sealed record Owned(int ChildId, int FamilyStoryId, int WishId, int CreatorStoryId);

    private sealed record Probe(string Method, string Template, string Permission, string Caller, string Url);

    [Fact]
    public async Task NonOwner_never_succeeds_on_another_owners_child_story_or_wish()
    {
        var owned = await SeedOwnerAAsync();
        var callers = new Dictionary<string, HttpClient>
        {
            [RoleCodes.User] = await factory.ClientForNewUserAsync(UserRole.User),
            [RoleCodes.ContentCreator] = await factory.ClientForNewUserAsync(UserRole.ContentCreator),
        };

        var probes = BuildProbes(owned).ToList();
        Assert.Contains(probes, p => p.Template.Contains("childProfileId", StringComparison.Ordinal));
        Assert.Contains(probes, p => p.Template.Contains("parent/stories/{storyId", StringComparison.Ordinal));
        Assert.Contains(probes, p => p.Template.Contains("creator/stories/{storyId", StringComparison.Ordinal));
        Assert.Contains(probes, p => p.Template.Contains("wishes/{wishId", StringComparison.Ordinal));

        var failures = new List<string>();
        foreach (var probe in probes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(probe.Method), probe.Url);
            if (probe.Method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(new { });
            using var response = await callers[probe.Caller].SendAsync(request);
            var status = (int)response.StatusCode;
            output.WriteLine($"{status} {probe.Method} {probe.Url} ({probe.Caller})");
            var readDenied = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
            if (status is >= 200 and < 300 || (probe.Method == "GET" && !readDenied))
                failures.Add($"{probe.Method} {probe.Url} ({probe.Permission}, {probe.Caller}) → {status}: "
                             + Truncate(await response.Content.ReadAsStringAsync()));
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count}/{probes.Count} endpoint cho người không phải chủ truy cập:\n" + string.Join("\n", failures));
    }

    private IEnumerable<Probe> BuildProbes(Owned owned)
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        foreach (var endpoint in endpoints)
        {
            var template = endpoint.RoutePattern.RawText ?? string.Empty;
            if (PublicByDesign.ContainsKey(template)) continue;
            var parameters = endpoint.RoutePattern.Parameters.Select(p => p.Name).ToArray();
            if (!parameters.Any(IsOwnedResource)) continue;

            var permission = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(data => data.Policy)
                .LastOrDefault(policy => policy?.StartsWith(PermissionPolicyProvider.Prefix, StringComparison.Ordinal) == true)
                ?[PermissionPolicyProvider.Prefix.Length..];
            var definition = permission is null ? null : PermissionCatalog.Find(permission);
            if (definition is null || !OwnedGroups.Contains(definition.Group)) continue;

            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            foreach (var caller in new[] { RoleCodes.User, RoleCodes.ContentCreator })
            {
                // Người gọi phải giữ quyền của endpoint, để 403 đến từ kiểm chủ sở hữu chứ không phải từ phân quyền.
                if (!DefaultRolePermissions.Map[caller].Contains(permission!)) continue;
                var storyId = caller == RoleCodes.ContentCreator ? owned.CreatorStoryId : owned.FamilyStoryId;
                var url = "/" + Regex.Replace(template, @"\{(\w+)(?::[^}]*)?\}", match => match.Groups[1].Value switch
                {
                    "childProfileId" or "childId" => owned.ChildId.ToString(),
                    "id" when IsChildController(template) => owned.ChildId.ToString(),
                    "storyId" => storyId.ToString(),
                    "wishId" => owned.WishId.ToString(),
                    _ => MissingId.ToString(),
                });
                yield return new Probe(method, template, permission!, caller, url);
            }
        }
    }

    private static bool IsOwnedResource(string name) =>
        name is "childProfileId" or "childId" or "storyId" or "wishId" or "id";

    private static bool IsChildController(string template) =>
        Regex.IsMatch(template, "^api/v1/(ChildProfile|LearningProfile|SafetyPolicy|ChildAccessCredential|ReadingSchedule)/",
            RegexOptions.IgnoreCase);

    private async Task<Owned> SeedOwnerAAsync()
    {
        var parentA = await factory.AddUserAsync(UserRole.User);
        var creatorA = await factory.AddUserAsync(UserRole.ContentCreator);
        var child = await ApiSeed.ChildAsync(factory, parentA.Id);
        var family = await ApiSeed.ParentStoryAsync(factory, parentA.Id, child.Id, StoryStatus.Ready);
        return await factory.WithDbAsync(async db =>
        {
            var wish = new StoryWish
            {
                ChildProfileId = child.Id, Status = StoryWishStatus.Pending,
                SubmittedAt = DateTime.UtcNow, PendingSince = DateTime.UtcNow,
            };
            var creatorStory = new Story
            {
                AuthorUserId = creatorA.Id, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Draft, Title = "Truyện của Creator A", Language = "vi", ReadingLevel = 2,
            };
            db.StoryWishes.Add(wish);
            db.Stories.Add(creatorStory);
            await db.SaveChangesAsync();
            return new Owned(child.Id, family.Id, wish.Id, creatorStory.Id);
        });
    }

    private static string Truncate(string text) => text.Length <= 160 ? text : text[..160] + "…";
}
