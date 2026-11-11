using System.Net;
using System.Text.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// F-20, F-21 qua HTTP thật: Creator xem lượt đọc và thu nhập dự kiến của truyện mình; không xem được của người khác.
/// </summary>
public sealed class CreatorStatisticsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Creator_SeesOwnReadingAndEstimatedEarnings_ButNotAnotherCreators()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var otherCreator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var now = DateTime.UtcNow;
        var (storyId, otherStoryId) = await factory.WithDbAsync(async db =>
        {
            Story Published(UserAccount author, string title) => new()
            {
                AuthorUserId = author.Id, Title = title, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = now.AddDays(-30), Language = "vi", AgeBand = "6-8"
            };
            var mine = Published(creator, "Chú ếch nhỏ");
            var theirs = Published(otherCreator, "Truyện của người khác");
            var child = new ChildProfile { OwnerUserId = parent.Id, Nickname = "Mây", AgeBand = AgeBand.Age_6_8, Language = "vi", Status = ChildProfileStatus.Active };
            var secondChild = new ChildProfile { OwnerUserId = parent.Id, Nickname = "Gió", AgeBand = AgeBand.Age_6_8, Language = "vi", Status = ChildProfileStatus.Active };
            db.AddRange(mine, theirs, child, secondChild);
            // Doanh thu Gói Sáng tạo 100.000 đ trong kỳ → quỹ Creator 10.000 đ; cũng là gói trả phí của phụ huynh.
            var payment = new PaymentTransaction
            {
                PlanId = 3, PayerUserId = parent.Id, TransactionCode = "SEPAYCRSTATHTTP", Amount = 100000, AiCredits = 4,
                Status = PaymentStatus.Paid, PaidAt = now.AddMinutes(-30), ExpiresAt = now
            };
            db.Add(payment);
            // Mỗi phiên đọc phải có đúng một nguồn vào; ở đây là phụ huynh bàn giao thiết bị.
            var handover = new RefreshToken
            {
                UserAccountId = parent.Id, TokenHash = "creator-stats-handover", SessionScope = SessionScope.Supervisor,
                IssuedAt = now.AddHours(-1), ExpiresAt = now.AddDays(7)
            };
            db.Add(handover);
            await db.SaveChangesAsync();
            db.Add(new UserSubscription
            {
                UserId = parent.Id, PlanId = 3, Status = SubscriptionStatus.Active, StartAt = now.AddDays(-1), EndAt = now.AddDays(29), PaymentId = payment.Id
            });
            ReadingSession Completed(Story story, ChildProfile reader) => new()
            {
                StoryId = story.Id, ChildProfileId = reader.Id, Status = SessionStatus.Completed,
                StartedAt = now.AddMinutes(-20), CompletedAt = now.AddMinutes(-10), TimeSpentSeconds = 600,
                SupervisorSessionId = handover.Id
            };
            db.AddRange(
                Completed(mine, child),
                Completed(theirs, child), Completed(theirs, secondChild), Completed(theirs, child),   // trùng trẻ, truyện, ngày: tính 2
                new ReadingSession { StoryId = mine.Id, ChildProfileId = secondChild.Id, Status = SessionStatus.Abandoned, StartedAt = now.AddMinutes(-5), SupervisorSessionId = handover.Id },
                new ReadingProgress { StoryId = mine.Id, ChildProfileId = child.Id, IsFavorited = true });
            await db.SaveChangesAsync();
            return (mine.Id, theirs.Id);
        });
        var client = factory.ClientFor(creator);
        var day = now.AddMinutes(-10).ToString("yyyy-MM-dd");
        var period = $"periodStart={now.AddDays(-1):yyyy-MM-dd}&periodEnd={now:yyyy-MM-dd}";

        var reading = await AdminApi.GetDataAsync(client, $"/api/v1/CreatorStatistics/reading?{period}");
        var row = Assert.Single(reading.GetProperty("stories").EnumerateArray());
        Assert.Equal(
            (storyId, 2, 1, 1, 1, 0.5m),
            (row.GetProperty("storyId").GetInt32(), row.GetProperty("views").GetInt32(), row.GetProperty("favorites").GetInt32(),
                row.GetProperty("completedReadings").GetInt32(), row.GetProperty("validReads").GetInt32(),
                row.GetProperty("completionRate").GetDecimal()));
        // Chỉ có số tổng hợp: không lộ mã hay tên hồ sơ trẻ.
        Assert.DoesNotContain("childProfile", reading.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mây", reading.GetRawText());

        var revenue = await AdminApi.GetDataAsync(client, $"/api/v1/CreatorStatistics/revenue?{period}");
        // Quỹ 10.000 đ; 1 lượt của mình trên tổng 3 lượt → 3.333 đ (làm tròn xuống).
        Assert.Equal(
            (10000L, 3, 1, 3333L),
            (revenue.GetProperty("creatorFundVnd").GetInt64(), revenue.GetProperty("totalValidReadsAllCreators").GetInt32(),
                revenue.GetProperty("myValidReads").GetInt32(), revenue.GetProperty("estimatedEarningsVnd").GetInt64()));
        Assert.Equal([storyId], revenue.GetProperty("stories").EnumerateArray().Select(item => item.GetProperty("storyId").GetInt32()));

        // Truyện của Creator khác → 403 ở cả hai endpoint.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/CreatorStatistics/reading?{period}&storyId={otherStoryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/CreatorStatistics/revenue?{period}&storyId={otherStoryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/CreatorStatistics/reading?{period}&storyId=987654")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/CreatorStatistics/reading?periodStart={day}&periodEnd={now.AddDays(-9):yyyy-MM-dd}")).StatusCode);

        // Creator kia thấy phần của mình: 2 lượt → 6.666 đ.
        var theirs = await AdminApi.GetDataAsync(factory.ClientFor(otherCreator), $"/api/v1/CreatorStatistics/revenue?{period}");
        Assert.Equal((2, 6666L), (theirs.GetProperty("myValidReads").GetInt32(), theirs.GetProperty("estimatedEarningsVnd").GetInt64()));
    }

    [Theory]
    [InlineData("/api/v1/CreatorStatistics/reading?periodStart=2026-11-01&periodEnd=2026-11-30")]
    [InlineData("/api/v1/CreatorStatistics/revenue?periodStart=2026-11-01&periodEnd=2026-11-30")]
    public async Task CreatorStatistics_AreContentCreatorOnly(string url)
    {
        foreach (var role in new[] { UserRole.User, UserRole.Moderator, UserRole.Administrator })
        {
            var client = await factory.ClientForNewUserAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/BusinessReport/admin/revenue?periodStart=2026-11-01&periodEnd=2026-11-30")]
    [InlineData("/api/v1/Payment/admin/transactions")]
    public async Task Creator_CannotCallAdminStatistics(string url)
    {
        var client = await factory.ClientForNewUserAsync(UserRole.ContentCreator);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }
}
