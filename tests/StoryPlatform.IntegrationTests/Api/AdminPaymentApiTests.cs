using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Payments.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Việc của Admin trong Luồng 2 qua HTTP thật: xem và xác nhận tay giao dịch, cấp / trừ lượt, xử lý ngoại lệ đối soát.
/// </summary>
public sealed class AdminPaymentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;

    [Fact]
    public async Task Admin_FindsPendingTransactionAndMarksPaid_PlanActivatesWithoutWebhook()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var parentClient = factory.ClientFor(parent);
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var created = await AdminApi.PostDataAsync(parentClient, "/api/v1/Payment/transactions", new { planId = CreativePlanId }, HttpStatusCode.Created);
        var code = created.GetProperty("transactionCode").GetString()!;

        // Tìm theo một phần mã, không phân biệt hoa thường; lọc trạng thái Pending.
        var found = await AdminApi.GetDataAsync(admin, $"/api/v1/Payment/admin/transactions?status=Pending&search={code[5..].ToLowerInvariant()}");
        var row = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal(
            (code, "Pending", parent.Id, parent.Email, "Creative"),
            (row.GetProperty("transactionCode").GetString(), row.GetProperty("status").GetString(),
                row.GetProperty("payerUserId").GetInt32(), row.GetProperty("payerEmail").GetString(),
                row.GetProperty("planType").GetString()));

        // Tìm theo email người mua cũng ra; ký tự % được hiểu theo nghĩa đen nên không khớp gì.
        var byEmail = await AdminApi.GetDataAsync(admin, $"/api/v1/Payment/admin/transactions?search={Uri.EscapeDataString(parent.Email.ToUpperInvariant())}");
        Assert.Equal(1, byEmail.GetProperty("totalCount").GetInt32());
        var wildcard = await AdminApi.GetDataAsync(admin, "/api/v1/Payment/admin/transactions?search=%25");
        Assert.Equal(0, wildcard.GetProperty("totalCount").GetInt32());

        await AdminApi.PostDataAsync(admin, $"/api/v1/Payment/transactions/{row.GetProperty("id").GetInt32()}/mark-paid", new { }, HttpStatusCode.OK);

        var quota = await AdminApi.GetDataAsync(parentClient, "/api/v1/TokenQuota/me");
        Assert.Equal("Creative", quota.GetProperty("plan").GetProperty("planType").GetString());
        Assert.Equal(4, quota.GetProperty("cycleCredits").GetInt32());
        var seenByAdmin = await AdminApi.GetDataAsync(admin, $"/api/v1/TokenQuota/admin/users/{parent.Id}");
        Assert.Equal(4, seenByAdmin.GetProperty("totalRemaining").GetInt32());
    }

    [Fact]
    public async Task Admin_GrantsThenDeductsCredits_ParentSeesBalance_AndHistoryIsKept()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var parentClient = factory.ClientFor(parent);
        var adminUser = await factory.AddUserAsync(UserRole.Administrator);
        var admin = factory.ClientFor(adminUser);
        const string url = "/api/v1/TokenQuota/admin/adjustments";

        var granted = await AdminApi.PostDataAsync(admin, url, new { userId = parent.Id, amount = 3, reason = "Bù lỗi sinh truyện" }, HttpStatusCode.OK);
        Assert.Equal((3, 3, adminUser.Id), (granted.GetProperty("amount").GetInt32(), granted.GetProperty("totalRemainingAfter").GetInt32(), granted.GetProperty("adminId").GetInt32()));
        var quota = await AdminApi.GetDataAsync(parentClient, "/api/v1/TokenQuota/me");
        Assert.Equal((3, 3), (quota.GetProperty("otherCredits").GetInt32(), quota.GetProperty("totalRemaining").GetInt32()));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url, new { userId = parent.Id, amount = 1, reason = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url, new { userId = parent.Id, amount = -4, reason = "Trừ quá số còn lại" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(url, new { userId = adminUser.Id, amount = 1, reason = "Không phải phụ huynh" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(url, new { userId = 987654, amount = 1, reason = "Không tồn tại" })).StatusCode);

        var deducted = await AdminApi.PostDataAsync(admin, url, new { userId = parent.Id, amount = -2, reason = "Thu hồi lượt cấp nhầm" }, HttpStatusCode.OK);
        Assert.Equal(1, deducted.GetProperty("totalRemainingAfter").GetInt32());
        Assert.Equal(1, (await AdminApi.GetDataAsync(parentClient, "/api/v1/TokenQuota/me")).GetProperty("totalRemaining").GetInt32());

        var history = await AdminApi.GetDataAsync(admin, $"{url}?userId={parent.Id}");
        Assert.Equal(
            ["-2:Thu hồi lượt cấp nhầm", "3:Bù lỗi sinh truyện"],
            history.GetProperty("items").EnumerateArray().Select(item =>
                $"{item.GetProperty("amount").GetInt32()}:{item.GetProperty("reason").GetString()}"));
        await factory.WithDbAsync(async db => Assert.Equal(
            2, await db.AuditLogs.CountAsync(log => log.Action == "CreditAdjusted" && log.ActorUserId == adminUser.Id)));
    }

    [Fact]
    public async Task Admin_ResolvesReconciliationException_PlanApplied_SecondResolveIs409()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        // Giao dịch đã Paid nhưng bước áp gói chưa chạy (ví dụ lỗi tạm thời lúc webhook về).
        var transaction = await factory.WithDbAsync(async db =>
        {
            var paid = new PaymentTransaction
            {
                PlanId = ReadingPlanId, PayerUserId = parent.Id, TransactionCode = "SEPAYRECONHTTP1", Amount = 49000,
                AiCredits = 0, Status = PaymentStatus.Paid, PaidAt = DateTime.UtcNow.AddHours(-1),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-45)
            };
            db.PaymentTransactions.Add(paid);
            await db.SaveChangesAsync();
            return paid;
        });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>().DetectAsync(DateTime.UtcNow);
        }

        var open = await AdminApi.GetDataAsync(admin, "/api/v1/Payment/admin/reconciliation");
        var exception = Assert.Single(
            open.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("transaction").GetProperty("id").GetInt32() == transaction.Id);
        Assert.Equal(("Open", "NotApplied"), (exception.GetProperty("status").GetString(), exception.GetProperty("reason").GetString()));
        var resolveUrl = $"/api/v1/Payment/admin/reconciliation/{exception.GetProperty("id").GetInt32()}/resolve";

        var resolved = await AdminApi.PostDataAsync(admin, resolveUrl, new { }, HttpStatusCode.OK);

        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(resolveUrl, new { })).StatusCode);
        var quota = await AdminApi.GetDataAsync(admin, $"/api/v1/TokenQuota/admin/users/{parent.Id}");
        Assert.Equal("Reading", quota.GetProperty("plan").GetProperty("planType").GetString());
        await factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.UserSubscriptions.CountAsync(item => item.PaymentId == transaction.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/Payment/admin/reconciliation?status=Closed")).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/Payment/admin/transactions")]
    [InlineData("GET", "/api/v1/Payment/admin/plans")]
    [InlineData("PUT", "/api/v1/Payment/admin/plans/2")]
    [InlineData("POST", "/api/v1/Payment/admin/plans")]
    [InlineData("GET", "/api/v1/Payment/admin/settings")]
    [InlineData("PUT", "/api/v1/Payment/admin/settings")]
    [InlineData("GET", "/api/v1/Payment/admin/reconciliation")]
    [InlineData("POST", "/api/v1/Payment/admin/reconciliation/1/resolve")]
    [InlineData("POST", "/api/v1/TokenQuota/admin/adjustments")]
    [InlineData("GET", "/api/v1/TokenQuota/admin/adjustments")]
    [InlineData("GET", "/api/v1/TokenQuota/admin/users/1")]
    public async Task AdminEndpoints_RejectParentsAndModerators(string method, string url)
    {
        foreach (var role in new[] { UserRole.User, UserRole.Moderator, UserRole.ContentCreator })
        {
            var client = await factory.ClientForNewUserAsync(role);
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method != "GET")
            {
                request.Content = JsonContent.Create(new { name = "x", priceVnd = 1, aiCredits = 0, isActive = true, userId = 1, amount = 1, reason = "x" });
            }

            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url))).StatusCode);
    }
}

