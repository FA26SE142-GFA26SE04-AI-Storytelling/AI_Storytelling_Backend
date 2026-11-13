using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Đánh giá truyện Creator qua HTTP thật: phụ huynh → báo cáo → Moderator → thư viện.</summary>
public sealed class StoryRatingApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ParentRates_ModeratorDecides_LibraryCountsOnlyPublishedRatings()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var otherParent = await factory.AddUserAsync(UserRole.User);
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var storyId = await SeedReadStoryAsync(creator, parent, otherParent);
        var parentClient = factory.ClientFor(parent);
        var otherClient = factory.ClientFor(otherParent);
        var url = $"/api/v1/StoryRating/story/{storyId}";

        // 5 sao, nhận xét sạch: hiện ngay, số điện thoại bị che.
        var mine = await PutDataAsync(parentClient, $"{url}/me", new { rating = 5, comment = "Bé rất thích, gọi 0912345678 nhé" });
        Assert.Equal("Published", mine.GetProperty("status").GetString());
        Assert.DoesNotContain("0912345678", mine.GetProperty("comment").GetString());

        // 1 sao kèm chữ: chờ Moderator, chưa được tính điểm.
        var other = await PutDataAsync(otherClient, $"{url}/me", new { rating = 1, comment = "Kết truyện hơi vội" });
        Assert.Equal("PendingModeration", other.GetProperty("status").GetString());

        var list = await AdminApi.GetDataAsync(otherClient, url);
        Assert.Equal(1, list.GetProperty("summary").GetProperty("ratingCount").GetInt32());
        Assert.Equal(5.0, list.GetProperty("summary").GetProperty("averageRating").GetDouble());
        Assert.Equal("PendingModeration", list.GetProperty("myRating").GetProperty("status").GetString());
        Assert.True(list.GetProperty("canRate").GetBoolean());
        var shown = Assert.Single(list.GetProperty("ratings").GetProperty("items").EnumerateArray().ToArray());
        Assert.StartsWith("Phụ huynh ", shown.GetProperty("reviewerName").GetString());
        var raw = list.GetRawText();
        Assert.DoesNotContain(parent.Email, raw);
        Assert.DoesNotContain(parent.Username, raw);

        // Báo cáo: tự báo cáo → 400; báo cáo lần hai → 409.
        var ratingId = shown.GetProperty("ratingId").GetInt32();
        var reportUrl = $"/api/v1/StoryRating/{ratingId}/reports";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await parentClient.PostAsJsonAsync(reportUrl, new { reason = "Spam" })).StatusCode);
        await AdminApi.PostDataAsync(otherClient, reportUrl, new { reason = "Offensive", note = "Công kích" }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict,
            (await otherClient.PostAsJsonAsync(reportUrl, new { reason = "Spam" })).StatusCode);

        // Moderator: hai mục StoryRating của truyện; không lộ người gửi.
        var moderatorClient = factory.ClientFor(moderator);
        var queue = await AdminApi.GetDataAsync(moderatorClient, "/api/v1/ModerationQueue?itemType=StoryRating&pageSize=100");
        var items = queue.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("storyId").GetInt32() == storyId)
            .ToArray();
        Assert.Equal(2, items.Length);
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("createdByName").ValueKind));

        foreach (var itemId in items.Select(item => item.GetProperty("id").GetInt32()))
        {
            await AdminApi.PostDataAsync(moderatorClient, $"/api/v1/ModerationQueue/{itemId}/claim", new { }, HttpStatusCode.OK);
            var detail = await AdminApi.GetDataAsync(moderatorClient, $"/api/v1/StoryRatingModeration/{itemId}");
            Assert.DoesNotContain(otherParent.Username, detail.GetRawText());
            if (detail.GetProperty("holdSource").GetString() == "Screening")
            {
                await AdminApi.PostDataAsync(moderatorClient, $"/api/v1/StoryRatingModeration/{itemId}/approve",
                    new { }, HttpStatusCode.OK);
            }
            else
            {
                var report = Assert.Single(detail.GetProperty("reports").EnumerateArray().ToArray());
                Assert.Equal("Offensive", report.GetProperty("reason").GetString());
                await AdminApi.PostDataAsync(moderatorClient, $"/api/v1/StoryRatingModeration/{itemId}/hide",
                    new { reason = "Nhận xét không phù hợp" }, HttpStatusCode.OK);
            }
        }

        // Kết quả: đánh giá 1 sao đã hiện, đánh giá 5 sao bị ẩn kèm lý do.
        var after = await AdminApi.GetDataAsync(parentClient, url);
        Assert.Equal(1.0, after.GetProperty("summary").GetProperty("averageRating").GetDouble());
        Assert.Equal(1, after.GetProperty("summary").GetProperty("ratingCount").GetInt32());
        Assert.Equal("Hidden", after.GetProperty("myRating").GetProperty("status").GetString());
        Assert.Equal("Nhận xét không phù hợp", after.GetProperty("myRating").GetProperty("hiddenReason").GetString());

        var library = await AdminApi.GetDataAsync(parentClient, $"/api/v1/Story/community?authorUserId={creator.Id}");
        var row = Assert.Single(library.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal((1.0, 1), (row.GetProperty("averageRating").GetDouble(), row.GetProperty("ratingCount").GetInt32()));

        var creatorView = await AdminApi.GetDataAsync(factory.ClientFor(creator), $"/api/v1/creator/stories/{storyId}/ratings");
        Assert.Equal(1, creatorView.GetProperty("ratings").GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, creatorView.GetProperty("myRating").ValueKind);
    }

    [Fact]
    public async Task OnlyParentsWhoseChildReadTheStory_CanRate()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var reader = await factory.AddUserAsync(UserRole.User);
        var stranger = await factory.AddUserAsync(UserRole.User);
        var storyId = await SeedReadStoryAsync(creator, reader);
        var url = $"/api/v1/StoryRating/story/{storyId}";
        var body = new { rating = 4 };

        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientFor(stranger).PutAsJsonAsync($"{url}/me", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.ClientFor(creator).PutAsJsonAsync($"{url}/me", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
        Assert.False((await AdminApi.GetDataAsync(factory.ClientFor(stranger), url)).GetProperty("canRate").GetBoolean());
        Assert.Equal("Published", (await PutDataAsync(factory.ClientFor(reader), $"{url}/me", body)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task RatingModeration_IsModeratorOnly()
    {
        foreach (var role in new[] { UserRole.User, UserRole.ContentCreator, UserRole.Administrator })
        {
            var client = await factory.ClientForNewUserAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/StoryRatingModeration/1")).StatusCode);
        }
    }

    private static async Task<JsonElement> PutDataAsync(HttpClient client, string url, object body)
    {
        var response = await client.PutAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    /// <summary>Một truyện Creator công khai; mỗi <paramref name="readers"/> có một bé đã đọc truyện.</summary>
    private Task<int> SeedReadStoryAsync(UserAccount author, params UserAccount[] readers) =>
        factory.WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var story = new Story
            {
                AuthorUserId = author.Id, Title = "Cá heo tốt bụng", Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = now.AddDays(-3), Language = "vi", AgeBand = "6-8"
            };
            db.Add(story);
            await db.SaveChangesAsync();

            foreach (var reader in readers)
            {
                var child = new ChildProfile
                {
                    OwnerUserId = reader.Id, Nickname = "Mây", AgeBand = AgeBand.Age_6_8, Language = "vi",
                    Status = ChildProfileStatus.Active
                };
                // Mỗi phiên đọc phải có đúng một nguồn vào; ở đây là phụ huynh bàn giao thiết bị.
                var handover = new RefreshToken
                {
                    UserAccountId = reader.Id, TokenHash = $"rating-handover-{Guid.NewGuid():N}",
                    SessionScope = SessionScope.Supervisor, IssuedAt = now.AddHours(-1), ExpiresAt = now.AddDays(7)
                };
                db.AddRange(child, handover);
                await db.SaveChangesAsync();
                db.Add(new ReadingSession
                {
                    StoryId = story.Id, ChildProfileId = child.Id, Status = SessionStatus.Completed,
                    StartedAt = now.AddMinutes(-30), SupervisorSessionId = handover.Id
                });
                await db.SaveChangesAsync();
            }

            return story.Id;
        });
}
