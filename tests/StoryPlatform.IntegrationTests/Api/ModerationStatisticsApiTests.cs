using System.Net;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// F-19 qua HTTP thật (WF-05): Moderator xem thống kê hàng đợi và SLA; vai trò khác bị 403;
/// Moderator không gọi được thống kê doanh thu / thanh toán của Admin và thống kê nội dung của Creator.
/// </summary>
public sealed class ModerationStatisticsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Moderator_SeesQueueAndOwnDecisionStatistics_WithoutAnyContent()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var otherModerator = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var now = DateTime.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            ModerationItem Item(ModerationItemType type, ModerationItemStatus status) => new()
            {
                ItemType = type, Status = status, CreatedByUserId = creator.Id, SlaDueAt = now.AddHours(4),
                Feedback = "nhan xet khong duoc lo"
            };
            var overdue = Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending);
            overdue.SlaDueAt = now.AddHours(-2);
            var resubmission = Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Pending);
            resubmission.IsResubmission = true;
            var mine = Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Approved);
            mine.DecidedByUserId = moderator.Id;
            mine.DecidedAt = now.AddMinutes(-5);
            var theirs = Item(ModerationItemType.PublicStoryReview, ModerationItemStatus.Rejected);
            theirs.DecidedByUserId = otherModerator.Id;
            theirs.DecidedAt = now.AddMinutes(-5);
            db.AddRange(
                overdue, resubmission, mine, theirs,
                Item(ModerationItemType.GrayZoneStory, ModerationItemStatus.Claimed));
            await db.SaveChangesAsync();
        });
        var period = $"periodStart={now.AddDays(-1):yyyy-MM-dd}&periodEnd={now:yyyy-MM-dd}";

        var stats = await AdminApi.GetDataAsync(factory.ClientFor(moderator), $"/api/v1/ModerationQueue/statistics?{period}");

        // Hàng đợi dùng chung giữa các test của factory nên số hàng đợi chỉ kiểm tra mức tối thiểu; phần của riêng mình phải đúng.
        Assert.True(stats.GetProperty("pending").GetInt32() >= 2);
        Assert.True(stats.GetProperty("claimed").GetInt32() >= 1);
        Assert.True(stats.GetProperty("overdue").GetInt32() >= 1);
        Assert.Equal(
            (1, 0, 0),
            (stats.GetProperty("myApproved").GetInt32(), stats.GetProperty("myRejected").GetInt32(),
                stats.GetProperty("myChangesRequested").GetInt32()));
        Assert.True(stats.GetProperty("resubmissionCount").GetInt32() >= 1);
        Assert.True(stats.GetProperty("grayZoneCount").GetInt32() >= 1);
        var raw = stats.GetRawText();
        foreach (var forbidden in new[] { "feedback", "nhan xet", "childProfile", "createdBy", "storyId" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await factory.ClientFor(moderator).GetAsync(
            $"/api/v1/ModerationQueue/statistics?periodStart={now:yyyy-MM-dd}&periodEnd={now.AddDays(-3):yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Statistics_AreModeratorOnly()
    {
        const string url = "/api/v1/ModerationQueue/statistics?periodStart=2026-11-01&periodEnd=2026-11-30";
        foreach (var role in new[] { UserRole.ContentCreator, UserRole.User, UserRole.Administrator })
        {
            var client = await factory.ClientForNewUserAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/BusinessReport/admin/revenue?periodStart=2026-11-01&periodEnd=2026-11-30")]
    [InlineData("/api/v1/Payment/admin/transactions")]
    [InlineData("/api/v1/CreatorStatistics/content?periodStart=2026-11-01&periodEnd=2026-11-30")]
    public async Task Moderator_CannotCallAdminOrCreatorStatistics(string url)
    {
        var client = await factory.ClientForNewUserAsync(UserRole.Moderator);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }
}