/// <summary>Sửa gói dùng host riêng để không đổi giá của các lớp test khác.</summary>
public sealed class AdminPlanApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int CreativePlanId = 3;

    [Fact]
    public async Task Admin_UpdatesPlan_OldPendingKeepsOldPriceAndCredits_NewPurchaseUsesNewOnes()
    {
        var earlyParent = factory.ClientFor(await factory.AddUserAsync(UserRole.User));
        var lateParent = factory.ClientFor(await factory.AddUserAsync(UserRole.User));
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var before = await AdminApi.PostDataAsync(earlyParent, "/api/v1/Payment/transactions", new { planId = CreativePlanId }, HttpStatusCode.Created);

        var plans = await AdminApi.GetDataAsync(admin, "/api/v1/Payment/admin/plans");
        Assert.Equal(
            ["1:Legacy:False", "2:Reading:True", "3:Creative:True", "4:AddOn:True", "5:CreatorPack:True"],
            plans.EnumerateArray().Select(plan =>
                $"{plan.GetProperty("id").GetInt32()}:{plan.GetProperty("planType").GetString()}:{plan.GetProperty("isActive").GetBoolean()}"));

        var response = await admin.PutAsJsonAsync($"/api/v1/Payment/admin/plans/{CreativePlanId}",
            new { name = "Gói Sáng tạo", priceVnd = 120000, aiCredits = 6, isActive = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/Payment/admin/plans/1",
            new { name = "Personal", priceVnd = 49000, aiCredits = 50, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/v1/Payment/admin/plans/{CreativePlanId}",
            new { name = "Gói Sáng tạo", priceVnd = 0, aiCredits = 6, isActive = true })).StatusCode);

        var after = await AdminApi.PostDataAsync(lateParent, "/api/v1/Payment/transactions", new { planId = CreativePlanId }, HttpStatusCode.Created);
        Assert.Equal((99000, 120000), (before.GetProperty("amount").GetInt32(), after.GetProperty("amount").GetInt32()));

        // Xác nhận tay cả hai: giao dịch cũ nhận 4 lượt (số chốt lúc tạo), giao dịch mới nhận 6.
        await AdminApi.PostDataAsync(admin, $"/api/v1/Payment/transactions/{before.GetProperty("id").GetInt32()}/mark-paid", new { }, HttpStatusCode.OK);
        await AdminApi.PostDataAsync(admin, $"/api/v1/Payment/transactions/{after.GetProperty("id").GetInt32()}/mark-paid", new { }, HttpStatusCode.OK);
        Assert.Equal(4, (await AdminApi.GetDataAsync(earlyParent, "/api/v1/TokenQuota/me")).GetProperty("cycleCredits").GetInt32());
        Assert.Equal(6, (await AdminApi.GetDataAsync(lateParent, "/api/v1/TokenQuota/me")).GetProperty("cycleCredits").GetInt32());

        var forBuyers = await AdminApi.GetDataAsync(lateParent, "/api/v1/Payment/plans");
        Assert.Equal(120000, forBuyers.EnumerateArray().Single(plan => plan.GetProperty("id").GetInt32() == CreativePlanId).GetProperty("priceVnd").GetInt32());
        await factory.WithDbAsync(async db => Assert.Equal(
            1, await db.AuditLogs.CountAsync(log => log.Action == "SubscriptionPlanUpdated" && log.EntityId == CreativePlanId)));
    }
}

internal static class AdminApi
{
    public static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    public static async Task<JsonElement> PostDataAsync(HttpClient client, string url, object body, HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync(url, body);
        Assert.Equal(expected, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
