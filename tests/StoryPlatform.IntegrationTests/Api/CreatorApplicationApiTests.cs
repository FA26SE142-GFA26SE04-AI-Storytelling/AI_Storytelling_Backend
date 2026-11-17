using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Luồng 5, M5 qua HTTP thật (JWT, middleware, phân quyền): Creator tự đăng ký, chờ duyệt thì không vào được API Creator,
/// Admin duyệt thì token role tạm bị thu hồi và phiên làm mới có role ContentCreator.
/// </summary>
public sealed class CreatorApplicationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Demo@12345";

    [Fact]
    public async Task SelfRegisteredCreator_WaitsForApproval_ThenGetsCreatorAccessAfterApproval()
    {
        var (applicant, refreshToken) = await RegisterVerifyAndLoginAsync("creator_signup_ok");

        // Chờ duyệt: đăng nhập được, xem được đơn, nhưng mọi API Creator trả 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.GetAsync("/api/v1/TokenQuota/creator/me")).StatusCode);
        var mine = await AdminApi.GetDataAsync(applicant, "/api/v1/creator-applications/me");
        Assert.Equal("Pending", mine.GetProperty("status").GetString());
        var applicationId = mine.GetProperty("id").GetInt32();

        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);
        var pending = await AdminApi.GetDataAsync(admin, "/api/v1/admin/creator-applications?status=Pending");
        Assert.Contains(pending.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt32() == applicationId);
        await AdminApi.PostDataAsync(admin, $"/api/v1/admin/creator-applications/{applicationId}/approve", new { }, HttpStatusCode.OK);

        // Token role tạm không còn khớp tài khoản đã duyệt: bị thu hồi.
        Assert.Equal(HttpStatusCode.Unauthorized, (await applicant.GetAsync("/api/v1/creator-applications/me")).StatusCode);

        var refreshed = await AdminApi.PostDataAsync(
            factory.CreateClient(), "/api/v1/Auth/refresh-token", new { refreshToken }, HttpStatusCode.OK);
        var creator = Bearer(refreshed.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await creator.GetAsync("/api/v1/TokenQuota/creator/me")).StatusCode);
        Assert.Contains(factory.Emails.Sent, sent => sent.To == "creator_signup_ok@example.test" && sent.Kind == "creator-approved");
    }

    [Fact]
    public async Task RejectedCreator_SeesTheReasonAndResubmits()
    {
        var (applicant, _) = await RegisterVerifyAndLoginAsync("creator_signup_no");
        var applicationId = (await AdminApi.GetDataAsync(applicant, "/api/v1/creator-applications/me")).GetProperty("id").GetInt32();
        var admin = await factory.ClientForNewUserAsync(UserRole.Administrator);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/creator-applications/{applicationId}/reject", new { reason = "" })).StatusCode);
        await AdminApi.PostDataAsync(admin, $"/api/v1/admin/creator-applications/{applicationId}/reject",
            new { reason = "Chưa có tác phẩm mẫu" }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/creator-applications/{applicationId}/approve", new { })).StatusCode);

        // Bị từ chối: token role tạm vẫn dùng được để xem lý do và nộp lại.
        var mine = await AdminApi.GetDataAsync(applicant, "/api/v1/creator-applications/me");
        Assert.Equal(("Rejected", "Chưa có tác phẩm mẫu"),
            (mine.GetProperty("status").GetString(), mine.GetProperty("rejectionReason").GetString()));
        var resubmitted = await AdminApi.PostDataAsync(applicant, "/api/v1/creator-applications/me/resubmit",
            new { introduction = "Đã bổ sung ba truyện mẫu về tình bạn cho trẻ 6–8 tuổi." }, HttpStatusCode.OK);
        Assert.Equal("Pending", resubmitted.GetProperty("status").GetString());
        Assert.NotEqual(applicationId, resubmitted.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task OnlyAdministrators_ReviewApplications_AndUnverifiedCreatorsCannotLogIn()
    {
        var register = await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/register-creator", Body("creator_unverified"));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().PostAsJsonAsync("/api/v1/Auth/login",
            new { identifier = "creator_unverified", password = Password })).StatusCode);

        var moderator = await factory.ClientForNewUserAsync(UserRole.Moderator);
        Assert.Equal(HttpStatusCode.Forbidden, (await moderator.GetAsync("/api/v1/admin/creator-applications")).StatusCode);
        var parent = await factory.ClientForNewUserAsync(UserRole.User);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/v1/creator-applications/me")).StatusCode);
    }

    private static object Body(string username) => new
    {
        username, email = $"{username}@example.test", fullName = "Tác Giả Mới", password = Password,
        confirmPassword = Password, introduction = "Tôi viết truyện thiếu nhi về thiên nhiên đã ba năm."
    };

    private async Task<(HttpClient Client, string RefreshToken)> RegisterVerifyAndLoginAsync(string username)
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/v1/Auth/register-creator", Body(username))).StatusCode);
        var email = $"{username}@example.test";
        var otp = factory.Emails.Sent.Last(sent => sent.To == email && sent.Kind == "verify").Secret;
        Assert.Equal(HttpStatusCode.OK,
            (await anonymous.PostAsJsonAsync("/api/v1/Auth/verify-email", new { email, otp })).StatusCode);

        var login = await AdminApi.PostDataAsync(anonymous, "/api/v1/Auth/login",
            new { identifier = username, password = Password }, HttpStatusCode.OK);
        Assert.Equal(("ContentCreator", "AwaitingApproval"),
            (login.GetProperty("user").GetProperty("role").GetString(), login.GetProperty("user").GetProperty("status").GetString()));
        return (Bearer(login.GetProperty("accessToken").GetString()!), login.GetProperty("refreshToken").GetString()!);
    }

    private HttpClient Bearer(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
