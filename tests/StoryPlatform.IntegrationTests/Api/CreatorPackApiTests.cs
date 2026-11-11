using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Luồng 2, 2f qua HTTP thật: Creator chỉ thấy và chỉ mua được Gói Creator; khi Paid được cộng lượt AI và lượt gửi duyệt.
/// </summary>
public sealed class CreatorPackApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int ReadingPlanId = 2;
    private const int CreatorPackPlanId = 5;

    [Fact]
    public async Task Creator_BuysTheCreatorPack_AndSeesMonthlyPlusPurchasedCredits()
    {
        var creator = factory.ClientFor(await factory.AddUserAsync(UserRole.ContentCreator));

        var plans = await AdminApi.GetDataAsync(creator, "/api/v1/Payment/plans");
        var pack = Assert.Single(plans.EnumerateArray());
        Assert.Equal(
            (CreatorPackPlanId, "CreatorPack", "Creator", 99000, 6, 20),
            (pack.GetProperty("id").GetInt32(), pack.GetProperty("planType").GetString(), pack.GetProperty("audience").GetString(),
                pack.GetProperty("priceVnd").GetInt32(), pack.GetProperty("aiCredits").GetInt32(), pack.GetProperty("submitCredits").GetInt32()));

        // Trước khi mua: chỉ có hạn mức tháng.
        var before = await AdminApi.GetDataAsync(creator, "/api/v1/TokenQuota/creator/me");
        Assert.Equal((2, 5), (Total(before, "aiCredits"), Total(before, "submitCredits")));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await creator.PostAsJsonAsync("/api/v1/Payment/transactions", new { planId = ReadingPlanId })).StatusCode);

        var transaction = await AdminApi.PostDataAsync(creator, "/api/v1/Payment/transactions", new { planId = CreatorPackPlanId }, HttpStatusCode.Created);
        await SendWebhookAsync(transaction, 99000);
        await SendWebhookAsync(transaction, 99000);   // gửi lại: không cộng hai lần

        var after = await AdminApi.GetDataAsync(creator, "/api/v1/TokenQuota/creator/me");
        Assert.Equal(
            (2, 6, 8, 5, 20, 25),
            (after.GetProperty("aiCredits").GetProperty("monthlyRemaining").GetInt32(),
                after.GetProperty("aiCredits").GetProperty("purchasedRemaining").GetInt32(), Total(after, "aiCredits"),
                after.GetProperty("submitCredits").GetProperty("monthlyRemaining").GetInt32(),
                after.GetProperty("submitCredits").GetProperty("purchasedRemaining").GetInt32(), Total(after, "submitCredits")));
        Assert.True(after.GetProperty("monthlyAllowanceResetsAt").GetDateTime() > DateTime.UtcNow);

        var history = await AdminApi.GetDataAsync(creator, "/api/v1/Payment/transactions/me");
        Assert.Equal("CreatorPack:Paid",
            $"{history.GetProperty("items")[0].GetProperty("planType").GetString()}:{history.GetProperty("items")[0].GetProperty("status").GetString()}");
    }

    [Fact]
    public async Task Parent_NeitherSeesNorBuysTheCreatorPack()
    {
        var parent = await factory.ClientForNewUserAsync(UserRole.User);

        var plans = await AdminApi.GetDataAsync(parent, "/api/v1/Payment/plans");
        Assert.DoesNotContain(plans.EnumerateArray(), plan => plan.GetProperty("planType").GetString() == "CreatorPack");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await parent.PostAsJsonAsync("/api/v1/Payment/transactions", new { planId = CreatorPackPlanId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/v1/TokenQuota/creator/me")).StatusCode);
    }

    [Fact]
    public async Task CreatorNotYetApproved_CannotBuyTheCreatorPack()
    {
        var pending = await factory.AddUserAsync(UserRole.ContentCreator, status: AccountStatus.Registered);

        var response = await factory.ClientFor(pending).PostAsJsonAsync("/api/v1/Payment/transactions", new { planId = CreatorPackPlanId });

        // Tài khoản chưa Active bị chặn ngay ở bước xác thực (401); dịch vụ thanh toán còn tự chặn thêm (403).
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(0, await factory.WithDbAsync(db => db.PaymentTransactions.CountAsync(item => item.PayerUserId == pending.Id)));
    }

    private static int Total(JsonElement quota, string kind) =>
        quota.GetProperty(kind).GetProperty("totalRemaining").GetInt32();

    private async Task SendWebhookAsync(JsonElement transaction, int amount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Payment/webhook/sepay")
        {
            Content = JsonContent.Create(new
            {
                content = $"CK {transaction.GetProperty("transactionCode").GetString()}",
                transferAmount = amount,
                referenceCode = $"FT-CP-{transaction.GetProperty("id").GetInt32()}"
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Apikey {ApiFactory.WebhookApiKey}");
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().SendAsync(request)).StatusCode);
    }
}
