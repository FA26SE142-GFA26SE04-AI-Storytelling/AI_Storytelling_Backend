using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Common.Security;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.ChildProfiles.ReadingTime;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Đồng hồ ứng dụng đứng ở 12:00 giờ Việt Nam hôm nay: gần giờ thật (JWT vẫn hợp lệ) nhưng cố định.</summary>
public sealed class ReadingTimeApiFactory : ApiFactory
{
    public static readonly DateTime NoonVietnamUtc = VietnamTime.StartOfDayUtc(DateTime.UtcNow).AddHours(12);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(descriptor => descriptor.ServiceType == typeof(TimeProvider));
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(NoonVietnamUtc));
        });
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}

public sealed class ReadingScheduleApiTests(ReadingTimeApiFactory factory) : IClassFixture<ReadingTimeApiFactory>
{
    private static string Url(int childProfileId) => $"/api/v1/ReadingSchedule/{childProfileId}";

    private static object Body(
        bool enabled, string? bedtime = null, bool screenLimitEnabled = false, int screenMinutes = 60,
        params (string DayType, string Start, string End)[] windows) => new
    {
        enabled,
        bedtimeWeekday = bedtime,
        bedtimeWeekend = bedtime,
        breakEnabled = true,
        breakAfterMinutes = 25,
        breakDurationMinutes = 5,
        breakRemindEyes = true,
        breakRemindWater = false,
        breakRemindMove = true,
        screenLimitEnabled,
        screenLimitWeekdayMinutes = screenMinutes,
        screenLimitWeekendMinutes = screenMinutes,
        windows = windows.Select(window => new { dayType = window.DayType, startTime = window.Start, endTime = window.End }).ToArray()
    };

    private static string[] Describe(JsonElement data) => data.GetProperty("windows").EnumerateArray()
        .Select(window => $"{window.GetProperty("dayType").GetString()} {window.GetProperty("slot").GetInt32()} " +
                          $"{window.GetProperty("startTime").GetString()}-{window.GetProperty("endTime").GetString()}")
        .ToArray();

    [Fact]
    public async Task Get_without_schedule_suggests_by_age_then_put_round_trips_and_replaces_windows()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        var client = factory.ClientFor(parent);

        var suggested = (await client.GetFromJsonAsync<JsonElement>(Url(child.Id))).GetProperty("data");
        Assert.True(suggested.GetProperty("isSuggested").GetBoolean());
        Assert.False(suggested.GetProperty("enabled").GetBoolean());
        Assert.Equal("20:30", suggested.GetProperty("bedtimeWeekday").GetString());
        Assert.Equal(45, suggested.GetProperty("screenLimitWeekdayMinutes").GetInt32());

        var first = await client.PutAsJsonAsync(Url(child.Id),
            Body(true, "21:00", true, 50, ("Weekend", "15:00", "20:00"), ("Weekend", "08:00", "11:00")));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Lưu cấu hình thời gian đọc thành công.",
            (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());

        var saved = (await client.GetFromJsonAsync<JsonElement>(Url(child.Id))).GetProperty("data");
        Assert.False(saved.GetProperty("isSuggested").GetBoolean());
        Assert.Equal("21:00", saved.GetProperty("bedtimeWeekend").GetString());
        Assert.False(saved.GetProperty("breakRemindWater").GetBoolean());
        Assert.True(saved.GetProperty("screenLimitEnabled").GetBoolean());
        Assert.Equal(50, saved.GetProperty("screenLimitWeekendMinutes").GetInt32());
        Assert.Equal(new[] { "Weekend 1 08:00-11:00", "Weekend 2 15:00-20:00" }, Describe(saved));

