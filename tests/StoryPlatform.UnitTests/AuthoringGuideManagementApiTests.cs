using System.Net;
using System.Text.Json;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class AuthoringGuideManagementApiTests
{
    private const string Base = "/api/v1/moderation/authoring-guides";

    [Fact]
    public async Task Only_moderator_and_administrator_can_use_the_management_api()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();

        await host.SendAsync(HttpMethod.Get, Base, null, null, HttpStatusCode.Unauthorized);
        foreach (var token in new[] { host.Parent, host.OtherParent, host.Creator, host.Child })
            await host.SendAsync(HttpMethod.Get, Base, null, token, HttpStatusCode.Forbidden);

        foreach (var token in new[] { host.Moderator, host.Administrator })
        {
            var overview = await host.SendAsync(HttpMethod.Get, Base, null, token, HttpStatusCode.OK);
            Assert.Equal("parent-vi-1", overview.GetProperty("published").GetProperty("version").GetProperty("catalogVersion").GetString());
            Assert.Equal(JsonValueKind.Null, overview.GetProperty("draft").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(overview.GetProperty("revisionToken").GetString()));
        }
    }

    [Fact]
    public async Task Moderator_reads_version_history_and_detail_but_unknown_version_is_404()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();

        var page = await host.SendAsync(HttpMethod.Get, Base + "/versions?pageIndex=1&pageSize=10", null, host.Moderator, HttpStatusCode.OK);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var versionId = page.GetProperty("items")[0].GetProperty("versionId").GetInt32();

        var detail = await host.SendAsync(HttpMethod.Get, Base + $"/versions/{versionId}", null, host.Administrator, HttpStatusCode.OK);
        Assert.Equal(30, detail.GetProperty("items").GetArrayLength());

        await host.SendAsync(HttpMethod.Get, Base + "/versions/999999", null, host.Moderator, HttpStatusCode.NotFound);
        await host.SendAsync(HttpMethod.Get, Base + "/versions?pageIndex=0", null, host.Moderator, HttpStatusCode.BadRequest);
    }

    private static object NewItem(string token) => new
    {
        revisionToken = token, purposes = new[] { "free" }, supportGroup = "opening", itemType = "template",
        title = "Mẫu mới", instruction = "Điền rồi chèn.", templateText = "Sáng nay, {{person}} thử {{activity}}.",
        placeholderDefinitions = new[] { "person", "activity" }, order = 31
    };

    [Fact]
    public async Task Draft_lifecycle_over_http_with_conflicts_and_validation()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();

        var created = await host.SendAsync(HttpMethod.Post, Base + "/draft", null, host.Moderator, HttpStatusCode.Created);
        var token = created.GetProperty("revisionToken").GetString()!;
        Assert.Equal("parent-vi-2", created.GetProperty("draft").GetProperty("catalogVersion").GetString());
        await host.SendAsync(HttpMethod.Post, Base + "/draft", new { }, host.Administrator, HttpStatusCode.Conflict);

        var added = await host.SendAsync(HttpMethod.Put, Base + "/draft/items/opening-9", NewItem(token), host.Moderator, HttpStatusCode.OK);
        var token2 = added.GetProperty("revisionToken").GetString()!;
        Assert.NotEqual(token, token2);
        Assert.Equal(31, added.GetProperty("draft").GetProperty("itemCount").GetInt32());

        await host.SendAsync(HttpMethod.Put, Base + "/draft/items/opening-8", NewItem(token), host.Administrator, HttpStatusCode.Conflict);
        await host.SendAsync(HttpMethod.Put, Base + "/draft/items/opening-8",
            new { revisionToken = token2, purposes = new[] { "free" }, supportGroup = "opening", itemType = "template", title = "x",
                  instruction = "y", templateText = "Xin chào {{person}}", placeholderDefinitions = new[] { "place" }, order = 32 },
            host.Moderator, HttpStatusCode.BadRequest);
        await host.SendAsync(HttpMethod.Delete, Base + "/draft/items/does-not-exist?revisionToken=" + token2, null, host.Moderator, HttpStatusCode.NotFound);
        await host.SendAsync(HttpMethod.Delete, Base + "/draft/items/opening-9", null, host.Moderator, HttpStatusCode.BadRequest);

        var removed = await host.SendAsync(HttpMethod.Delete, Base + "/draft/items/opening-9?revisionToken=" + token2, null, host.Moderator, HttpStatusCode.OK);
        Assert.Equal(30, removed.GetProperty("draft").GetProperty("itemCount").GetInt32());

        // Parent vẫn nhận bộ cũ trong suốt thời gian soạn nháp.
        var parent = await host.SendAsync(HttpMethod.Get, "/api/v1/authoring-guides?locale=vi&purpose=free&pageSize=100", null, host.Parent, HttpStatusCode.OK);
        Assert.Equal("parent-vi-1", parent.GetProperty("catalogVersion").GetString());
        Assert.Equal(21, parent.GetProperty("totalCount").GetInt32());

        // Chỉ vai trò quản lý mới ghi được.
        await host.SendAsync(HttpMethod.Post, Base + "/draft", new { }, host.Parent, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Put, Base + "/draft/items/x", NewItem(token), host.Creator, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Moderator_cycle_draft_edit_preview_publish_then_parent_sees_the_new_set()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();
        const string ParentList = "/api/v1/authoring-guides?locale=vi&purpose=free&pageSize=100";

        var token = (await host.SendAsync(HttpMethod.Post, Base + "/draft", null, host.Moderator, HttpStatusCode.Created))
            .GetProperty("revisionToken").GetString()!;
        token = (await host.SendAsync(HttpMethod.Put, Base + "/draft/items/opening-9", NewItem(token), host.Moderator, HttpStatusCode.OK))
            .GetProperty("revisionToken").GetString()!;

        var list = await host.SendAsync(HttpMethod.Post, Base + "/draft/preview", new { revisionToken = token, purpose = "free", pageSize = 100 },
            host.Administrator, HttpStatusCode.OK);
        Assert.Equal(22, list.GetProperty("page").GetProperty("totalCount").GetInt32());
        var render = await host.SendAsync(HttpMethod.Post, Base + "/draft/preview",
            new { revisionToken = token, guideId = "opening-9", values = new { person = "An", activity = "đạp xe" } }, host.Moderator, HttpStatusCode.OK);
        Assert.Equal("Sáng nay, An thử đạp xe.", render.GetProperty("render").GetProperty("text").GetString());
        await host.SendAsync(HttpMethod.Post, Base + "/draft/preview", new { revisionToken = "stale" }, host.Moderator, HttpStatusCode.Conflict);

        var before = await host.SendAsync(HttpMethod.Get, ParentList, null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(("parent-vi-1", 21), (before.GetProperty("catalogVersion").GetString(), before.GetProperty("totalCount").GetInt32()));

        await host.SendAsync(HttpMethod.Post, Base + "/draft/publish", new { }, host.Moderator, HttpStatusCode.BadRequest);
        await host.SendAsync(HttpMethod.Post, Base + "/draft/publish", new { revisionToken = "stale" }, host.Moderator, HttpStatusCode.Conflict);
        await host.SendAsync(HttpMethod.Post, Base + "/draft/publish", new { revisionToken = token }, host.Parent, HttpStatusCode.Forbidden);
        var published = await host.SendAsync(HttpMethod.Post, Base + "/draft/publish", new { revisionToken = token }, host.Moderator, HttpStatusCode.OK);
        Assert.Equal("parent-vi-2", published.GetProperty("published").GetProperty("catalogVersion").GetString());
        await host.SendAsync(HttpMethod.Post, Base + "/draft/publish", new { revisionToken = token }, host.Moderator, HttpStatusCode.Conflict);

        var after = await host.SendAsync(HttpMethod.Get, ParentList, null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(("parent-vi-2", 22), (after.GetProperty("catalogVersion").GetString(), after.GetProperty("totalCount").GetInt32()));

        // Client cũ không gửi catalogVersion vẫn render được; client giữ bản cũ nhận 409.
        var rendered = await host.SendAsync(HttpMethod.Post, "/api/v1/authoring-guides/opening-9/render",
            new { values = new { person = "An", activity = "đạp xe" } }, host.Parent, HttpStatusCode.OK);
        Assert.Equal("Sáng nay, An thử đạp xe.", rendered.GetProperty("text").GetString());
        await host.SendAsync(HttpMethod.Post, "/api/v1/authoring-guides/dialogue-1/render",
            new { values = new { person = "An", words = "Chào" }, catalogVersion = "parent-vi-1" }, host.Parent, HttpStatusCode.Conflict);

        // Khôi phục: sao chép phiên bản cũ rồi phát hành thành phiên bản mới.
        var versions = await host.SendAsync(HttpMethod.Get, Base + "/versions", null, host.Moderator, HttpStatusCode.OK);
        var v1 = versions.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("catalogVersion").GetString() == "parent-vi-1")
            .GetProperty("versionId").GetInt32();
        var restore = await host.SendAsync(HttpMethod.Post, Base + "/draft", new { sourceVersionId = v1 }, host.Administrator, HttpStatusCode.Created);
        await host.SendAsync(HttpMethod.Post, Base + "/draft/publish",
            new { revisionToken = restore.GetProperty("revisionToken").GetString() }, host.Administrator, HttpStatusCode.OK);
        var restored = await host.SendAsync(HttpMethod.Get, ParentList, null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(("parent-vi-3", 21), (restored.GetProperty("catalogVersion").GetString(), restored.GetProperty("totalCount").GetInt32()));
    }

    [Fact]
    public async Task Discarding_the_draft_over_http_is_soft_and_a_new_draft_can_follow()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();
        const string ParentList = "/api/v1/authoring-guides?locale=vi&purpose=free&pageSize=100";

        var token = (await host.SendAsync(HttpMethod.Post, Base + "/draft", null, host.Moderator, HttpStatusCode.Created))
            .GetProperty("revisionToken").GetString()!;

        await host.SendAsync(HttpMethod.Delete, Base + "/draft?revisionToken=" + token, null, host.Parent, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Delete, Base + "/draft", null, host.Moderator, HttpStatusCode.BadRequest);
        await host.SendAsync(HttpMethod.Delete, Base + "/draft?revisionToken=stale", null, host.Moderator, HttpStatusCode.Conflict);

        var discarded = await host.SendAsync(HttpMethod.Delete, Base + "/draft?revisionToken=" + token, null, host.Administrator, HttpStatusCode.OK);
        Assert.Equal("parent-vi-2", discarded.GetProperty("discardedCatalogVersion").GetString());
        var newToken = discarded.GetProperty("revisionToken").GetString()!;
        Assert.NotEqual(token, newToken);

        var overview = await host.SendAsync(HttpMethod.Get, Base, null, host.Moderator, HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("draft").ValueKind);
        var versions = await host.SendAsync(HttpMethod.Get, Base + "/versions", null, host.Moderator, HttpStatusCode.OK);
        Assert.Equal(1, versions.GetProperty("totalCount").GetInt32());
        await host.SendAsync(HttpMethod.Delete, Base + "/draft?revisionToken=" + newToken, null, host.Moderator, HttpStatusCode.NotFound);

        var again = await host.SendAsync(HttpMethod.Post, Base + "/draft", null, host.Moderator, HttpStatusCode.Created);
        Assert.Equal("parent-vi-2", again.GetProperty("draft").GetProperty("catalogVersion").GetString());

        var parent = await host.SendAsync(HttpMethod.Get, ParentList, null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(("parent-vi-1", 21), (parent.GetProperty("catalogVersion").GetString(), parent.GetProperty("totalCount").GetInt32()));
    }
}
