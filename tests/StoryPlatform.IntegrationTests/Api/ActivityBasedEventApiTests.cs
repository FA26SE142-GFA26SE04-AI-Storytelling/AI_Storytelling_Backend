using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Application.Features.Events.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class ActivityBasedEventApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ApiEnvelope<T>(bool Success, string? Message, T? Data);

    [Fact]
    public async Task Archetype1_SubmissionOnly_ParentAndCreatorSharedRules_AiCreditGrantAndUsage_SubmitQuotaWaived_CapEnforced()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);

        var modClient = factory.ClientFor(moderator);
        var adminClient = factory.ClientFor(admin);
        var creatorClient = factory.ClientFor(creator);
        var parentClient = factory.ClientFor(parent);

        int categoryId, creatorStoryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = new ContentCategory { Code = "act-sub-" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Sáng tác V2", IsActive = true };
            db.ContentCategories.Add(cat);
            await db.SaveChangesAsync();
            categoryId = cat.Id;

            var cStory = new Story
            {
                AuthorUserId = creator.Id, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, OutputMode = StoryOutputMode.ReadingMediaOnly,
                CoverImageUrl = "https://cdn.example.com/cover.png",
                Title = "Creator Unpublished Ready", AgeBand = "6-8", Language = "vi"
            };
            db.Stories.Add(cStory);
            await db.SaveChangesAsync();
            creatorStoryId = cStory.Id;
            var cVer = new StoryVersion
            {
                StoryId = cStory.Id, VersionNo = 1, IsCurrent = true, Title = cStory.Title,
                Content = "Nội dung gửi duyệt miễn phí lượt nộp.", SafetyScore = 100m
            };
            cVer.AuthoringMetadata.StoryId = cStory.Id;
            db.StoryVersions.Add(cVer);
            await db.SaveChangesAsync();

            var scene = new StoryScene { StoryVersionId = cVer.Id, SceneIndex = 1, TextRangeStart = 0, TextRangeEnd = 8, SceneText = "Cảnh một" };
            db.StoryScenes.Add(scene);
            await db.SaveChangesAsync();

            var beat = new IllustrationBeat { StorySceneId = scene.Id, BeatOrder = 1, StartOffset = 0, EndOffset = 8, VisualFocus = "Thiên nhiên" };
            var segment = new StorySegment { StorySceneId = scene.Id, SegmentOrder = 1, StartOffset = 0, EndOffset = 8, TextContent = "Cảnh một" };
            db.IllustrationBeats.Add(beat);
            db.StorySegments.Add(segment);
            await db.SaveChangesAsync();

            db.MediaAssets.AddRange(
                new MediaAsset { StoryVersionId = cVer.Id, StorySceneId = scene.Id, IllustrationBeatId = beat.Id, Type = MediaType.Illustration, Status = MediaStatus.Ready, ValidationStatus = ValidationStatus.Passed, IsCurrent = true, Url = "https://cdn.example.com/img.png" },
                new MediaAsset { StoryVersionId = cVer.Id, StorySceneId = scene.Id, StorySegmentId = segment.Id, Type = MediaType.TtsAudio, Status = MediaStatus.Ready, IsCurrent = true, Url = "https://cdn.example.com/audio.mp3" });
            await db.SaveChangesAsync();
        }
        var pStory1 = await ApiSeed.ParentStoryAsync(factory, parent.Id, child.Id, StoryStatus.Ready, "Truyện Parent thứ nhất.");
        var pStory2 = await ApiSeed.ParentStoryAsync(factory, parent.Id, child.Id, StoryStatus.Ready, "Truyện Parent thứ hai khác hẳn.");

        var now = DateTime.UtcNow;
        var createRes = await modClient.PostAsJsonAsync("/api/v1/events", new
        {
            configVersion = "ActivityBased",
            title = "Chiến dịch Sáng tác V2",
            description = "Chỉ bật hoạt động sáng tác cho cả Creator và Parent.",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8",
            budgetVnd = 0L,
            activities = new[]
            {
                new
                {
                    activityType = "Submission",
                    allowedAudience = "Both",
                    registrationStartAt = now.AddHours(-2),
                    registrationEndAt = now.AddDays(3),
                    startAt = now.AddHours(-2),
                    endAt = now.AddDays(3),
                    grantAiCredits = 2,
                    maxSubmissionsPerUser = 1
                }
            },
            awardSlots = new[]
            {
                new
                {
                    activityType = "Submission",
                    resultSource = "SubmissionScore",
                    targetAudience = "Both",
                    rankOrTier = "Giải Sáng tác",
                    slotCount = 1,
                    rewardType = "PhysicalGift",
                    rewardName = "Cúp V2",
                    unitValueVnd = 0L
                }
            }
        });
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<EventDetailDto>>())!.Data!;
        var eventId = created.Id;
        Assert.Equal(EventConfigVersion.ActivityBased, created.ConfigVersion);

        // No ReadingMissions enabled -> saving rules with 0 tasks and submitting approval succeeds.
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/rules", new
        {
            rulesText = "Chỉ cần sáng tác, không có nhiệm vụ đọc.",
            completionMode = "AllMandatory",
            tasks = Array.Empty<object>()
        })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submit-approval", new { })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/approve", new { approved = true })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/publish", new { })).EnsureSuccessStatusCode();

        // Register Parent twice (idempotent, only 1 AI CreditLot created) and register Creator.
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();

        var aiLotsBefore = await factory.WithDbAsync(db => db.CreditLots
            .Where(l => l.UserId == parent.Id && l.EventId == eventId && l.CreditType == CreditType.AI && l.Source == CreditSource.EventGrant)
            .ToListAsync());
        var singleLot = Assert.Single(aiLotsBefore);
        Assert.Equal(2, singleLot.Total);
        Assert.Equal(2, singleLot.Remaining);

        // Parent uses EventGrant AI credit via AIStoryInput endpoint with eventId.
        var aiReq = await parentClient.PostAsJsonAsync("/api/v1/ai-story-input/requests", new
        {
            childProfileId = child.Id,
            eventId,
            idempotencyKey = "v2-parent-ai-" + Guid.NewGuid().ToString("N")[..8],
            topic = "Thế giới cây xanh",
            lesson = "Yêu thiên nhiên",
            vocabularyLevel = "level_2",
            characterMode = "ai_suggested",
            settingMode = "ai_suggested",
            targetLength = 400
        });
        Assert.True(aiReq.IsSuccessStatusCode, $"AI input failed {aiReq.StatusCode}: {await aiReq.Content.ReadAsStringAsync()}");
        var aiLotAfter = await factory.WithDbAsync(db => db.CreditLots.SingleAsync(l => l.Id == singleLot.Id));
        Assert.Equal(1, aiLotAfter.Remaining);

        // Creator submits story without any Submit CreditLot -> waiveSubmitQuota succeeds.
        var creatorSubmit = await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit",
            new { storyId = creatorStoryId });
        Assert.True(creatorSubmit.IsSuccessStatusCode, $"Creator submit failed {creatorSubmit.StatusCode}: {await creatorSubmit.Content.ReadAsStringAsync()}");

        // Parent submits first story without Voting enabled, lists my-submissions, and hits MaxSubmissionsPerUser = 1 on second story.
        var pSub1 = await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit",
            new { storyId = pStory1.Id, consentToEventSharing = true });
        pSub1.EnsureSuccessStatusCode();
        var mySubs = await parentClient.GetFromJsonAsync<ApiEnvelope<List<EventSubmissionDto>>>($"/api/v1/events/{eventId}/my-submissions");
        Assert.Single(mySubs!.Data!);
        Assert.Equal(pStory1.Id, mySubs.Data![0].StoryId);

        var pSub2 = await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit",
            new { storyId = pStory2.Id, consentToEventSharing = true });
        Assert.Equal(HttpStatusCode.Conflict, pSub2.StatusCode);
    }

    [Fact]
    public async Task Archetype2_ReadingMissionsOnly_TasksRequired_MandatoryInPointsMode_ReadinessAndMissionReward()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var author = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        await ApiSeed.ChildAsync(factory, parent.Id);

        var modClient = factory.ClientFor(moderator);
        var adminClient = factory.ClientFor(admin);
        var parentClient = factory.ClientFor(parent);

        int categoryId, curatedStoryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = new ContentCategory { Code = "act-read-" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Đọc V2", IsActive = true };
            db.ContentCategories.Add(cat);
            await db.SaveChangesAsync();
            categoryId = cat.Id;

            var story = new Story
            {
                AuthorUserId = author.Id, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = DateTime.UtcNow,
                OutputMode = StoryOutputMode.ReadingMediaOnly, Title = "Truyện Đọc Mẫu", AgeBand = "6-8", Language = "vi"
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            curatedStoryId = story.Id;
            var ver = new StoryVersion { StoryId = story.Id, VersionNo = 1, IsCurrent = true, Title = story.Title, Content = "Nội dung đọc." };
            ver.AuthoringMetadata.StoryId = story.Id;
            db.StoryVersions.Add(ver);
            await db.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        var createRes = await modClient.PostAsJsonAsync("/api/v1/events", new
        {
            configVersion = "ActivityBased",
            title = "Tuần lễ Đọc truyện V2",
            description = "Chỉ bật ReadingMissions.",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8",
            budgetVnd = 0L,
            activities = new[]
            {
                new
                {
                    activityType = "ReadingMissions",
                    allowedAudience = "Parent",
                    registrationStartAt = now.AddHours(-2),
                    registrationEndAt = now.AddDays(3),
                    startAt = now.AddHours(-2),
                    endAt = now.AddDays(3),
                    waiveWeeklyLibraryLimit = true,
                    storySource = "CuratedOnly"
                }
            },
            awardSlots = new[]
            {
                new
                {
                    activityType = "ReadingMissions",
                    resultSource = "MissionCompletion",
                    targetAudience = "Parent",
                    rankOrTier = "Giải Đọc Tích Cực",
                    slotCount = 1,
                    rewardType = "PhysicalGift",
                    rewardName = "Huy hiệu Đọc",
                    unitValueVnd = 0L
                }
            }
        });
        createRes.EnsureSuccessStatusCode();
        var eventId = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<EventDetailDto>>())!.Data!.Id;

        // ReadingMissions enabled -> 0 tasks blocked on rules save & submit-approval.
        var emptyRulesRes = await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/rules", new
        {
            rulesText = "Cần làm nhiệm vụ.",
            completionMode = "MinimumPointsThreshold",
            requiredPoints = 30,
            tasks = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyRulesRes.StatusCode);
        var missingTasksRes = await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submit-approval", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingTasksRes.StatusCode);

        // Save 1 mandatory task (10pts), 1 optional checkin (40pts), and 1 optional reading task (5pts, triggers curated story readiness).
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/rules", new
        {
            rulesText = "Hoàn thành ít nhất 30 điểm và đủ nhiệm vụ bắt buộc.",
            completionMode = "MinimumPointsThreshold",
            requiredPoints = 30,
            tasks = new object[]
            {
                new { taskCode = "T1", title = "Thảo luận bắt buộc", verifierType = "GuidedDiscussionCheckin", targetValue = 1, isMandatory = true, points = 10, sortOrder = 1 },
                new { taskCode = "T2", title = "Thực hành tùy chọn", verifierType = "RealLifePracticeCheckin", targetValue = 1, isMandatory = false, points = 40, sortOrder = 2 },
                new { taskCode = "T3", title = "Đọc truyện curated", verifierType = "ReadEventStories", targetValue = 1, isMandatory = false, points = 5, sortOrder = 3 }
            }
        })).EnsureSuccessStatusCode();
        // CuratedOnly requires eligible stories before the draft can be sent for approval.
        var preSeedApproval = await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submit-approval", new { });
        Assert.Equal(HttpStatusCode.Conflict, preSeedApproval.StatusCode);
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/seed-stories", new { storyIds = new[] { curatedStoryId } })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submit-approval", new { })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/approve", new { approved = true })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/publish", new { })).EnsureSuccessStatusCode();

        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "ReadingMissions", acceptRules = true })).EnsureSuccessStatusCode();
        var dash = await parentClient.GetFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>($"/api/v1/events/{eventId}/mission-dashboard");
        var mandatoryId = dash!.Data!.Tasks.Single(t => t.TaskCode == "T1").TaskDefinitionId;
        var optionalId = dash.Data!.Tasks.Single(t => t.TaskCode == "T2").TaskDefinitionId;

        // Completing only optional task hits 40 >= 30 pts, yet mandatory check blocks completion in V2!
        var optRes = await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/tasks/{optionalId}/complete",
            new { storyId = curatedStoryId, selfDeclaredPayloadJson = "{\"done\":true}" });
        optRes.EnsureSuccessStatusCode();
        var optDash = (await optRes.Content.ReadFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>())!.Data!;
        Assert.Equal(40, optDash.EarnedPoints);
        Assert.False(optDash.AllRequirementsMet);

        // Now complete mandatory task -> AllRequirementsMet becomes true.
        var manRes = await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/tasks/{mandatoryId}/complete",
            new { storyId = curatedStoryId, selfDeclaredPayloadJson = "{\"done\":true}" });
        manRes.EnsureSuccessStatusCode();
        var manDash = (await manRes.Content.ReadFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>())!.Data!;
        Assert.True(manDash.AllRequirementsMet);

        await factory.WithDbAsync(async db =>
        {
            var act = await db.EventActivities.SingleAsync(a => a.EventId == eventId && a.ActivityType == EventActivityType.ReadingMissions);
            act.EndAt = DateTime.UtcNow.AddMinutes(-1);
            var part = await db.Set<EventParticipation>().SingleAsync(p => p.EventId == eventId && p.EventActivityId == act.Id);
            part.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
            var progresses = await db.Set<EventTaskProgress>().Where(tp => tp.ParticipationId == part.Id).ToListAsync();
            foreach (var tp in progresses) tp.CompletedAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        });

        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/verify", new { })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/propose", new { })).EnsureSuccessStatusCode();
        // Admin chỉ duyệt được sau khi Moderator phụ trách xác nhận danh sách.
        Assert.Equal(HttpStatusCode.Conflict,
            (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/approve", new { })).StatusCode);
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/confirm", new { })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/approve", new { })).EnsureSuccessStatusCode();
        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Set<Event>().SingleAsync(e => e.Id == eventId);
            ev.ResultsPublishedAt = DateTime.UtcNow.AddHours(-49);
            await db.SaveChangesAsync();
        });
        using (var scope = factory.Services.CreateScope())
        {
            var sweep = scope.ServiceProvider.GetRequiredService<IEventResultSweepService>();
            await sweep.SweepEndedEventsAsync();
        }
        var myRewards = await parentClient.GetFromJsonAsync<ApiEnvelope<List<EventAwardSlotDto>>>("/api/v1/events/my-rewards");
        var awardId = Assert.Single(myRewards!.Data!).Id;
        (await parentClient.PostAsJsonAsync($"/api/v1/events/awards/{awardId}/claim",
            new { recipientName = "Parent", phoneNumber = "0901234567", shippingAddress = "HN" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Archetype3_SubmissionReadingAndVoting_SameUserAllActivities_VersionMatchedVoting_SnapshotFreezeAndClaim()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);

        var modClient = factory.ClientFor(moderator);
        var adminClient = factory.ClientFor(admin);
        var creatorClient = factory.ClientFor(creator);
        var parentClient = factory.ClientFor(parent);

        int categoryId, creatorStoryId, seedStoryId, oldVerId, currentVerId, credId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = new ContentCategory { Code = "act-full-" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Tổng hợp V2", IsActive = true };
            db.ContentCategories.Add(cat);
            await db.SaveChangesAsync();
            categoryId = cat.Id;

            var cStory = new Story
            {
                AuthorUserId = creator.Id, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = DateTime.UtcNow,
                OutputMode = StoryOutputMode.ReadingMediaOnly, Title = "Truyện Creator Được Đọc", AgeBand = "6-8", Language = "vi"
            };
            var sStory = new Story
            {
                AuthorUserId = moderator.Id, Scope = StoryScope.Creator, Source = StorySource.Manual,
                Status = StoryStatus.Ready, IsPublished = true, PublishedAt = DateTime.UtcNow,
                OutputMode = StoryOutputMode.ReadingMediaOnly, Title = "Truyện Gợi Ý Đọc Trước", AgeBand = "6-8", Language = "vi"
            };
            db.Stories.AddRange(cStory, sStory);
            await db.SaveChangesAsync();
            creatorStoryId = cStory.Id;
            seedStoryId = sStory.Id;

            var v1 = new StoryVersion { StoryId = cStory.Id, VersionNo = 1, IsCurrent = false, Title = "v1", Content = "Bản cũ", Moderation = new() { ReviewStatus = VersionReviewStatus.Approved } };
            var v2 = new StoryVersion { StoryId = cStory.Id, VersionNo = 2, IsCurrent = true, Title = "v2", Content = "Bản dự thi mới nhất", Moderation = new() { ReviewStatus = VersionReviewStatus.Approved } };
            var sVer = new StoryVersion { StoryId = sStory.Id, VersionNo = 1, IsCurrent = true, Title = "s1", Content = "Truyện gợi ý đọc", Moderation = new() { ReviewStatus = VersionReviewStatus.Approved } };
            v1.AuthoringMetadata.StoryId = cStory.Id;
            v2.AuthoringMetadata.StoryId = cStory.Id;
            sVer.AuthoringMetadata.StoryId = sStory.Id;
            db.StoryVersions.AddRange(v1, v2, sVer);
            var cred = new ChildAccessCredential { ChildProfileId = child.Id, CreatedByUserId = parent.Id };
            db.ChildAccessCredentials.Add(cred);
            await db.SaveChangesAsync();
            oldVerId = v1.Id;
            currentVerId = v2.Id;
            credId = cred.Id;
        }
        var pOwnStory = await ApiSeed.ParentStoryAsync(factory, parent.Id, child.Id, StoryStatus.Ready, "Truyện riêng Parent dự thi.");

        var now = DateTime.UtcNow;
        var createRes = await modClient.PostAsJsonAsync("/api/v1/events", new
        {
            configVersion = "ActivityBased",
            title = "Sự kiện Đầy đủ 3 Hoạt động V2",
            description = "Sáng tác + Đọc + Bình chọn",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8",
            budgetVnd = 0L,
            activities = new object[]
            {
                new
                {
                    activityType = "Submission", allowedAudience = "Both",
                    registrationStartAt = now.AddDays(-2), registrationEndAt = now.AddHours(2),
                    startAt = now.AddDays(-2), endAt = now.AddHours(2), reviewCutoffAt = now.AddHours(2),
                    grantAiCredits = 1, maxSubmissionsPerUser = 2
                },
                new
                {
                    activityType = "ReadingMissions", allowedAudience = "Parent",
                    registrationStartAt = now.AddHours(-1), registrationEndAt = now.AddHours(5),
                    startAt = now.AddHours(-1), endAt = now.AddHours(5),
                    storySource = "Both"
                },
                new
                {
                    activityType = "Voting", allowedAudience = "Parent",
                    registrationStartAt = now.AddHours(-1), registrationEndAt = now.AddHours(5),
                    startAt = now.AddHours(2), endAt = now.AddHours(5),
                    minVotesPerSubmission = 1
                }
            },
            awardSlots = new[]
            {
                new
                {
                    activityType = "Voting", resultSource = "VotingAverage", targetAudience = "Creator",
                    rankOrTier = "Giải Bình chọn", slotCount = 1, rewardType = "PhysicalGift",
                    rewardName = "Cúp Khán Giả", unitValueVnd = 0L
                }
            }
        });
        createRes.EnsureSuccessStatusCode();
        var eventId = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<EventDetailDto>>())!.Data!.Id;

        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/rules", new
        {
            rulesText = "Tham gia đủ 3 hoạt động.",
            completionMode = "AllMandatory",
            tasks = new[]
            {
                new { taskCode = "R1", title = "Đọc truyện dự thi", verifierType = "ReadEventStories", targetValue = 1, isMandatory = true, points = 10, sortOrder = 1 }
            }
        })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/seed-stories", new { storyIds = new[] { seedStoryId } })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submit-approval", new { })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/approve", new { approved = true })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/publish", new { })).EnsureSuccessStatusCode();

        // Same Parent registers for all 3 activities; Creator registers for Submission.
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "ReadingMissions", acceptRules = true })).EnsureSuccessStatusCode();
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Voting", acceptRules = true })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { activityType = "Submission", acceptRules = true })).EnsureSuccessStatusCode();

        // Creator submits Ready+Approved story (links EventStory v2); Parent submits own private story & Moderator approves it.
        (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit", new { storyId = creatorStoryId })).EnsureSuccessStatusCode();
        var pSubRes = await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/activities/submission/submit", new { storyId = pOwnStory.Id, consentToEventSharing = true });
        pSubRes.EnsureSuccessStatusCode();
        var pSubDto = (await pSubRes.Content.ReadFromJsonAsync<ApiEnvelope<EventSubmissionDto>>())!.Data!;
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/submissions/{pSubDto.Id}/review-parent",
            new { storyVersionId = pSubDto.CurrentStoryVersionId, submittedAt = pSubDto.SubmittedAt, approved = true })).EnsureSuccessStatusCode();

        // Transition into active voting window + seed old-version reading session first.
        await factory.WithDbAsync(async db =>
        {
            var sub = await db.EventActivities.SingleAsync(a => a.EventId == eventId && a.ActivityType == EventActivityType.Submission);
            var vote = await db.EventActivities.SingleAsync(a => a.EventId == eventId && a.ActivityType == EventActivityType.Voting);
            sub.EndAt = now.AddMinutes(-40);
            sub.ReviewCutoffAt = now.AddMinutes(-35);
            vote.StartAt = now.AddMinutes(-30);
            vote.EndAt = now.AddMinutes(30);
            db.ReadingSessions.Add(new ReadingSession
            {
                ChildProfileId = child.Id, StoryId = creatorStoryId, StoryVersionId = oldVerId,
                Status = SessionStatus.Completed, StartedAt = now.AddMinutes(-20), CompletedAt = now.AddMinutes(-15),
                ChildAccessCredentialId = credId
            });
            await db.SaveChangesAsync();
        });

        // Wrong StoryVersionId and non-creator-readable ParentSubmission cannot be voted on.
        Assert.Equal(HttpStatusCode.Conflict, (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = creatorStoryId, stars = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = pOwnStory.Id, stars = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await creatorClient.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = creatorStoryId, stars = 5 })).StatusCode);

        // Seed exact StoryVersionId reading session inside voting window -> Parent votes 4, updates to 5.
        await factory.WithDbAsync(async db =>
        {
            db.ReadingSessions.Add(new ReadingSession
            {
                ChildProfileId = child.Id, StoryId = creatorStoryId, StoryVersionId = currentVerId,
                Status = SessionStatus.Completed, StartedAt = now.AddMinutes(-10), CompletedAt = now.AddMinutes(-5),
                ChildAccessCredentialId = credId
            });
            await db.SaveChangesAsync();
        });
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = creatorStoryId, stars = 4 })).EnsureSuccessStatusCode();
        (await parentClient.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = creatorStoryId, stars = 5, comment = "Rất hay" })).EnsureSuccessStatusCode();

        // Leaderboard hidden for Parent during voting window.
        var hiddenLb = await parentClient.GetFromJsonAsync<ApiEnvelope<EventLeaderboardDto>>($"/api/v1/events/{eventId}/leaderboard");
        Assert.True(hiddenLb!.Data!.Hidden);

        // End activities -> Verify -> Propose -> Admin Approve (freezes snapshot) -> Creator Claim.
        await factory.WithDbAsync(async db =>
        {
            var acts = await db.EventActivities.Where(a => a.EventId == eventId).ToListAsync();
            foreach (var a in acts) a.EndAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/verify", new { })).EnsureSuccessStatusCode();
        (await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/awards/propose", new { })).EnsureSuccessStatusCode();
        var approvalUrl = $"/api/v1/events/{eventId}/awards/approve";
        var confirmUrl = $"/api/v1/events/{eventId}/awards/confirm";
        // Chưa xác nhận -> Admin chưa duyệt được.
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsJsonAsync(approvalUrl, new { })).StatusCode);
        // Moderator xác nhận không cần chờ 48h; Admin duyệt = công bố (AppealHold).
        (await modClient.PostAsJsonAsync(confirmUrl, new { })).EnsureSuccessStatusCode();
        var rewardsBeforePublication =
            await creatorClient.GetFromJsonAsync<ApiEnvelope<List<EventAwardSlotDto>>>("/api/v1/events/my-rewards");
        Assert.Empty(rewardsBeforePublication!.Data!);
        (await adminClient.PostAsJsonAsync(approvalUrl, new { })).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync(approvalUrl, new { })).EnsureSuccessStatusCode();

        var evRow = await factory.WithDbAsync(db => db.Set<Event>().SingleAsync(e => e.Id == eventId));
        Assert.False(string.IsNullOrWhiteSpace(evRow.FinalLeaderboardSnapshotJson));
        Assert.NotNull(evRow.ResultsPublishedAt);
        var publishedNotices = await factory.WithDbAsync(db => db.Notifications
            .Where(n => n.Type == NotificationType.EventResultPublished &&
                (n.RecipientUserId == creator.Id || n.RecipientUserId == parent.Id))
            .Select(n => n.RecipientUserId).OrderBy(id => id).ToListAsync());
        Assert.Equal(new[] { creator.Id, parent.Id }.OrderBy(id => id), publishedNotices);

        // Tạm dừng sau khi công bố vẫn phải mở nhận thưởng khi hết hạn khiếu nại.
        await factory.WithDbAsync(async db =>
        {
            var ev = await db.Set<Event>().SingleAsync(e => e.Id == eventId);
            ev.Status = EventStatus.Paused;
            ev.ResultsPublishedAt = DateTime.UtcNow.AddHours(-49);
            await db.SaveChangesAsync();
        });
        using (var scope = factory.Services.CreateScope())
        {
            var sweep = scope.ServiceProvider.GetRequiredService<IEventResultSweepService>();
            await sweep.SweepEndedEventsAsync();
        }

        var creatorRewards = await creatorClient.GetFromJsonAsync<ApiEnvelope<List<EventAwardSlotDto>>>("/api/v1/events/my-rewards");
        var awardId = Assert.Single(creatorRewards!.Data!).Id;
        var claimRes = await creatorClient.PostAsJsonAsync($"/api/v1/events/awards/{awardId}/claim",
            new { recipientName = "Creator", phoneNumber = "0909888777", shippingAddress = "Đà Nẵng" });
        claimRes.EnsureSuccessStatusCode();
        var claimed = (await claimRes.Content.ReadFromJsonAsync<ApiEnvelope<EventAwardSlotDto>>())!.Data!;
        Assert.Equal(EventAwardStatus.FulfillmentProcessing, claimed.Status);
    }
}
