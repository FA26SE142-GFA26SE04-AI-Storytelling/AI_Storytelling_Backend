using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>
/// Kiểm thử tích hợp End-to-End (HTTP thật qua ApiFactory + SQLite) cho luồng Tạo, Tổ chức,
/// Nhiệm vụ động (N &gt; 5), Xác minh, Khiếu nại, Trao giải và Đóng sự kiện (Phase 2 &amp; 3).
/// </summary>
public sealed class EventCampaignApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task OnlyAdminCanTransferEventAndFormerLeadLosesLifecyclePermissions()
    {
        var lead = await factory.AddUserAsync(UserRole.Moderator);
        var next = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var outsider = await factory.AddUserAsync(UserRole.User);
        var leadClient = factory.ClientFor(lead);
        var nextClient = factory.ClientFor(next);
        var adminClient = factory.ClientFor(admin);
        int eventId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new ContentCategory { Code = $"assignment-{Guid.NewGuid():N}", DisplayName = "Chủ đề thử nghiệm", IsActive = true };
            db.ContentCategories.Add(category);
            await db.SaveChangesAsync();
            var campaign = new Event
            {
                Title = "Sự kiện kiểm thử phân công", Slug = $"assignment-{Guid.NewGuid():N}", MainCategoryId = category.Id,
                CreatedByUserId = lead.Id, AssignedModeratorId = lead.Id, Status = EventStatus.Approved
            };
            db.Set<Event>().Add(campaign);
            await db.SaveChangesAsync();
            eventId = campaign.Id;
        }

        var url = $"/api/v1/events/{eventId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await nextClient.PostAsJsonAsync($"{url}/publish", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leadClient.PutAsJsonAsync($"{url}/lead-moderator", new { moderatorId = next.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nextClient.GetAsync($"{url}/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync($"{url}/lead-moderator", new { moderatorId = outsider.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync($"{url}/lead-moderator",
            new { moderatorId = lead.Id, assistantModeratorIds = new[] { lead.Id } })).StatusCode);
        (await adminClient.PutAsJsonAsync($"{url}/lead-moderator",
            new { moderatorId = lead.Id, assistantModeratorIds = new[] { next.Id } })).EnsureSuccessStatusCode();
        (await nextClient.GetAsync($"{url}/summary")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await nextClient.PostAsJsonAsync($"{url}/publish", new { })).StatusCode);

        (await adminClient.PutAsJsonAsync($"{url}/lead-moderator",
            new { moderatorId = next.Id, assistantModeratorIds = new[] { lead.Id } })).EnsureSuccessStatusCode();
        (await leadClient.GetAsync($"{url}/summary")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await leadClient.PostAsJsonAsync($"{url}/publish", new { })).StatusCode);
        (await nextClient.PostAsJsonAsync($"{url}/publish", new { })).EnsureSuccessStatusCode();
        (await adminClient.PutAsJsonAsync($"{url}/lead-moderator",
            new { moderatorId = next.Id, assistantModeratorIds = Array.Empty<int>() })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await leadClient.GetAsync($"{url}/summary")).StatusCode);
    }

    [Fact]
    public async Task FullEventLifecycle_CreateUpdateDynamicTasksVerifyAppealAwardAndClose_WorksEndToEnd()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var parentFailed = await factory.AddUserAsync(UserRole.User);
        await ApiSeed.ChildAsync(factory, parent.Id);
        await ApiSeed.ChildAsync(factory, parentFailed.Id);

        var modClient = factory.ClientFor(moderator);
        var adminClient = factory.ClientFor(admin);
        var creatorClient = factory.ClientFor(creator);
        var parentClient = factory.ClientFor(parent);
        var parentFailedClient = factory.ClientFor(parentFailed);

        var (seedStoryId, categoryId) = await SeedPublishedStoryAsync(creator.Id);

        var now = DateTime.UtcNow;
        var createPayload = new
        {
            title = "Lễ hội Đọc truyện Mùa Thu",
            description = "Bản nháp ban đầu",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8,9-10",
            budgetVnd = 2_000_000m,
            tracks = new object[]
            {
                new
                {
                    trackType = "Creator",
                    registrationStartAt = now.AddDays(-10),
                    registrationEndAt = now.AddMinutes(-1),
                    executionStartAt = now.AddDays(-10),
                    executionEndAt = now.AddMinutes(-1),
                    maxSubmissionsPerUser = 1,
                    grantAiCredits = 1,
                    grantSubmitCredits = 1,
                    waiveWeeklyLibraryLimit = false,
                    claimWindowHours = 72,
                    maxReallocationRounds = 1
                },
                new
                {
                    trackType = "Parent",
                    registrationStartAt = now.AddMinutes(-1),
                    registrationEndAt = now.AddDays(20),
                    executionStartAt = now.AddMinutes(-1),
                    executionEndAt = now.AddDays(20),
                    maxSubmissionsPerUser = 1,
                    grantAiCredits = 0,
                    grantSubmitCredits = 0,
                    waiveWeeklyLibraryLimit = true,
                    claimWindowHours = 72,
                    maxReallocationRounds = 1
                }
            },
            awardSlots = new object[]
            {
                new
                {
                    trackType = "Creator",
                    rankOrTier = "Giải Nhất",
                    slotCount = 1,
                    rewardType = "CashTransfer",
                    rewardName = "Giải Nhất Sáng tác",
                    unitValueVnd = 500_000m,
                    creditGrantAmount = 0
                },
                new
                {
                    trackType = "Parent",
                    rankOrTier = "Quà Gia đình",
                    slotCount = 1,
                    rewardType = "PhysicalGift",
                    rewardName = "Bộ sách thiếu nhi",
                    unitValueVnd = 150_000m,
                    creditGrantAmount = 0
                }
            }
        };

        // 1. Phân quyền: Parent không được tạo Event; Moderator tạo & chỉnh sửa Draft.
        Assert.Equal(HttpStatusCode.Forbidden, (await parentClient.PostAsJsonAsync("/api/v1/events", createPayload)).StatusCode);
        var createHttpRes = await modClient.PostAsJsonAsync("/api/v1/events", createPayload);
        var createBody = await createHttpRes.Content.ReadAsStringAsync();
        Assert.True(createHttpRes.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {createHttpRes.StatusCode}: {createBody}");
        var created = System.Text.Json.JsonDocument.Parse(createBody).RootElement.GetProperty("data");
        var eventId = created.GetProperty("id").GetInt32();

        var updatePayload = new
        {
            title = "Lễ hội Đọc truyện Mùa Thu (Chính thức)",
            description = "Mô tả đã cập nhật",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8",
            budgetVnd = 2_500_000m,
            tracks = createPayload.tracks,
            awardSlots = createPayload.awardSlots
        };
        var putRes = await modClient.PutAsJsonAsync($"/api/v1/events/{eventId}", updatePayload);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        // 2. Cấu hình 6 nhiệm vụ động (> 5) theo chế độ MinimumTasksCount = 1 để test hoàn thành linh hoạt.
        var sixTasks = Enumerable.Range(1, 6).Select(i => new
        {
            taskCode = $"T0{i}",
            title = $"Nhiệm vụ động {i}",
            description = "Mô tả nhiệm vụ",
            sortOrder = i,
            verifierType = i == 1 ? "GuidedDiscussionCheckin" : "RealLifePracticeCheckin",
            targetValue = 1,
            isMandatory = i == 1,
            points = 15,
            allowMultiplePerStory = false
        }).ToArray();

        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/rules", new
        {
            rulesText = "Thể lệ linh hoạt N nhiệm vụ.",
            completionMode = "MinimumTasksCount",
            requiredTaskCount = 1,
            requiredPoints = 15,
            tasks = sixTasks
        }, HttpStatusCode.OK);

        // 3. Submit approval -> Admin approve -> Seed fallback story -> Publish.
        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/submit-approval", new { }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Forbidden, (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/approve", new { approved = true })).StatusCode);
        await AdminApi.PostDataAsync(adminClient, $"/api/v1/events/{eventId}/approve", new { approved = true, note = "Hợp lệ" }, HttpStatusCode.OK);
        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/seed-stories", new { storyIds = new[] { seedStoryId } }, HttpStatusCode.OK);
        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/publish", new { }, HttpStatusCode.OK);

        // 4. Parent 1 hoàn thành nhiệm vụ; Parent 2 không hoàn thành để test luồng VerificationFailed -> Appeal.
        await AdminApi.PostDataAsync(parentClient, $"/api/v1/events/{eventId}/register",
            new { trackType = "Parent", acceptRules = true }, HttpStatusCode.OK);
        var failedPart = await AdminApi.PostDataAsync(parentFailedClient, $"/api/v1/events/{eventId}/register",
            new { trackType = "Parent", acceptRules = true }, HttpStatusCode.OK);
        var failedPartId = failedPart.GetProperty("id").GetInt32();

        var dashboard = await AdminApi.GetDataAsync(parentClient, $"/api/v1/events/{eventId}/mission-dashboard");
        var tasks = dashboard.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(6, tasks.Count);
        var firstTaskId = tasks[0].GetProperty("taskDefinitionId").GetInt32();

        var checkinHttp = await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/tasks/{firstTaskId}/complete",
            new { storyId = seedStoryId, selfDeclaredPayloadJson = "{\"note\":\"ok\"}" });
        var checkinBody = await checkinHttp.Content.ReadAsStringAsync();
        Assert.True(checkinHttp.StatusCode == HttpStatusCode.OK, $"Complete failed {checkinHttp.StatusCode}: {checkinBody}");
        var afterCheckin = System.Text.Json.JsonDocument.Parse(checkinBody).RootElement.GetProperty("data");
        Assert.True(afterCheckin.GetProperty("allRequirementsMet").GetBoolean());

        foreach (var action in new[] { "verify", "awards/propose", "close" })
            Assert.Equal(HttpStatusCode.Conflict, (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/{action}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/approve", new { })).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var parentTrack = await db.Set<EventTrack>().SingleAsync(t => t.EventId == eventId && t.TrackType == EventTrackType.Parent);
            parentTrack.ExecutionEndAt = DateTime.UtcNow.AddTicks(-1);
            await db.SaveChangesAsync();
        });

        // 5. Verify -> Parent 2 Appeal -> Moderator Decide -> Propose/Approve Awards -> Parent 1 Claim -> Fulfill -> Close -> Summary.
        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/verify", new { }, HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/approve", new { })).StatusCode);
        var appeal = await AdminApi.PostDataAsync(parentFailedClient, $"/api/v1/events/{eventId}/appeals",
            new { participationId = failedPartId, reasonText = "Xin rà soát lại minh chứng" }, HttpStatusCode.OK);
        var appealId = appeal.GetProperty("id").GetInt32();
        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/appeals/{appealId}/decide",
            new { decision = "Rejected", resolutionNote = "Chưa đủ nhiệm vụ theo thể lệ" }, HttpStatusCode.OK);

        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/awards/propose", new { }, HttpStatusCode.OK);
        await AdminApi.PostDataAsync(adminClient, $"/api/v1/events/{eventId}/awards/approve", new { }, HttpStatusCode.OK);

        var myRewards = (await AdminApi.GetDataAsync(parentClient, "/api/v1/events/my-rewards")).EnumerateArray().ToList();
        var awardId = Assert.Single(myRewards).GetProperty("id").GetInt32();

        await AdminApi.PostDataAsync(parentClient, $"/api/v1/events/awards/{awardId}/claim",
            new { recipientName = "Nguyễn Văn A", phoneNumber = "0901234567", shippingAddress = "TP.HCM" }, HttpStatusCode.OK);
        var recipientUrl = $"/api/v1/events/awards/{awardId}/recipient";
        Assert.Equal(HttpStatusCode.Forbidden, (await parentClient.GetAsync(recipientUrl)).StatusCode);
        var authRecipient = await adminClient.GetAsync(recipientUrl);
        Assert.Equal(HttpStatusCode.OK, authRecipient.StatusCode);
        Assert.True(authRecipient.Headers.CacheControl?.NoStore);
        var recipient = await AdminApi.GetDataAsync(modClient, recipientUrl);
        Assert.Equal("TP.HCM", recipient.GetProperty("shippingAddress").GetString());
        Assert.False(recipient.GetProperty("isLegacyPlaintext").GetBoolean());

        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/awards/{awardId}/fulfill",
            new { delivered = true, trackingOrBankTxCode = "VNPOST-888" }, HttpStatusCode.OK);
        await AdminApi.PostDataAsync(modClient, $"/api/v1/events/{eventId}/close", new { }, HttpStatusCode.OK);

        var summary = await AdminApi.GetDataAsync(modClient, $"/api/v1/events/{eventId}/summary");
        Assert.Equal(1, summary.GetProperty("publishedEventStories").GetInt32());
        Assert.Equal(1, summary.GetProperty("parentVerifiedCount").GetInt32());
        Assert.Equal(1, summary.GetProperty("awardsFulfilled").GetInt32());
    }

    private async Task<(int StoryId, int CategoryId)> SeedPublishedStoryAsync(int creatorUserId)
    {
        return await factory.WithDbAsync(async db =>
        {
            var cat = db.ContentCategories.FirstOrDefault(c => c.IsActive);
            if (cat == null)
            {
                cat = new ContentCategory { Code = "FAIRY_TALE", DisplayName = "Cổ tích", IsActive = true };
                db.ContentCategories.Add(cat);
                await db.SaveChangesAsync();
            }

            var story = new Story
            {
                AuthorUserId = creatorUserId,
                Scope = StoryScope.Creator,
                Source = StorySource.Manual,
                Status = StoryStatus.Ready,
                OutputMode = StoryOutputMode.ReadingMediaOnly,
                Title = "Chú Rùa Bông",
                Genre = "Cổ tích",
                AgeBand = "6-8",
                Language = "vi",
                ReadingLevel = 2,
                IsPublished = true,
                PublishedAt = DateTime.UtcNow
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync();

            var ver = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = 1,
                IsCurrent = true,
                Title = "Chú Rùa Bông",
                Content = "Ngày xửa ngày xưa...",
                Lesson = "Tình bạn",
                SafetyScore = 100m
            };
            ver.AuthoringMetadata.StoryId = story.Id;
            db.StoryVersions.Add(ver);
            await db.SaveChangesAsync();
            return (story.Id, cat.Id);
        });
    }
}
