using System.Net;
using System.Net.Http.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Luồng 5, 5h qua HTTP thật (JWT, phân quyền, EF trên SQLite): Creator nhập thông tin nhận tiền, Admin thấy Creator trong
/// danh sách chi, đánh dấu Đã chi rồi huỷ lần chi; số dư là tổng các bút toán.
/// </summary>
public sealed class CreatorPayoutApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Creator_EntersPayoutAccount_AdminMarksPaidThenCancels()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        await SeedEarningAsync(creator.Id, 655_000);
        var creatorClient = factory.ClientFor(creator);
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);

        // Chưa nhập thông tin nhận tiền: có lời nhắc, chưa có trong danh sách chi.
        var mine = await AdminApi.GetDataAsync(creatorClient, "/api/v1/creator-payouts/me");
        Assert.Equal(655_000, mine.GetProperty("balanceVnd").GetInt64());
        Assert.False(mine.GetProperty("hasPayoutAccount").GetBoolean());
        Assert.False(string.IsNullOrEmpty(mine.GetProperty("reminder").GetString()));
        Assert.DoesNotContain((await AdminApi.GetDataAsync(admin, "/api/v1/admin/creator-payouts/due")).EnumerateArray(),
            row => row.GetProperty("creatorUserId").GetInt32() == creator.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await creatorClient.PutAsJsonAsync("/api/v1/creator-payouts/me/account",
            new { bankName = "Vietcombank", accountNumber = "12ab", accountHolderName = "LE VAN A" })).StatusCode);
        var saved = await creatorClient.PutAsJsonAsync("/api/v1/creator-payouts/me/account",
            new { bankName = "Vietcombank", accountNumber = "0011 0000 12345", accountHolderName = "LE VAN A" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var due = (await AdminApi.GetDataAsync(admin, "/api/v1/admin/creator-payouts/due")).EnumerateArray()
            .Single(row => row.GetProperty("creatorUserId").GetInt32() == creator.Id);
        Assert.Equal(655_000, due.GetProperty("balanceVnd").GetInt64());
        Assert.Equal("0011000012345", due.GetProperty("accountNumber").GetString());

        var today = DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/creator-payouts/creators/{creator.Id}/mark-paid",
            new { amountVnd = 655_000, bankTransactionCode = "", transferredOn = today })).StatusCode);
        var paid = await AdminApi.PostDataAsync(admin, $"/api/v1/admin/creator-payouts/creators/{creator.Id}/mark-paid",
            new { amountVnd = 655_000, bankTransactionCode = "FT26321999", transferredOn = today }, HttpStatusCode.OK);
        var payoutId = paid.GetProperty("payoutId").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/creator-payouts/creators/{creator.Id}/mark-paid",
            new { amountVnd = 655_000, bankTransactionCode = "FT26321999", transferredOn = today })).StatusCode);

        mine = await AdminApi.GetDataAsync(creatorClient, "/api/v1/creator-payouts/me");
        Assert.Equal(0, mine.GetProperty("balanceVnd").GetInt64());
        var history = Assert.Single(mine.GetProperty("payouts").EnumerateArray());
        Assert.Equal(("FT26321999", 655_000L),
            (history.GetProperty("bankTransactionCode").GetString(), history.GetProperty("amountVnd").GetInt64()));

        await AdminApi.PostDataAsync(admin, $"/api/v1/admin/creator-payouts/{payoutId}/cancel",
            new { reason = "Ngân hàng trả lại tiền" }, HttpStatusCode.OK);
        var overview = await AdminApi.GetDataAsync(admin, $"/api/v1/admin/creator-payouts/creators/{creator.Id}");
        Assert.Equal(655_000, overview.GetProperty("balanceVnd").GetInt64());
        Assert.Equal("Cancelled", Assert.Single(overview.GetProperty("payouts").EnumerateArray()).GetProperty("status").GetString());
        Assert.True(await factory.WithDbAsync(db => Task.FromResult(
            db.AuditLogs.Count(log => log.Action == "CreatorPayoutMarkedPaid" || log.Action == "CreatorPayoutCancelled"
                                      || log.Action == "CreatorPayoutAccountUpdated") >= 3)));
    }

    [Fact]
    public async Task OnlyCreatorsSeeTheirOwnBalance_AndOnlyAdministratorsRecordPayouts()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var creatorClient = factory.ClientFor(creator);
        var moderator = await factory.ClientForNewUserAsync(UserRole.Moderator);
        var parent = await factory.ClientForNewUserAsync(UserRole.User);

        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/v1/creator-payouts/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creatorClient.GetAsync("/api/v1/admin/creator-payouts/due")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await creatorClient.GetAsync($"/api/v1/admin/creator-payouts/creators/{creator.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.PostAsJsonAsync(
            $"/api/v1/admin/creator-payouts/creators/{creator.Id}/mark-paid",
            new { amountVnd = 1, bankTransactionCode = "X", transferredOn = "2026-11-17" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/v1/creator-payouts/me")).StatusCode);
    }

    private Task SeedEarningAsync(int creatorUserId, long amount) =>
        factory.WithDbAsync(async db =>
        {
            db.CreatorBalanceEntries.Add(new CreatorBalanceEntry
            {
                CreatorUserId = creatorUserId, Type = CreatorBalanceEntryType.PeriodEarning, AmountVnd = amount, ValidReads = 30
            });
            await db.SaveChangesAsync();
        });
}
