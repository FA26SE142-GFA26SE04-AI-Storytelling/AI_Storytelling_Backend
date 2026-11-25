using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Moderation.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class EventSubmissionModerationQueueApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ApiEnvelope<T>(bool Success, string? Message, T? Data);

    private sealed record StoryFixture(int CategoryId, int StoryId, int VersionId, int SceneId, int SegmentId);

    private async Task<int> CreateCategoryAsync(string codePrefix)
    {
        return await factory.WithDbAsync(async db =>
        {
            var cat = new ContentCategory
            {
                Code = $"{codePrefix}-{Guid.NewGuid():N}"[..18],
                DisplayName = $"Chủ đề {codePrefix}",
                IsActive = true
            };
            db.ContentCategories.Add(cat);
            await db.SaveChangesAsync();
            return cat.Id;
        });
    }

    private async Task<StoryFixture> CreateReadyCreatorStoryAsync(int creatorId, int categoryId, string title)
    {
        return await factory.WithDbAsync(async db =>
        {
            var story = new Story
            {
                AuthorUserId = creatorId,
                Scope = StoryScope.Creator,
                Source = StorySource.Manual,
                Status = StoryStatus.Ready,
                OutputMode = StoryOutputMode.ReadingMediaOnly,
                CoverImageUrl = "https://cdn.example.com/cover.png",
                Title = title,
                AgeBand = "6-8",
                Language = "vi"
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync();

            var version = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = 1,
                IsCurrent = true,
                Title = title,
                Content = $"Nội dung truyện {title} dài hơn để kiểm tra trùng lặp và kiểm duyệt an toàn.",
                Lesson = "Bài học đoàn kết",
                SafetyScore = 100m
            };
            version.AuthoringMetadata.StoryId = story.Id;
            db.StoryVersions.Add(version);
            await db.SaveChangesAsync();

            var scene = new StoryScene
            {
                StoryVersionId = version.Id,
                SceneIndex = 1,
                TextRangeStart = 0,
                TextRangeEnd = 10,
                SceneText = "Cảnh một"
            };
            db.StoryScenes.Add(scene);
            await db.SaveChangesAsync();

            var beat = new IllustrationBeat
            {
                StorySceneId = scene.Id,
                BeatOrder = 1,
                StartOffset = 0,
                EndOffset = 10,
                VisualFocus = "Khu rừng"
            };
            var segment = new StorySegment
            {
                StorySceneId = scene.Id,
                SegmentOrder = 1,
                StartOffset = 0,
                EndOffset = 10,
                TextContent = "Cảnh một"
            };
            db.IllustrationBeats.Add(beat);
            db.StorySegments.Add(segment);
            await db.SaveChangesAsync();

            db.MediaAssets.AddRange(
                new MediaAsset
                {
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    IllustrationBeatId = beat.Id,
                    Type = MediaType.Illustration,
                    Status = MediaStatus.Ready,
                    ValidationStatus = ValidationStatus.Passed,
                    IsCurrent = true,
                    Url = "https://cdn.example.com/img.png"
                },
                new MediaAsset
                {
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    StorySegmentId = segment.Id,
                    Type = MediaType.TtsAudio,
                    Status = MediaStatus.Ready,
                    IsCurrent = true,
                    Url = "https://cdn.example.com/audio.mp3"
                });
            db.StoryCategories.Add(new StoryCategory
            {
                StoryId = story.Id,
                ContentCategoryId = categoryId,
                Source = TagSource.Creator
            });
            await db.SaveChangesAsync();

            return new StoryFixture(categoryId, story.Id, version.Id, scene.Id, segment.Id);
        });
    }

    private async Task<int> CreatePublishedSubmissionEventAsync(
        UserAccount leadMod,
        UserAccount admin,
        int categoryId,
        string title,
        DateTime submissionEndUtc,
        DateTime reviewCutoffUtc,
        UserAccount[]? assistants = null,
        bool includeReadingMissions = false,
        EventReadingStorySource readingSource = EventReadingStorySource.ApprovedSubmissionsOnly)
    {
        var modClient = factory.ClientFor(leadMod);
        var adminClient = factory.ClientFor(admin);
        var now = DateTime.UtcNow;

        var activities = new List<object>
        {
            new
            {
                activityType = "Submission",
                allowedAudience = "Creator",
                registrationStartAt = now.AddHours(-4),
                registrationEndAt = submissionEndUtc,
                startAt = now.AddHours(-4),
                endAt = submissionEndUtc,
                reviewCutoffAt = reviewCutoffUtc,
                grantAiCredits = 0,
                maxSubmissionsPerUser = 3
            }
        };

        if (includeReadingMissions)
        {
            activities.Add(new
            {
                activityType = "ReadingMissions",
                allowedAudience = "Parent",
                registrationStartAt = reviewCutoffUtc.AddMinutes(5),
                registrationEndAt = reviewCutoffUtc.AddDays(5),
                startAt = reviewCutoffUtc.AddMinutes(5),
                endAt = reviewCutoffUtc.AddDays(5),
                waiveWeeklyLibraryLimit = true,
                storySource = readingSource.ToString(),
                grantAiCredits = 0
            });
        }

        var createRes = await modClient.PostAsJsonAsync("/api/v1/events", new
        {
            configVersion = "ActivityBased",
            title,
            description = $"Mô tả {title}",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8",
            budgetVnd = 0L,
            activities,
            awardSlots = new[]
            {
                new
                {
                    activityType = "Submission",
                    resultSource = "SubmissionScore",
                    targetAudience = "Creator",
                    rankOrTier = "Giải Sáng tác",
                    slotCount = 1,
                    rewardType = "PhysicalGift",
                    rewardName = "Cúp",
                    unitValueVnd = 0L
                }
            }
        });
        createRes.EnsureSuccessStatusCode();
        var eventId = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<EventDetailDto>>())!.Data!.Id;

        var tasks = includeReadingMissions
            ? new object[]
            {
                new
                {
                    taskCode = "READ_EVENT",
                    title = "Đọc 1 truyện sự kiện",
                    description = "Hoàn thành đọc truyện",
                    sortOrder = 1,
                    verifierType = "ReadEventStories",
                    targetValue = 1,
                    isMandatory = true,
                    points = 10
                }
            }
            : Array.Empty<object>();

        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/rules", new
        {
            rulesText = $"Thể lệ {title}",
            completionMode = "AllMandatory",
            tasks
        })).EnsureSuccessStatusCode();

        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submit-approval", new { })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/approve", new { approved = true })).EnsureSuccessStatusCode();

        if (assistants is { Length: > 0 })
        {
            (await adminClient.PutAsJsonAsync($"/api/v1/events/{eventId}/lead-moderator", new
            {
                moderatorId = leadMod.Id,
                assistantModeratorIds = assistants.Select(a => a.Id).ToArray()
            })).EnsureSuccessStatusCode();
        }

        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/publish", new { })).EnsureSuccessStatusCode();
        return eventId;
    }

    private async Task CompleteAndApproveReviewAsync(HttpClient modClient, int itemId, StoryFixture story)
    {
        (await modClient.PostAsJsonAsync($"/api/v1/PublicStoryReview/{itemId}/views", new { partType = "scene", partId = story.SceneId })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/PublicStoryReview/{itemId}/views", new { partType = "audio", partId = story.SegmentId })).EnsureSuccessStatusCode();
        (await modClient.PutAsJsonAsync($"/api/v1/PublicStoryReview/{itemId}/checklist", new
        {
            safety = true,
            ageFit = true,
            education = true,
            language = true,
            copyright = true,
            image = true,
            audio = true,
            interaction = true
        })).EnsureSuccessStatusCode();
        var approveRes = await modClient.PostAsJsonAsync($"/api/v1/PublicStoryReview/{itemId}/approve", new { });
        Assert.True(approveRes.IsSuccessStatusCode, $"Approve failed {approveRes.StatusCode}: {await approveRes.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task RegularStory_KeepsStandardFlow_AndIsExcludedFromEventSubmissionFilter()
    {
        var categoryId = await CreateCategoryAsync("reg");
        var mod = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện thường độc lập");

        await factory.WithDbAsync(async db =>
        {
            db.CreditLots.Add(new CreditLot
            {
                UserId = creator.Id,
                CreditType = CreditType.Submit,
                Source = CreditSource.CreatorPack,
                Total = 2,
                Remaining = 2
            });
            await db.SaveChangesAsync();
        });

        var creatorClient = factory.ClientFor(creator);
        var submitRes = await creatorClient.PostAsJsonAsync("/api/v1/PublicStorySubmission/submit", new
        {
            storyId = story.StoryId,
            storyVersionId = story.VersionId,
            contentCategoryIds = new[] { categoryId }
        });
        submitRes.EnsureSuccessStatusCode();
        var submitted = (await submitRes.Content.ReadFromJsonAsync<ApiEnvelope<PublicStorySubmissionResultDto>>())!.Data!;

        var modClient = factory.ClientFor(mod);
        var eventOnlyRes = await modClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&onlyMyEventSubmissions=true&pageSize=20");
        Assert.DoesNotContain(eventOnlyRes!.Data!.Items, x => x.Id == submitted.ItemId);

        var allRes = await modClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        var row = Assert.Single(allRes!.Data!.Items, x => x.Id == submitted.ItemId);
        Assert.Empty(row.EventSubmissions);

        (await modClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{submitted.ItemId}/claim", new { })).EnsureSuccessStatusCode();
        await CompleteAndApproveReviewAsync(modClient, submitted.ItemId, story);
    }

    [Fact]
    public async Task AssignedLeadAndAssistant_OrderByNearestCutoff_BlockNonAssigned_AndAllowAdminCoordination()
    {
        var categoryId = await CreateCategoryAsync("perm");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var modA = await factory.AddUserAsync(UserRole.Moderator);
        var modB = await factory.AddUserAsync(UserRole.Moderator);
        var modC = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);

        // Partner creator so normal story has High priority, verifying active event items still rank ahead of it.
        await factory.WithDbAsync(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Id == creator.Id);
            u.CreatorTier = CreatorTier.Partner;
            db.CreditLots.Add(new CreditLot
            {
                UserId = creator.Id,
                CreditType = CreditType.Submit,
                Source = CreditSource.CreatorPack,
                Total = 5,
                Remaining = 5
            });
            await db.SaveChangesAsync();
        });

        var now = DateTime.UtcNow;
        var eventSoonId = await CreatePublishedSubmissionEventAsync(
            modA, admin, categoryId, "Sự kiện Hạn Gần", now.AddHours(2), now.AddHours(2), assistants: [modB]);
        var eventLaterId = await CreatePublishedSubmissionEventAsync(
            modA, admin, categoryId, "Sự kiện Hạn Xa", now.AddHours(24), now.AddHours(24), assistants: [modB]);
        var eventOtherId = await CreatePublishedSubmissionEventAsync(
            modC, admin, categoryId, "Sự kiện Nhóm Khác", now.AddHours(1), now.AddHours(1));

        var storySoon = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Bài dự thi gần hạn");
        var storyLater = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Bài dự thi xa hạn");
        var storyOther = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Bài dự thi nhóm khác");
        var storyNormalHigh = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Bài thường ưu tiên cao");

        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventLaterId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventSoonId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventOtherId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();

        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventLaterId}/activities/submission/submit", new { storyId = storyLater.StoryId })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventSoonId}/activities/submission/submit", new { storyId = storySoon.StoryId })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventOtherId}/activities/submission/submit", new { storyId = storyOther.StoryId })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync("/api/v1/PublicStorySubmission/submit", new
        {
            storyId = storyNormalHigh.StoryId,
            storyVersionId = storyNormalHigh.VersionId,
            contentCategoryIds = new[] { categoryId }
        })).EnsureSuccessStatusCode();

        // Find the moderation item IDs created for our stories.
        var soonItemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == storySoon.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());
        var otherItemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == storyOther.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        // Assistant Mod B queries Pending page 1 with pageSize=2 using onlyMyEventSubmissions=true:
        // TotalCount should reflect scoped/filtered items before pagination (2, NOT including EventOther or normal story).
        var modBClient = factory.ClientFor(modB);
        var filteredB = await modBClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&onlyMyEventSubmissions=true&pageIndex=1&pageSize=1");
        Assert.Equal(2, filteredB!.Data!.TotalCount);
        var firstFiltered = Assert.Single(filteredB.Data.Items);
        Assert.Equal(storySoon.StoryId, firstFiltered.StoryId);
        var linkedEv = Assert.Single(firstFiltered.EventSubmissions);
        Assert.Equal(eventSoonId, linkedEv.EventId);
        Assert.Equal("Sự kiện Hạn Gần", linkedEv.EventTitle);
        Assert.NotNull(linkedEv.ReviewCutoffAt);

        // Unfiltered Pending queue for Mod B places EventSoon, EventLater ahead of NormalHigh, and excludes EventOther completely.
        var allModB = await modBClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageIndex=1&pageSize=20");
        var ourStoryIds = new HashSet<int> { storySoon.StoryId, storyLater.StoryId, storyOther.StoryId, storyNormalHigh.StoryId };
        var modBOrder = allModB!.Data!.Items.Where(x => x.StoryId.HasValue && ourStoryIds.Contains(x.StoryId.Value)).Select(x => x.StoryId!.Value).ToList();
        Assert.Equal(new[] { storySoon.StoryId, storyLater.StoryId, storyNormalHigh.StoryId }, modBOrder);

        // Outside Mod C cannot see EventSoon/EventLater in list and gets 403 on direct claim.
        var modCClient = factory.ClientFor(modC);
        var allModC = await modCClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        Assert.DoesNotContain(allModC!.Data!.Items, x => x.Id == soonItemId);

        var modCClaimForbidden = await modCClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{soonItemId}/claim", new { });
        Assert.Equal(HttpStatusCode.Forbidden, modCClaimForbidden.StatusCode);

        // Admin sees EventOther in queue for coordination, cannot claim directly while Moderator exists, and can assign Mod B.
        var adminClient = factory.ClientFor(admin);
        var adminQueue = await adminClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&onlyMyEventSubmissions=true&pageSize=50");
        Assert.Contains(adminQueue!.Data!.Items, x => x.Id == otherItemId);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{otherItemId}/claim", new { })).StatusCode);

        (await adminClient.PutAsJsonAsync($"/api/v1/events/{eventOtherId}/lead-moderator", new
        {
            moderatorId = modC.Id,
            assistantModeratorIds = new[] { modB.Id }
        })).EnsureSuccessStatusCode();

        // Assistant Mod B now sees EventSoon, claims, opens package (with EventSubmissions), and approves.
        var claimRes = await modBClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{soonItemId}/claim", new { });
        claimRes.EnsureSuccessStatusCode();
        var claimedDto = (await claimRes.Content.ReadFromJsonAsync<ApiEnvelope<ModerationItemDetailDto>>())!.Data!;
        Assert.Single(claimedDto.EventSubmissions);

        var pkgRes = await modBClient.GetFromJsonAsync<ApiEnvelope<PublicReviewPackageDto>>($"/api/v1/PublicStoryReview/{soonItemId}");
        Assert.Single(pkgRes!.Data!.EventSubmissions);
        await CompleteAndApproveReviewAsync(modBClient, soonItemId, storySoon);

        var soonSubmission = await factory.WithDbAsync(db => db.EventSubmissions.SingleAsync(s => s.EventId == eventSoonId && s.StoryId == storySoon.StoryId));
        Assert.Equal(EventSubmissionStatus.Approved, soonSubmission.Status);
    }

    [Fact]
    public async Task RevokingAssignmentAfterClaim_ReturnsItemToPending_BlocksOldMod_AndAllowsNewMod()
    {
        var categoryId = await CreateCategoryAsync("revoke");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var modA = await factory.AddUserAsync(UserRole.Moderator);
        var modB = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);

        var eventId = await CreatePublishedSubmissionEventAsync(
            modA, admin, categoryId, "Sự kiện Thu Hồi Phân Công", DateTime.UtcNow.AddHours(6), DateTime.UtcNow.AddHours(6));
        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện bị đổi người duyệt");

        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var itemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == story.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        var modAClient = factory.ClientFor(modA);
        (await modAClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).EnsureSuccessStatusCode();

        // Admin reassigns lead moderator from Mod A to Mod B -> claimed item must return to Pending immediately.
        var adminClient = factory.ClientFor(admin);
        (await adminClient.PutAsJsonAsync($"/api/v1/events/{eventId}/lead-moderator", new
        {
            moderatorId = modB.Id,
            assistantModeratorIds = Array.Empty<int>()
        })).EnsureSuccessStatusCode();

        var releasedItem = await factory.WithDbAsync(db => db.ModerationItems.SingleAsync(m => m.Id == itemId));
        Assert.Equal(ModerationItemStatus.Pending, releasedItem.Status);
        Assert.Null(releasedItem.ClaimedByUserId);

        // Old moderator Mod A is blocked on queue endpoints & direct actions.
        Assert.Equal(HttpStatusCode.Forbidden, (await modAClient.GetAsync($"/api/v1/ModerationQueue/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await modAClient.GetAsync($"/api/v1/PublicStoryReview/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await modAClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await modAClient.PostAsJsonAsync($"/api/v1/PublicStoryReview/{itemId}/approve", new { })).StatusCode);

        // New moderator Mod B sees Pending item, claims it, and completes approval.
        var modBClient = factory.ClientFor(modB);
        var modBQueue = await modBClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&onlyMyEventSubmissions=true&pageSize=20");
        Assert.Contains(modBQueue!.Data!.Items, x => x.Id == itemId);

        (await modBClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).EnsureSuccessStatusCode();
        await CompleteAndApproveReviewAsync(modBClient, itemId, story);

        var sub = await factory.WithDbAsync(db => db.EventSubmissions.SingleAsync(s => s.EventId == eventId && s.StoryId == story.StoryId));
        Assert.Equal(EventSubmissionStatus.Approved, sub.Status);
        Assert.Equal(story.VersionId, sub.LockedStoryVersionId);
    }

    [Fact]
    public async Task MultiEventSameVersion_RequiresPermissionAcrossAllEvents_AndUpdatesAllSubmissionsOnSingleApprove()
    {
        var categoryId = await CreateCategoryAsync("multi");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var modA = await factory.AddUserAsync(UserRole.Moderator);
        var modB = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);

        var now = DateTime.UtcNow;
        var event1Id = await CreatePublishedSubmissionEventAsync(modA, admin, categoryId, "Sự kiện Một", now.AddHours(10), now.AddHours(10));
        var event2Id = await CreatePublishedSubmissionEventAsync(modB, admin, categoryId, "Sự kiện Hai", now.AddHours(4), now.AddHours(4));
        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện nộp hai sự kiện");

        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event1Id}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event2Id}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event1Id}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event2Id}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var itemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == story.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        // Neither Mod A nor Mod B has permission for BOTH events -> neither sees it, neither can claim it.
        var modAClient = factory.ClientFor(modA);
        var modBClient = factory.ClientFor(modB);
        var queueA = await modAClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>("/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        var queueB = await modBClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>("/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        Assert.DoesNotContain(queueA!.Data!.Items, x => x.Id == itemId);
        Assert.DoesNotContain(queueB!.Data!.Items, x => x.Id == itemId);
        Assert.Equal(HttpStatusCode.Forbidden, (await modAClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await modBClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).StatusCode);

        // Admin sees BOTH linked events and assigns Mod A as assistant on Event 2.
        var adminClient = factory.ClientFor(admin);
        var adminQueue = await adminClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>("/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        var adminItem = Assert.Single(adminQueue!.Data!.Items, x => x.Id == itemId);
        Assert.Equal(new[] { event2Id, event1Id }, adminItem.EventSubmissions.Select(e => e.EventId).ToArray());

        (await adminClient.PutAsJsonAsync($"/api/v1/events/{event2Id}/lead-moderator", new
        {
            moderatorId = modB.Id,
            assistantModeratorIds = new[] { modA.Id }
        })).EnsureSuccessStatusCode();

        // Mod A now claims & approves once -> BOTH submissions become Approved + EventStory created in both events.
        (await modAClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).EnsureSuccessStatusCode();
        await CompleteAndApproveReviewAsync(modAClient, itemId, story);

        var submissions = await factory.WithDbAsync(db => db.EventSubmissions
            .Where(s => s.StoryId == story.StoryId)
            .OrderBy(s => s.EventId)
            .ToListAsync());
        Assert.Equal(2, submissions.Count);
        Assert.All(submissions, s =>
        {
            Assert.Equal(EventSubmissionStatus.Approved, s.Status);
            Assert.Equal(story.VersionId, s.LockedStoryVersionId);
        });

        var eventStories = await factory.WithDbAsync(db => db.EventStories
            .Where(es => es.StoryId == story.StoryId && es.IsActive)
            .ToListAsync());
        Assert.Equal(2, eventStories.Count);
        const string historyUrl = "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Approved&onlyMyEventSubmissions=true&pageSize=1";
        var historyA = await modAClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(historyUrl);
        var historyB = await modBClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(historyUrl);
        Assert.Equal(2, Assert.Single(historyA!.Data!.Items, x => x.Id == itemId).EventSubmissions.Count);
        Assert.DoesNotContain(historyB!.Data!.Items, x => x.Id == itemId);
    }

    [Fact]
    public async Task ClaimedSingleEvent_WhenSameVersionSubmittedToSecondUnassignedEvent_ReleasesItemToPending()
    {
        var categoryId = await CreateCategoryAsync("dyn");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var modA = await factory.AddUserAsync(UserRole.Moderator);
        var modB = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);

        var event1Id = await CreatePublishedSubmissionEventAsync(modA, admin, categoryId, "Sự kiện Đầu", DateTime.UtcNow.AddHours(8), DateTime.UtcNow.AddHours(8));
        var event2Id = await CreatePublishedSubmissionEventAsync(modB, admin, categoryId, "Sự kiện Thêm", DateTime.UtcNow.AddHours(6), DateTime.UtcNow.AddHours(6));
        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện đang giữ lại nộp thêm");

        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event1Id}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event1Id}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var itemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == story.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        var modAClient = factory.ClientFor(modA);
        (await modAClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).EnsureSuccessStatusCode();

        // Creator submits same version to Event 2 where Mod A is not assigned -> item releases back to Pending!
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event2Id}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{event2Id}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var released = await factory.WithDbAsync(db => db.ModerationItems.SingleAsync(m => m.Id == itemId));
        Assert.Equal(ModerationItemStatus.Pending, released.Status);
        Assert.Null(released.ClaimedByUserId);
        Assert.Equal(HttpStatusCode.Forbidden, (await modAClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).StatusCode);
    }

    [Fact]
    public async Task ExpiredCutoff_ReturnsToNormalQueue_LatePublicApprovalDoesNotApproveSubmission_AndPreApprovedVersionReusesResult()
    {
        var categoryId = await CreateCategoryAsync("exp");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var modA = await factory.AddUserAsync(UserRole.Moderator);
        var outsideMod = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);

        var expiredEventId = await CreatePublishedSubmissionEventAsync(
            modA, admin, categoryId, "Sự kiện Hết Hạn Duyệt", DateTime.UtcNow.AddHours(3), DateTime.UtcNow.AddHours(3));
        var activeEventId = await CreatePublishedSubmissionEventAsync(
            modA, admin, categoryId, "Sự kiện Mới Còn Hạn", DateTime.UtcNow.AddHours(5), DateTime.UtcNow.AddHours(5));
        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện quá hạn sự kiện");

        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{expiredEventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{expiredEventId}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var itemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == story.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        // Move review cutoff of expiredEvent into the past.
        await factory.WithDbAsync(async db =>
        {
            var act = await db.EventActivities.SingleAsync(a => a.EventId == expiredEventId && a.ActivityType == EventActivityType.Submission);
            act.EndAt = DateTime.UtcNow.AddMinutes(-10);
            act.ReviewCutoffAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });

        // Outside moderator now sees the item as a normal public review (no active EventSubmissions) and can approve it publicly.
        var outsideClient = factory.ClientFor(outsideMod);
        var outsideQueue = await outsideClient.GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        var row = Assert.Single(outsideQueue!.Data!.Items, x => x.Id == itemId);
        Assert.Empty(row.EventSubmissions);

        (await outsideClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).EnsureSuccessStatusCode();
        await CompleteAndApproveReviewAsync(outsideClient, itemId, story);

        var expiredSub = await factory.WithDbAsync(db => db.EventSubmissions.SingleAsync(s => s.EventId == expiredEventId && s.StoryId == story.StoryId));
        Assert.Equal(EventSubmissionStatus.ReviewExpired, expiredSub.Status);
        Assert.Equal(itemId, expiredSub.LatestModerationItemId);
        var expiredEventStories = await factory.WithDbAsync(db => db.EventStories.Where(es => es.EventId == expiredEventId && es.StoryId == story.StoryId).ToListAsync());
        Assert.Empty(expiredEventStories);

        // Now Creator submits the same already-approved & published version to activeEventId -> reuses approval immediately.
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{activeEventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        var activeSubRes = await creatorClient.PostAsJsonAsync($"/api/v1/events/{activeEventId}/activities/submission/submit", new { storyId = story.StoryId });
        activeSubRes.EnsureSuccessStatusCode();

        var activeSub = await factory.WithDbAsync(db => db.EventSubmissions.SingleAsync(s => s.EventId == activeEventId && s.StoryId == story.StoryId));
        Assert.Equal(EventSubmissionStatus.Approved, activeSub.Status);
        Assert.Equal(story.VersionId, activeSub.LockedStoryVersionId);
        var activeEventStory = await factory.WithDbAsync(db => db.EventStories.SingleAsync(es => es.EventId == activeEventId && es.StoryId == story.StoryId));
        Assert.Equal(story.VersionId, activeEventStory.StoryVersionId);
    }

    [Fact]
    public async Task ConcurrentClaim_ByTwoAuthorizedModerators_AllowsOnlyOneSuccess()
    {
        var categoryId = await CreateCategoryAsync("race");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var modA = await factory.AddUserAsync(UserRole.Moderator);
        var modB = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);

        var eventId = await CreatePublishedSubmissionEventAsync(
            modA, admin, categoryId, "Sự kiện Nhận Đồng Thời", DateTime.UtcNow.AddHours(4), DateTime.UtcNow.AddHours(4), assistants: [modB]);
        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện tranh chấp claim");

        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var itemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == story.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        var clientA = factory.ClientFor(modA);
        var clientB = factory.ClientFor(modB);
        // ApiFactory dùng chung một SqliteConnection :memory: nên chạy hai yêu cầu claim nối tiếp nhau
        // để kiểm tra khóa trạng thái Pending -> Claimed trả 200 cho người đầu và 409 Conflict cho người thứ hai.
        var firstClaim = await clientA.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { });
        var secondClaim = await clientB.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { });

        Assert.Equal(HttpStatusCode.OK, firstClaim.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondClaim.StatusCode);
    }

    [Fact]
    public async Task EndToEnd_CreatorSubmits_AssignedModeratorApproves_ParentSeesApprovedVersionInApprovedSubmissionsOnlySource()
    {
        var categoryId = await CreateCategoryAsync("e2e");
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var assignedMod = await factory.AddUserAsync(UserRole.Moderator);
        var outsideMod = await factory.AddUserAsync(UserRole.Moderator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        await ApiSeed.ChildAsync(factory, parent.Id);

        var now = DateTime.UtcNow;
        var eventId = await CreatePublishedSubmissionEventAsync(
            assignedMod,
            admin,
            categoryId,
            "Sự kiện Thi & Đọc Chỉ Bài Đã Duyệt",
            now.AddHours(1),
            now.AddHours(2),
            includeReadingMissions: true,
            readingSource: EventReadingStorySource.ApprovedSubmissionsOnly);

        var story = await CreateReadyCreatorStoryAsync(creator.Id, categoryId, "Truyện Đọc Thi Tuyển");
        var creatorClient = factory.ClientFor(creator);
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit", new { storyId = story.StoryId })).EnsureSuccessStatusCode();

        var itemId = await factory.WithDbAsync(db => db.ModerationItems
            .Where(m => m.StoryId == story.StoryId && m.Status == ModerationItemStatus.Pending)
            .Select(m => m.Id)
            .SingleAsync());

        // Outside moderator does not see the submission item.
        var outsideQueue = await factory.ClientFor(outsideMod).GetFromJsonAsync<ApiEnvelope<PagedResult<ModerationItemSummaryDto>>>(
            "/api/v1/ModerationQueue?itemType=PublicStoryReview&status=Pending&pageSize=50");
        Assert.DoesNotContain(outsideQueue!.Data!.Items, x => x.Id == itemId);

        // Assigned moderator sees & approves it.
        var assignedClient = factory.ClientFor(assignedMod);
        (await assignedClient.PostAsJsonAsync($"/api/v1/ModerationQueue/{itemId}/claim", new { })).EnsureSuccessStatusCode();
        await CompleteAndApproveReviewAsync(assignedClient, itemId, story);

        // Open ReadingMissions window so parent can register & see the approved version.
        await factory.WithDbAsync(async db =>
        {
            var readAct = await db.EventActivities.SingleAsync(a => a.EventId == eventId && a.ActivityType == EventActivityType.ReadingMissions);
            readAct.RegistrationStartAt = DateTime.UtcNow.AddMinutes(-5);
            readAct.StartAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });

        var parentClient = factory.ClientFor(parent);
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "ReadingMissions", acceptRules = true })).EnsureSuccessStatusCode();
        var dashRes = await parentClient.GetFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>($"/api/v1/events/{eventId}/mission-dashboard");
        var storyInLibrary = Assert.Single(dashRes!.Data!.Stories);
        Assert.Equal(story.StoryId, storyInLibrary.StoryId);
        Assert.Equal(story.VersionId, storyInLibrary.StoryVersionId);
        Assert.Equal(EventStorySource.CreatorSubmission, storyInLibrary.Source);
    }
}
