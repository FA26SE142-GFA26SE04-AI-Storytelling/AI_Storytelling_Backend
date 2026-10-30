using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using static StoryPlatform.IntegrationTests.Api.AuthApiTests;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class ChildProfileApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object NewChild(string nickname = "Bé Na") => new { nickname, ageBand = "Age_6_8", language = "vi" };

    [Fact]
    public async Task Create_User_Returns201AsDraftOwnedByCaller()
    {
        var parent = await factory.AddUserAsync(UserRole.User);

        var response = await factory.ClientFor(parent).PostAsJsonAsync("/api/v1/ChildProfile", NewChild());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await ReadJsonAsync(response)).GetProperty("data");
        Assert.Equal("Draft", data.GetProperty("status").GetString());
        var id = data.GetProperty("id").GetInt32();
        Assert.Equal(parent.Id, await factory.WithDbAsync(async db => (await db.ChildProfiles.FindAsync(id))!.OwnerUserId));
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/ChildProfile", NewChild());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.ContentCreator)]
    [InlineData(UserRole.Administrator)]
    public async Task Create_NonUserRole_Returns403(UserRole role)
    {
        var response = await (await factory.ClientForNewUserAsync(role)).PostAsJsonAsync("/api/v1/ChildProfile", NewChild());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmptyNickname_Returns400()
    {
        var response = await (await factory.ClientForNewUserAsync(UserRole.User)).PostAsJsonAsync("/api/v1/ChildProfile", NewChild(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Biệt danh của trẻ không được để trống.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Activate_OwnerWithPoliciesAndConsent_Returns200Active()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id, ChildProfileStatus.Draft);

        var response = await factory.ClientFor(parent).PatchAsync($"/api/v1/ChildProfile/{child.Id}/activate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active", (await ReadJsonAsync(response)).GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Activate_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PatchAsync("/api/v1/ChildProfile/1/activate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Activate_UserNotChildOwner_Returns403()
    {
        var owner = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, owner.Id, ChildProfileStatus.Draft);

        var response = await (await factory.ClientForNewUserAsync(UserRole.User))
            .PatchAsync($"/api/v1/ChildProfile/{child.Id}/activate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ChildProfileStatus.Draft, await factory.WithDbAsync(async db => (await db.ChildProfiles.FindAsync(child.Id))!.Status));
    }

    [Fact]
    public async Task Activate_ModeratorRole_Returns403()
    {
        var owner = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, owner.Id, ChildProfileStatus.Draft);

        var response = await (await factory.ClientForNewUserAsync(UserRole.Moderator))
            .PatchAsync($"/api/v1/ChildProfile/{child.Id}/activate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Activate_WithoutSafetyPolicy_Returns400()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id, ChildProfileStatus.Draft, withPolicies: false);

        var response = await factory.ClientFor(parent).PatchAsync($"/api/v1/ChildProfile/{child.Id}/activate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Hồ sơ trẻ chưa có Learning Profile không thể kích hoạt.",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Activate_UnknownChild_Returns404()
    {
        var response = await (await factory.ClientForNewUserAsync(UserRole.User)).PatchAsync("/api/v1/ChildProfile/987654/activate", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class AIStoryInputApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object Submit(int childProfileId, string key = "api-submit-0001", string topic = "Tình bạn") => new
    {
        childProfileId, idempotencyKey = key, topic, lesson = "Biết giúp đỡ bạn bè", vocabularyLevel = "level_2",
        characterMode = "ai_suggested", settingMode = "ai_suggested", targetLength = 500
    };

    [Fact]
    public async Task Submit_ChildOwner_Returns200AcceptedAndQueuesOutline()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);

        var response = await factory.ClientFor(parent).PostAsJsonAsync("/api/v1/ai-story-input/requests", Submit(child.Id));

        Assert.True(response.IsSuccessStatusCode, $"Status was {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var data = (await ReadJsonAsync(response)).GetProperty("data");
        Assert.Equal("input_accepted", data.GetProperty("inputStatus").GetString());
        var storyId = data.GetProperty("storyId").GetInt32();
        Assert.True(await factory.WithDbAsync(db => db.Set<StoryGenerationJob>().AnyAsync(
            job => job.StoryId == storyId && job.Operation == GenerationJobOperation.GenerateOutline)));
    }

    [Fact]
    public async Task Submit_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/ai-story-input/requests", Submit(1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Submit_ContentCreatorRole_Returns403()
    {
        var response = await (await factory.ClientForNewUserAsync(UserRole.ContentCreator))
            .PostAsJsonAsync("/api/v1/ai-story-input/requests", Submit(1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Submit_UserNotChildOwner_Returns403AndCreatesNoStory()
    {
        var owner = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, owner.Id);

        var response = await (await factory.ClientForNewUserAsync(UserRole.User))
            .PostAsJsonAsync("/api/v1/ai-story-input/requests", Submit(child.Id, "api-submit-0002"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Stories.AnyAsync(story => story.ChildProfileId == child.Id)));
    }

    [Fact]
    public async Task Submit_ChildProfileNotActive_Returns409()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id, ChildProfileStatus.Suspended);

        var response = await factory.ClientFor(parent).PostAsJsonAsync("/api/v1/ai-story-input/requests", Submit(child.Id, "api-submit-0003"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Child Profile phải đang Active để tạo truyện.",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Submit_TopicTooShort_Returns400()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);

        var response = await factory.ClientFor(parent).PostAsJsonAsync("/api/v1/ai-story-input/requests",
            Submit(child.Id, "api-submit-0004", topic: "x"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class StoryReviewApproveApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Approve_OwnerOfReadyContent_Returns200AndQueuesMedia()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        var story = await ApiSeed.ParentStoryAsync(factory, parent.Id, child.Id, StoryStatus.ContentReview);
        var client = factory.ClientFor(parent);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/stories/{story.Id}/review/story/complete", null)).StatusCode);

        var response = await client.PostAsync($"/api/v1/stories/{story.Id}/review/approve", null);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Status was {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.True(await factory.WithDbAsync(db => db.Set<StoryGenerationJob>().AnyAsync(
            job => job.StoryId == story.Id && job.Operation == GenerationJobOperation.GenerateMediaPackage)));
    }

    [Fact]
    public async Task Approve_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/stories/1/review/approve", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Moderator)]
    [InlineData(UserRole.ContentCreator)]
    public async Task Approve_RoleOutsideUserOrAdministrator_Returns403(UserRole role)
    {
        var response = await (await factory.ClientForNewUserAsync(role)).PostAsync("/api/v1/stories/1/review/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approve_UserNotChildOwner_Returns403()
    {
        var owner = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, owner.Id);
        var story = await ApiSeed.ParentStoryAsync(factory, owner.Id, child.Id, StoryStatus.ContentReview);

        var response = await (await factory.ClientForNewUserAsync(UserRole.User))
            .PostAsync($"/api/v1/stories/{story.Id}/review/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Approve_StoryNotInContentReview_Returns409()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        var story = await ApiSeed.ParentStoryAsync(factory, parent.Id, child.Id, StoryStatus.OutlineReview);

        var response = await factory.ClientFor(parent).PostAsync($"/api/v1/stories/{story.Id}/review/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INVALID_STORY_STATUS", (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Approve_ContentFailsValidation_Returns400()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        var story = await ApiSeed.ParentStoryAsync(factory, parent.Id, child.Id, StoryStatus.ContentReview, content: " ");

        var response = await factory.ClientFor(parent).PostAsync($"/api/v1/stories/{story.Id}/review/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Content is not ready for media", (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }
}

public sealed class ModerationQueueClaimApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Claim_ModeratorOnPendingItem_Returns200AndMarksClaimed()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var item = await ApiSeed.PendingModerationItemAsync(factory);

        var response = await factory.ClientFor(moderator).PostAsync($"/api/v1/ModerationQueue/{item.Id}/claim", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await factory.WithDbAsync(async db => (await db.ModerationItems.FindAsync(item.Id))!);
        Assert.Equal((ModerationItemStatus.Claimed, moderator.Id), (stored.Status, stored.ClaimedByUserId!.Value));
    }

    [Fact]
    public async Task Claim_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsync("/api/v1/ModerationQueue/1/claim", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.ContentCreator)]
    public async Task Claim_NonStaffRole_Returns403(UserRole role)
    {
        var item = await ApiSeed.PendingModerationItemAsync(factory);

        var response = await (await factory.ClientForNewUserAsync(role)).PostAsync($"/api/v1/ModerationQueue/{item.Id}/claim", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Claim_ModeratorOwnSubmission_Returns403()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var item = await ApiSeed.PendingModerationItemAsync(factory, createdByUserId: moderator.Id);

        var response = await factory.ClientFor(moderator).PostAsync($"/api/v1/ModerationQueue/{item.Id}/claim", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Bạn không thể duyệt nội dung do chính mình tạo hoặc nộp.",
            (await ReadJsonAsync(response)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Claim_AlreadyClaimedItem_Returns409()
    {
        var first = await factory.AddUserAsync(UserRole.Moderator);
        var item = await ApiSeed.PendingModerationItemAsync(factory);
        await factory.ClientFor(first).PostAsync($"/api/v1/ModerationQueue/{item.Id}/claim", null);

        var response = await (await factory.ClientForNewUserAsync(UserRole.Moderator))
            .PostAsync($"/api/v1/ModerationQueue/{item.Id}/claim", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Claim_UnknownItem_Returns404()
    {
        var response = await (await factory.ClientForNewUserAsync(UserRole.Moderator)).PostAsync("/api/v1/ModerationQueue/987654/claim", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Claim_NonNumericId_Returns404FromRouteConstraint()
    {
        var response = await (await factory.ClientForNewUserAsync(UserRole.Moderator)).PostAsync("/api/v1/ModerationQueue/abc/claim", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed class PaymentWebhookApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/v1/Payment/webhook/sepay";

    [Fact]
    public async Task Webhook_CorrectApiKeyAndAmount_Returns200AndMarksTransactionPaid()
    {
        var transaction = await SeedPendingTransactionAsync("TOPUP123456");

        var response = await Send($"Apikey {ApiFactory.WebhookApiKey}",
            new { content = "CK TOPUP123456 nap quota", transferAmount = 50000, referenceCode = "FT-001" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await factory.WithDbAsync(async db => (await db.PaymentTransactions.FindAsync(transaction.Id))!);
        Assert.Equal((PaymentStatus.Paid, "FT-001"), (stored.Status, stored.SepayTransactionId));
    }

    [Fact]
    public async Task Webhook_CorrectApiKeyWrongAmount_Returns200AndFlagsMismatch()
    {
        var transaction = await SeedPendingTransactionAsync("TOPUP654321");

        var response = await Send($"Apikey {ApiFactory.WebhookApiKey}",
            new { content = "TOPUP654321", transferAmount = 10000, referenceCode = "FT-002" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentStatus.MismatchAmount,
            await factory.WithDbAsync(async db => (await db.PaymentTransactions.FindAsync(transaction.Id))!.Status));
    }

    [Fact]
    public async Task Webhook_WrongApiKey_Returns401AndLeavesTransactionPending()
    {
        var transaction = await SeedPendingTransactionAsync("TOPUP777777");

        var response = await Send("Apikey wrong-key", new { content = "TOPUP777777", transferAmount = 50000 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending,
            await factory.WithDbAsync(async db => (await db.PaymentTransactions.FindAsync(transaction.Id))!.Status));
    }

    [Fact]
    public async Task Webhook_BearerSchemeInsteadOfApikey_Returns401()
    {
        var response = await Send($"Bearer {ApiFactory.WebhookApiKey}", new { content = "x", transferAmount = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_MissingAuthorizationHeader_Returns401()
    {
        var response = await Send(null, new { content = "x", transferAmount = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_MalformedJson_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = new StringContent("{\"transferAmount\": \"not-a-number\"", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Apikey {ApiFactory.WebhookApiKey}");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> Send(string? authorization, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(body) };
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return factory.CreateClient().SendAsync(request);
    }

    private async Task<PaymentTransaction> SeedPendingTransactionAsync(string code)
    {
        var payer = await factory.AddUserAsync(UserRole.User);
        return await factory.WithDbAsync(async db =>
        {
            var plan = new SubscriptionPlan { Name = "Gói 50k", PriceVnd = 50000, QuotaAmount = 10 };
            db.SubscriptionPlans.Add(plan);
            var transaction = new PaymentTransaction
            {
                Plan = plan, PayerUserId = payer.Id, TransactionCode = code, Amount = 50000,
                Status = PaymentStatus.Pending, ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            };
            db.PaymentTransactions.Add(transaction);
            await db.SaveChangesAsync();
            return transaction;
        });
    }
}
