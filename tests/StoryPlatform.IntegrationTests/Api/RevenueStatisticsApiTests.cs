using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// F-15 qua HTTP thật: mua gói → Admin xem doanh thu của kỳ → tạo và publish báo cáo kỳ.
/// Dùng host riêng để số liệu của kỳ chỉ gồm giao dịch do lớp test này tạo.
/// </summary>
public sealed class RevenueStatisticsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;
    private const int AddOnPlanId = 4;

    [Fact]
    public async Task Admin_SeesPaidRevenueForThePeriod_ThenGeneratesAndPublishesTheReport()
    {
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var firstParent = await factory.AddUserAsync(UserRole.User);
        var first = factory.ClientFor(firstParent);
        var second = await factory.ClientForNewUserAsync(UserRole.User);
        var third = await factory.ClientForNewUserAsync(UserRole.User);

        // Phụ huynh 1: Gói Sáng tạo rồi Add-on, đều qua webhook. Phụ huynh 2: Gói Đọc, Admin xác nhận tay.
        await PayByWebhookAsync(await CreateAsync(first, CreativePlanId), 99000);
        await PayByWebhookAsync(await CreateAsync(first, AddOnPlanId), 29000);
        var reading = await CreateAsync(second, ReadingPlanId);
        await AdminApi.PostDataAsync(admin, $"/api/v1/Payment/transactions/{reading.GetProperty("id").GetInt32()}/mark-paid", new { }, HttpStatusCode.OK);
        // Phụ huynh 3: chuyển sai số tiền (không tính doanh thu) và một giao dịch còn chờ.
        await PayByWebhookAsync(await CreateAsync(third, CreativePlanId), 90000);
        await CreateAsync(third, CreativePlanId);
        // Admin cấp 3 lượt rồi trừ 1 lượt của phụ huynh 1.
        await AdminApi.PostDataAsync(admin, "/api/v1/TokenQuota/admin/adjustments", new { userId = firstParent.Id, amount = 3, reason = "Bù lỗi" }, HttpStatusCode.OK);
        await AdminApi.PostDataAsync(admin, "/api/v1/TokenQuota/admin/adjustments", new { userId = firstParent.Id, amount = -1, reason = "Cấp nhầm" }, HttpStatusCode.OK);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var revenue = await AdminApi.GetDataAsync(admin, $"/api/v1/BusinessReport/admin/revenue?periodStart={today}&periodEnd={today}");

        Assert.Equal((177000L, 3), (revenue.GetProperty("totalRevenueVnd").GetInt64(), revenue.GetProperty("paidTransactionCount").GetInt32()));
        var byStatus = revenue.GetProperty("transactionsByStatus");
        Assert.Equal(
            (3, 1, 0, 1),
            (byStatus.GetProperty("paid").GetInt32(), byStatus.GetProperty("pending").GetInt32(),
                byStatus.GetProperty("expired").GetInt32(), byStatus.GetProperty("mismatchAmount").GetInt32()));
        Assert.Equal(
            ["Reading:1:49000", "Creative:1:99000", "AddOn:1:29000"],
            revenue.GetProperty("revenueByPlanType").EnumerateArray().Select(row =>
                $"{row.GetProperty("planType").GetString()}:{row.GetProperty("transactionCount").GetInt32()}:{row.GetProperty("revenueVnd").GetInt64()}"));
        Assert.Equal((2, 1), (revenue.GetProperty("buyingParents").GetInt32(), revenue.GetProperty("repeatBuyingParents").GetInt32()));
        Assert.Equal(
            (1, 49000L),
            (revenue.GetProperty("manuallyMarkedPaidCount").GetInt32(), revenue.GetProperty("manuallyMarkedPaidRevenueVnd").GetInt64()));
        // Cấp: 4 lượt chu kỳ + 2 lượt Add-on + 3 lượt Admin cấp. Dùng: 1 lượt Admin trừ.
        Assert.Equal((9, 1), (revenue.GetProperty("creditsGranted").GetInt32(), revenue.GetProperty("creditsUsed").GetInt32()));
        // Quỹ Creator = 30% × 49.000 + 10% × 99.000.
        Assert.Equal(24600L, revenue.GetProperty("creatorFundVnd").GetInt64());

        var generated = await AdminApi.PostDataAsync(admin, "/api/v1/BusinessReport/generate", new { periodStart = today, periodEnd = today }, HttpStatusCode.Created);
        Assert.Equal(("Compiling", 177000L), (generated.GetProperty("status").GetString(), generated.GetProperty("revenue").GetProperty("totalRevenueVnd").GetInt64()));
        var reportId = generated.GetProperty("id").GetInt32();

        // Kỳ chồng lên báo cáo đã có bị từ chối.
        var yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/v1/BusinessReport/generate", new { periodStart = yesterday, periodEnd = today })).StatusCode);

        var published = await AdminApi.PostDataAsync(admin, $"/api/v1/BusinessReport/{reportId}/publish", new { }, HttpStatusCode.OK);
        Assert.Equal("Published", published.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/v1/BusinessReport/{reportId}/publish", new { })).StatusCode);

        // Giao dịch đến sau không làm đổi báo cáo đã chụp, nhưng số liệu trực tiếp thì có.
        await PayByWebhookAsync(await CreateAsync(second, AddOnPlanId), 29000);
        var stored = await AdminApi.GetDataAsync(admin, $"/api/v1/BusinessReport/admin/{reportId}");
        Assert.Equal(177000L, stored.GetProperty("revenue").GetProperty("totalRevenueVnd").GetInt64());
        var live = await AdminApi.GetDataAsync(admin, $"/api/v1/BusinessReport/admin/revenue?periodStart={today}&periodEnd={today}");
        Assert.Equal(206000L, live.GetProperty("totalRevenueVnd").GetInt64());

        var list = await AdminApi.GetDataAsync(admin, "/api/v1/BusinessReport/admin");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt32() == reportId);

        // Phụ huynh xem được báo cáo mới nhất nhưng không thấy doanh thu.
        var forParents = await AdminApi.GetDataAsync(first, "/api/v1/BusinessReport/latest-published");
        Assert.Equal(reportId, forParents.GetProperty("id").GetInt32());
        Assert.False(forParents.TryGetProperty("revenue", out _));
    }

    [Fact]
    public async Task Admin_SeesAccountStatisticsForThePeriod_AndTheyAreSnapshottedIntoTheReport()
    {
        // Host riêng của lớp test: mọi tài khoản phụ huynh ở đây đều được tạo hôm nay.
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        await factory.AddUserAsync(UserRole.User);
        await factory.AddUserAsync(UserRole.User, status: AccountStatus.Suspended);
        await factory.AddUserAsync(UserRole.ContentCreator);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var url = $"/api/v1/BusinessReport/admin/accounts?periodStart={today}&periodEnd={today}";

        var before = await AdminApi.GetDataAsync(admin, url);
        // Một phụ huynh đăng nhập thật (được cấp refresh token) → tính là hoạt động trong kỳ.
        var signedInParent = await factory.AddUserAsync(UserRole.User);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login", new { identifier = signedInParent.Username, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var after = await AdminApi.GetDataAsync(admin, url);

        var total = before.GetProperty("totalParentAccounts").GetInt32();
        Assert.Equal(total, before.GetProperty("newAccounts").GetInt32());
        Assert.True(before.GetProperty("lockedOrDisabledAccounts").GetInt32() >= 1);
        Assert.Equal(total + 1, after.GetProperty("totalParentAccounts").GetInt32());
        Assert.Equal(before.GetProperty("activeAccounts").GetInt32() + 1, after.GetProperty("activeAccounts").GetInt32());

        var tomorrow = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var none = await AdminApi.GetDataAsync(admin, $"/api/v1/BusinessReport/admin/accounts?periodStart={tomorrow}&periodEnd={tomorrow}");
        Assert.Equal((0, 0), (none.GetProperty("newAccounts").GetInt32(), none.GetProperty("activeAccounts").GetInt32()));

        // Kỳ "ngày mai" chưa có báo cáo nào trong host này nên không chồng lên báo cáo của test khác.
        var generated = await AdminApi.PostDataAsync(admin, "/api/v1/BusinessReport/generate", new { periodStart = tomorrow, periodEnd = tomorrow }, HttpStatusCode.Created);
        Assert.Equal(
            after.GetProperty("totalParentAccounts").GetInt32(),
            generated.GetProperty("accounts").GetProperty("totalParentAccounts").GetInt32());
    }

    [Theory]
    [InlineData("/api/v1/BusinessReport/admin/revenue?periodStart=2026-11-01&periodEnd=2026-11-30")]
    [InlineData("/api/v1/BusinessReport/admin/accounts?periodStart=2026-11-01&periodEnd=2026-11-30")]
    [InlineData("/api/v1/BusinessReport/admin")]
    [InlineData("/api/v1/BusinessReport/admin/1")]
    public async Task RevenueEndpoints_AreAdministratorOnly(string url)
    {
        foreach (var role in new[] { UserRole.User, UserRole.Moderator, UserRole.ContentCreator })
        {
            var client = await factory.ClientForNewUserAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Revenue_PeriodEndBeforeStart_Returns400()
    {
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);

        var response = await admin.GetAsync("/api/v1/BusinessReport/admin/revenue?periodStart=2026-11-30&periodEnd=2026-11-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<JsonElement> CreateAsync(HttpClient client, int planId) =>
        AdminApi.PostDataAsync(client, "/api/v1/Payment/transactions", new { planId }, HttpStatusCode.Created);

    private async Task PayByWebhookAsync(JsonElement transaction, int amount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Payment/webhook/sepay")
        {
            Content = JsonContent.Create(new
            {
                content = $"CK {transaction.GetProperty("transactionCode").GetString()}",
                transferAmount = amount,
                referenceCode = $"FT-{transaction.GetProperty("id").GetInt32()}"
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Apikey {ApiFactory.WebhookApiKey}");
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }
}
