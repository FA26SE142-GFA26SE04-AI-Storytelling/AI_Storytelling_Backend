using System.Net;
using System.Text.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Thư viện cộng đồng qua HTTP thật: lượt xem / yêu thích của từng truyện, sắp xếp theo hai số này,
/// và danh sách truyện đã đăng của chính Creator.
/// </summary>
public sealed class StoryCommunityApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Library_ShowsViewAndFavoriteCounts_AndSortsByThem()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var (popularId, lovedId) = await SeedTwoStoriesAsync(creator, parent);
        var client = factory.ClientFor(parent);
        var mine = $"authorUserId={creator.Id}";

        var byViews = await AdminApi.GetDataAsync(client, $"/api/v1/Story/community?{mine}&orderBy=views&isDescending=true");
        Assert.Equal(
            new[] { (popularId, 2, 0), (lovedId, 1, 2) },
            Rows(byViews));
        // Chỉ có số tổng hợp: không lộ mã hay tên hồ sơ trẻ.
        Assert.DoesNotContain("childProfile", byViews.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mây", byViews.GetRawText());

        var byFavorites = await AdminApi.GetDataAsync(client, $"/api/v1/Story/community?{mine}&orderBy=favorites&isDescending=true");
        Assert.Equal(new[] { lovedId, popularId }, Rows(byFavorites).Select(row => row.Id));

        // Mặc định vẫn theo ngày tạo tăng dần: truyện thêm trước (popular) đứng trước.
        var byDefault = await AdminApi.GetDataAsync(client, $"/api/v1/Story/community?{mine}");
        Assert.Equal(new[] { popularId, lovedId }, Rows(byDefault).Select(row => row.Id));
    }

    [Fact]
    public async Task Creator_SeesOnlyOwnPublishedStories_WithCounts()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var otherCreator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var (popularId, lovedId) = await SeedTwoStoriesAsync(creator, parent);
        await SeedTwoStoriesAsync(otherCreator, parent);
        await factory.WithDbAsync(async db =>
        {
            // Bản nháp của chính Creator: không được xuất hiện.
            db.Add(new Story
            {
                AuthorUserId = creator.Id, Title = "Bản nháp", Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Draft, Language = "vi", AgeBand = "6-8"
            });
            await db.SaveChangesAsync();
        });

        // authorUserId của người khác trên query string bị bỏ qua.
        var data = await AdminApi.GetDataAsync(factory.ClientFor(creator),
            $"/api/v1/creator/stories/published?authorUserId={otherCreator.Id}&orderBy=favorites&isDescending=true");

        Assert.Equal(new[] { (lovedId, 1, 2), (popularId, 2, 0) }, Rows(data));
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreatorPublishedList_IsContentCreatorOnly()
    {
        const string url = "/api/v1/creator/stories/published";
        foreach (var role in new[] { UserRole.User, UserRole.Moderator, UserRole.Administrator })
        {
            var client = await factory.ClientForNewUserAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    /// <summary>
    /// Hai truyện công khai của <paramref name="author"/>: "popular" có 2 phiên đọc, 0 yêu thích;
    /// "loved" (tạo sau) có 1 phiên đọc, 2 trẻ yêu thích.
    /// </summary>
    private Task<(int PopularId, int LovedId)> SeedTwoStoriesAsync(UserAccount author, UserAccount parent) =>
        factory.WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            Story Published(string title) => new()
            {
                AuthorUserId = author.Id, Title = title, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = now.AddDays(-30),
                Language = "vi", AgeBand = "6-8"
            };
            // ApplicationDbContext tự gán CreatedAt = UtcNow khi thêm: lưu "popular" riêng trước để nó chắc chắn cũ hơn.
            var popular = Published("Chú ếch nhỏ");
            db.Add(popular);
            await db.SaveChangesAsync();
            var loved = Published("Cô mèo con");
            var child = new ChildProfile { OwnerUserId = parent.Id, Nickname = "Mây", AgeBand = AgeBand.Age_6_8, Language = "vi", Status = ChildProfileStatus.Active };
            var secondChild = new ChildProfile { OwnerUserId = parent.Id, Nickname = "Gió", AgeBand = AgeBand.Age_6_8, Language = "vi", Status = ChildProfileStatus.Active };
            // Mỗi phiên đọc phải có đúng một nguồn vào; ở đây là phụ huynh bàn giao thiết bị.
            var handover = new RefreshToken
            {
                UserAccountId = parent.Id, TokenHash = $"community-handover-{Guid.NewGuid():N}", SessionScope = SessionScope.Supervisor,
                IssuedAt = now.AddHours(-1), ExpiresAt = now.AddDays(7)
            };
            db.AddRange(loved, child, secondChild, handover);
            await db.SaveChangesAsync();

            ReadingSession Opened(Story story, ChildProfile reader, SessionStatus status) => new()
            {
                StoryId = story.Id, ChildProfileId = reader.Id, Status = status,
                StartedAt = now.AddMinutes(-20), SupervisorSessionId = handover.Id
            };
            db.AddRange(
                Opened(popular, child, SessionStatus.Completed),
                Opened(popular, secondChild, SessionStatus.Abandoned),
                Opened(loved, child, SessionStatus.Started),
                new ReadingProgress { StoryId = loved.Id, ChildProfileId = child.Id, IsFavorited = true },
                new ReadingProgress { StoryId = loved.Id, ChildProfileId = secondChild.Id, IsFavorited = true },
                new ReadingProgress { StoryId = popular.Id, ChildProfileId = child.Id, IsFavorited = false });
            await db.SaveChangesAsync();
            return (popular.Id, loved.Id);
        });

    private static (int Id, int ViewCount, int FavoriteCount)[] Rows(JsonElement page) =>
        page.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetInt32(), item.GetProperty("viewCount").GetInt32(),
                item.GetProperty("favoriteCount").GetInt32()))
            .ToArray();
}