        var second = await client.PutAsJsonAsync(Url(child.Id),
            Body(true, null, false, 60, ("Weekend", "08:00", "09:00"), ("Weekday", "18:00", "20:00")));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var replaced = (await client.GetFromJsonAsync<JsonElement>(Url(child.Id))).GetProperty("data");
        Assert.Equal(JsonValueKind.Null, replaced.GetProperty("bedtimeWeekday").ValueKind);
        Assert.Equal(new[] { "Weekday 1 18:00-20:00", "Weekend 1 08:00-09:00" }, Describe(replaced));
    }

    [Fact]
    public async Task Put_invalid_values_return_400_with_message()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        var client = factory.ClientFor(parent);

        var overlap = await client.PutAsJsonAsync(Url(child.Id),
            Body(true, null, false, 60, ("Weekday", "07:00", "09:00"), ("Weekday", "08:30", "10:00")));
        var bedtime = await client.PutAsJsonAsync(Url(child.Id), Body(true, "17:00"));

        Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
        Assert.Equal("Hai khung giờ ngày thường đang chồng nhau.",
            (await overlap.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bedtime.StatusCode);
        Assert.Equal("Giờ đi ngủ phải từ 18:00 đến 23:30.",
            (await bedtime.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Other_parent_moderator_and_anonymous_are_rejected()
    {
        var owner = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, owner.Id);

        var otherParent = await factory.ClientForNewUserAsync(UserRole.User);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherParent.GetAsync(Url(child.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherParent.PutAsJsonAsync(Url(child.Id), Body(false))).StatusCode);

        var moderator = await factory.ClientForNewUserAsync(UserRole.Moderator);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync(Url(child.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Url(child.Id))).StatusCode);
    }

    private const string EasyLoginUrl = "/api/v1/ChildAccessCredential/easylogin/login";

    private async Task<(UserAccount Parent, ChildProfile Child, HttpClient ParentClient)> ChildWithQrAsync(string secret)
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        await factory.WithDbAsync(db =>
        {
            db.ChildAccessCredentials.Add(new ChildAccessCredential
            {
                ChildProfileId = child.Id, AvatarId = "avatar-fox", EasyLoginSecretHash = TokenHasher.Hash(secret),
                EasyLoginCreatedAt = DateTime.UtcNow, CreatedByUserId = parent.Id
            });
            return Task.CompletedTask;
        });
        return (parent, child, factory.ClientFor(parent));
    }

    private static async Task AssertBlockedAsync(HttpResponseMessage response, string message, string code)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(message, body.GetProperty("message").GetString());
        Assert.Contains(code, body.GetProperty("errors").EnumerateArray().Select(error => error.GetString()));
    }

    [Fact]
    public async Task Easy_login_outside_window_is_blocked_then_allowed_and_me_reports_reading_time()
    {
        var (_, child, parentClient) = await ChildWithQrAsync("qr-reading-time");
        // Đồng hồ ứng dụng đứng ở 12:00 giờ Việt Nam; khung 06:00–07:00 mọi ngày nên bé đang ngoài giờ.
        Assert.Equal(HttpStatusCode.OK, (await parentClient.PutAsJsonAsync(Url(child.Id),
            Body(true, null, false, 60, ("Weekday", "06:00", "07:00"), ("Weekend", "06:00", "07:00")))).StatusCode);
        var anonymous = factory.CreateClient();

        await AssertBlockedAsync(await anonymous.PostAsJsonAsync(EasyLoginUrl, new { secret = "qr-reading-time" }),
            ReadingSchedulePolicy.ChildOutsideMessage, ReadingSchedulePolicy.OutsideErrorCode);

        Assert.Equal(HttpStatusCode.OK, (await parentClient.PutAsJsonAsync(Url(child.Id),
            Body(true, null, true, 60, ("Weekday", "06:00", "23:00"), ("Weekend", "06:00", "23:00")))).StatusCode);
        var login = await anonymous.PostAsJsonAsync(EasyLoginUrl, new { secret = "qr-reading-time" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();

        var childClient = factory.CreateClient();
        childClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = (await childClient.GetFromJsonAsync<JsonElement>("/api/v1/ChildAccessCredential/me")).GetProperty("data");
        var readingTime = me.GetProperty("readingTime");
        Assert.Equal("Allowed", readingTime.GetProperty("state").GetString());
        Assert.Equal(60, readingTime.GetProperty("screenMinutesLimit").GetInt32());
    }

    [Fact]
    public async Task Child_token_cannot_read_or_change_reading_schedule()
    {
        var (_, child, _) = await ChildWithQrAsync("qr-child-forbidden");
        var login = await factory.CreateClient().PostAsJsonAsync(EasyLoginUrl, new { secret = "qr-child-forbidden" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString();

        var childClient = factory.CreateClient();
        childClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await childClient.GetAsync(Url(child.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await childClient.PutAsJsonAsync(Url(child.Id), Body(false))).StatusCode);
    }

    [Fact]
    public async Task Easy_login_after_screen_limit_used_up_is_blocked()
    {
        var (_, child, parentClient) = await ChildWithQrAsync("qr-screen-limit");
        Assert.Equal(HttpStatusCode.OK, (await parentClient.PutAsJsonAsync(Url(child.Id), Body(true, null, true, 10))).StatusCode);
        var today = DateOnly.FromDateTime(VietnamTime.LocalDate(ReadingTimeApiFactory.NoonVietnamUtc));
        await factory.WithDbAsync(async db =>
        {
            var schedule = await db.ReadingSchedules.SingleAsync(value => value.ChildProfileId == child.Id);
            schedule.UsageDate = today;
            schedule.UsageSeconds = 600;
        });

        await AssertBlockedAsync(
            await factory.CreateClient().PostAsJsonAsync(EasyLoginUrl, new { secret = "qr-screen-limit" }),
            ReadingSchedulePolicy.ChildScreenLimitMessage, ReadingSchedulePolicy.ScreenLimitErrorCode);
    }
}
