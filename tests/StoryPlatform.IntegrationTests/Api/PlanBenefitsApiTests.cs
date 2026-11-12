using System.Net;
using System.Net.Http.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// F-17 qua HTTP thật: Admin tạo gói và cấu hình chung; quyền lợi theo gói được áp dụng.
/// </summary>
public sealed class PlanBenefitsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AdminCreatesPlan_ParentSeesItWithBenefits()
    {
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var created = await AdminApi.PostDataAsync(admin, "/api/v1/Payment/admin/plans", new
        {
            name = "Gói Đọc Gia đình", audience = "User", planType = "Reading", priceVnd = 69000, aiCredits = 0, isActive = true,
            benefits = new { maxChildProfiles = 6, weeklyNewLibraryStories = (int?)null, canCopyRoadmapTemplates = true, readAloudPerMonth = 90, writingAssistPerDay = 10 }
        }, HttpStatusCode.Created);
        var planId = created.GetProperty("id").GetInt32();

        var parent = await factory.ClientForNewUserAsync(UserRole.User);
        var plans = await AdminApi.GetDataAsync(parent, "/api/v1/Payment/plans");
        var plan = plans.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == planId);

        Assert.Equal((69000, 6, 10), (plan.GetProperty("priceVnd").GetInt32(),
            plan.GetProperty("benefits").GetProperty("maxChildProfiles").GetInt32(),
            plan.GetProperty("benefits").GetProperty("writingAssistPerDay").GetInt32()));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/Payment/admin/plans",
            new { name = "Sai", audience = "User", planType = "CreatorPack", priceVnd = 99000, aiCredits = 6 })).StatusCode);
    }

    private Task SeedProfilesAsync(int ownerId, params (string Nickname, int DaysOld)[] profiles) => factory.WithDbAsync(async db =>
    {
        foreach (var (nickname, daysOld) in profiles)
        {
            db.ChildProfiles.Add(new ChildProfile
            {
                OwnerUserId = ownerId, Nickname = nickname, AgeBand = AgeBand.Age_6_8, Language = "vi",
                Status = ChildProfileStatus.Active, CreatedAt = DateTime.UtcNow.AddDays(-daysOld)
            });
        }

        await db.SaveChangesAsync();
    });

    [Fact]
    public async Task FreeParent_SecondProfile_Returns409()
    {
        var user = await factory.AddUserAsync(UserRole.User);
        await SeedProfilesAsync(user.Id, ("Mây", 10));

        var response = await factory.ClientFor(user).PostAsJsonAsync("/api/v1/ChildProfile",
            new { nickname = "Gió", ageBand = (int)AgeBand.Age_6_8, language = "vi" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("tối đa 1 hồ sơ trẻ", (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("message").GetString());
    }

    [Fact]
    public async Task FreeParent_CopyTemplate_Returns403()
    {
        var parent = await factory.ClientForNewUserAsync(UserRole.User);

        var response = await parent.PostAsJsonAsync("/api/v1/RoadmapLibrary/1/copy", new { childProfileIds = new[] { 1 } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("sao chép bộ truyện mẫu", (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("message").GetString());
    }

    [Fact]
    public async Task FreeParent_QuotaShowsFreeBenefits()
    {
        var parent = await factory.ClientForNewUserAsync(UserRole.User);

        var quota = await AdminApi.GetDataAsync(parent, "/api/v1/TokenQuota/me");

        Assert.Equal(("Free", 1, 3, 0, 0), (quota.GetProperty("benefits").GetProperty("tier").GetString(),
            quota.GetProperty("benefits").GetProperty("maxChildProfiles").GetInt32(),
            quota.GetProperty("benefits").GetProperty("weeklyNewLibraryStories").GetInt32(),
            quota.GetProperty("newLibraryStoriesThisWeek").GetInt32(), quota.GetProperty("writingAssistsToday").GetInt32()));
    }

    [Fact]
    public async Task ExpiredParentWithTwoProfiles_OneIsViewOnly_SecondSwitchSameWeekReturns409()
    {
        // Hai hồ sơ tạo khi còn gói; gói đã hết hạn nên tài khoản đang ở mức Miễn phí (1 chỗ đọc).
        var user = await factory.AddUserAsync(UserRole.User);
        await SeedProfilesAsync(user.Id, ("Mây", 60), ("Gió", 10));
        await factory.WithDbAsync(async db =>
        {
            var payment = new PaymentTransaction
            {
                PlanId = 2, PayerUserId = user.Id, TransactionCode = $"SEPAYEXP{user.Id:D7}", Amount = 49000,
                Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow.AddDays(-40), ExpiresAt = DateTime.UtcNow.AddDays(-40)
            };
            db.Add(payment);
            await db.SaveChangesAsync();
            db.Add(new UserSubscription
            {
                UserId = user.Id, PlanId = 2, Status = SubscriptionStatus.Expired, PaymentId = payment.Id,
                StartAt = DateTime.UtcNow.AddDays(-40), EndAt = DateTime.UtcNow.AddDays(-10)
            });
            await db.SaveChangesAsync();
        });
        var parent = factory.ClientFor(user);

        var slots = await AdminApi.GetDataAsync(parent, "/api/v1/ChildProfile/reading-slots");
        var profiles = slots.GetProperty("profiles").EnumerateArray().ToList();
        Assert.Equal((1, true), (slots.GetProperty("slots").GetInt32(), slots.GetProperty("isOverLimit").GetBoolean()));
        var viewOnly = profiles.Single(profile => !profile.GetProperty("canRead").GetBoolean());
        Assert.Equal("Gió", viewOnly.GetProperty("nickname").GetString());

        var choose = await parent.PutAsJsonAsync("/api/v1/ChildProfile/reading-slots",
            new { childProfileIds = new[] { viewOnly.GetProperty("childProfileId").GetInt32() } });
        Assert.Equal(HttpStatusCode.OK, choose.StatusCode);
        var again = await parent.PutAsJsonAsync("/api/v1/ChildProfile/reading-slots",
            new { childProfileIds = new[] { profiles.Single(profile => profile.GetProperty("canRead").GetBoolean()).GetProperty("childProfileId").GetInt32() } });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task AdminChangesFundRate_RevenueReflectsIt()
    {
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var settings = await AdminApi.GetDataAsync(admin, "/api/v1/Payment/admin/settings");
        Assert.Equal(30, settings.GetProperty("readingFundPercent").GetInt32());
        var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(settings.GetRawText())!;
        body["readingFundPercent"] = 20;
        body["creativeFundPercent"] = 5;
        try
        {
            var update = await admin.PutAsJsonAsync("/api/v1/Payment/admin/settings", body);
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);

            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var revenue = await AdminApi.GetDataAsync(admin, $"/api/v1/BusinessReport/admin/revenue?periodStart={today}&periodEnd={today}");
            Assert.Equal((0.20m, 0.05m), (revenue.GetProperty("readingFundRate").GetDecimal(), revenue.GetProperty("creativeFundRate").GetDecimal()));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/Payment/admin/settings",
                new Dictionary<string, object?>(body) { ["minimumReadSeconds"] = 0 })).StatusCode);
        }
        finally
        {
            body["readingFundPercent"] = 30;
            body["creativeFundPercent"] = 10;
            await admin.PutAsJsonAsync("/api/v1/Payment/admin/settings", body);
        }
    }
}
