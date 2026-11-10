using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Luồng 2 đợt 1 qua HTTP thật: xem gói → tạo giao dịch → webhook SePay → chu kỳ gói và sổ lượt → lịch sử.
/// Gói Đọc (2), Gói Sáng tạo (3) và Add-on (4) đến từ seed HasData.
/// </summary>
public sealed class SubscriptionPurchaseApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int ReadingPlanId = 2;
    private const int CreativePlanId = 3;
    private const int AddOnPlanId = 4;

    [Fact]
    public async Task Parent_BuysCreativeThenAddOn_GetsCycleAndCredits_AndSeesOnlyOwnHistory()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var client = factory.ClientFor(parent);

        var plans = await GetDataAsync(client, "/api/v1/Payment/plans");
        Assert.Equal(
            ["Reading:49000:0:True", "Creative:99000:4:True", "AddOn:29000:2:False"],
            plans.EnumerateArray().Select(plan =>
                $"{plan.GetProperty("planType").GetString()}:{plan.GetProperty("priceVnd").GetInt32()}:" +
                $"{plan.GetProperty("aiCredits").GetInt32()}:{plan.GetProperty("isPurchasable").GetBoolean()}"));

        // Chưa có gói thì chưa mua được Add-on.
        Assert.Equal(HttpStatusCode.Conflict, (await CreateTransactionAsync(client, AddOnPlanId)).StatusCode);

        var creative = await CreatePendingAsync(client, CreativePlanId);
        Assert.Equal((99000, "Pending"), (creative.GetProperty("amount").GetInt32(), creative.GetProperty("status").GetString()));
        // Mỗi tài khoản chỉ có 1 giao dịch gói chờ thanh toán.
        Assert.Equal(HttpStatusCode.Conflict, (await CreateTransactionAsync(client, ReadingPlanId)).StatusCode);

        await SendWebhookAsync(creative, 99000, "FT-CREATIVE");

        var quota = await GetDataAsync(client, "/api/v1/TokenQuota/me");
        Assert.Equal("Creative", quota.GetProperty("plan").GetProperty("planType").GetString());
        Assert.Equal((4, 0, 4), Credits(quota));
        var cycleEnd = quota.GetProperty("plan").GetProperty("endAt").GetDateTime();
        Assert.Equal(
            quota.GetProperty("plan").GetProperty("startAt").GetDateTime().AddMonths(1), cycleEnd);

        var addOn = await CreatePendingAsync(client, AddOnPlanId);
        await SendWebhookAsync(addOn, 29000, "FT-ADDON");

        quota = await GetDataAsync(client, "/api/v1/TokenQuota/me");
        Assert.Equal((4, 2, 6), Credits(quota));
        Assert.Equal(cycleEnd, quota.GetProperty("plan").GetProperty("endAt").GetDateTime());

        // Webhook gửi lại cho giao dịch đã Paid không cộng thêm.
        await SendWebhookAsync(creative, 99000, "FT-CREATIVE");
        await SendWebhookAsync(addOn, 29000, "FT-ADDON");
        Assert.Equal((4, 2, 6), Credits(await GetDataAsync(client, "/api/v1/TokenQuota/me")));

        var history = await GetDataAsync(client, "/api/v1/Payment/transactions/me");
        Assert.Equal(2, history.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            ["AddOn:Paid", "Creative:Paid"],
            history.GetProperty("items").EnumerateArray().Select(item =>
                $"{item.GetProperty("planType").GetString()}:{item.GetProperty("status").GetString()}"));
        Assert.All(
            history.GetProperty("items").EnumerateArray(),
            item => Assert.Equal(JsonValueKind.Null, item.GetProperty("qrCodeUrl").ValueKind));

        var otherParent = await factory.ClientForNewUserAsync(UserRole.User);
        var otherHistory = await GetDataAsync(otherParent, "/api/v1/Payment/transactions/me");
        Assert.Equal(0, otherHistory.GetProperty("totalCount").GetInt32());

        await factory.WithDbAsync(async db =>
        {
            var subscription = await db.UserSubscriptions.SingleAsync(item => item.UserId == parent.Id);
            Assert.Equal((SubscriptionStatus.Active, CreativePlanId), (subscription.Status, subscription.PlanId));
            var lots = await db.CreditLots.Where(lot => lot.UserId == parent.Id).OrderBy(lot => lot.Id).ToListAsync();
            Assert.Equal(
                [(CreditSource.Cycle, 4, (DateTime?)subscription.EndAt), (CreditSource.AddOn, 2, (DateTime?)subscription.EndAt)],
                lots.Select(lot => (lot.Source, lot.Remaining, lot.ExpiresAt)));
        });
    }

    [Fact]
    public async Task Parent_RenewsEarly_NextCycleIsScheduledRightAfterCurrentOne()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var client = factory.ClientFor(parent);
        await SendWebhookAsync(await CreatePendingAsync(client, ReadingPlanId), 49000, "FT-READ-1");

        await SendWebhookAsync(await CreatePendingAsync(client, ReadingPlanId), 49000, "FT-READ-2");

        var quota = await GetDataAsync(client, "/api/v1/TokenQuota/me");
        var plan = quota.GetProperty("plan");
        var next = quota.GetProperty("nextPlan");
        Assert.Equal(("Reading", "Reading"), (plan.GetProperty("planType").GetString(), next.GetProperty("planType").GetString()));
        Assert.Equal(plan.GetProperty("endAt").GetDateTime(), next.GetProperty("startAt").GetDateTime());
        Assert.Equal(0, quota.GetProperty("totalRemaining").GetInt32());
    }

    [Fact]
    public async Task Parent_UpgradesReadingToCreative_TakesEffectImmediately()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var client = factory.ClientFor(parent);
        await SendWebhookAsync(await CreatePendingAsync(client, ReadingPlanId), 49000, "FT-UP-1");

        await SendWebhookAsync(await CreatePendingAsync(client, CreativePlanId), 99000, "FT-UP-2");

        var quota = await GetDataAsync(client, "/api/v1/TokenQuota/me");
        Assert.Equal("Creative", quota.GetProperty("plan").GetProperty("planType").GetString());
        Assert.Equal(JsonValueKind.Null, quota.GetProperty("nextPlan").ValueKind);
        Assert.Equal((4, 0, 4), Credits(quota));
        await factory.WithDbAsync(async db => Assert.Equal(
            [SubscriptionStatus.Superseded, SubscriptionStatus.Active],
            await db.UserSubscriptions.Where(item => item.UserId == parent.Id).OrderBy(item => item.Id)
                .Select(item => item.Status).ToListAsync()));
    }

    [Fact]
    public async Task WrongAmount_LeavesAccountOnFreeTier()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var client = factory.ClientFor(parent);
        var creative = await CreatePendingAsync(client, CreativePlanId);

        await SendWebhookAsync(creative, 90000, "FT-MISMATCH");

        var quota = await GetDataAsync(client, "/api/v1/TokenQuota/me");
        Assert.Equal(JsonValueKind.Null, quota.GetProperty("plan").ValueKind);
        Assert.Equal(0, quota.GetProperty("totalRemaining").GetInt32());
        var history = await GetDataAsync(client, "/api/v1/Payment/transactions/me");
        Assert.Equal("MismatchAmount", history.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.ContentCreator)]
    public async Task MyQuota_IsParentOnly(UserRole role)
    {
        var client = await factory.ClientForNewUserAsync(role);

        var response = await client.GetAsync("/api/v1/TokenQuota/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemovedQuotaConfigEndpoints_AreGone()
    {
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);

        var response = await admin.GetAsync("/api/v1/TokenQuota/config");

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    private static (int Cycle, int AddOn, int Total) Credits(JsonElement quota) => (
        quota.GetProperty("cycleCredits").GetInt32(),
        quota.GetProperty("addOnCredits").GetInt32(),
        quota.GetProperty("totalRemaining").GetInt32());

    private static Task<HttpResponseMessage> CreateTransactionAsync(HttpClient client, int planId) =>
        client.PostAsJsonAsync("/api/v1/Payment/transactions", new { planId });

    private static async Task<JsonElement> CreatePendingAsync(HttpClient client, int planId)
    {
        var response = await CreateTransactionAsync(client, planId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task SendWebhookAsync(JsonElement transaction, int amount, string referenceCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Payment/webhook/sepay")
        {
            Content = JsonContent.Create(new
            {
                content = $"CK {transaction.GetProperty("transactionCode").GetString()} thanh toan",
                transferAmount = amount,
                referenceCode
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Apikey {ApiFactory.WebhookApiKey}");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
