using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoryPlatform.Application.Features.Events.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class SharedCreatorParentEventApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private record ApiEnvelope<T>(bool Success, string? Message, T? Data);

    [Fact]
    public async Task ParentEntryIsPrivateUntilApprovedAndOnlyRegisteredCreatorCanVoteOnce()
    {
        var parent = await factory.AddUserAsync(UserRole.User);
        var child = await ApiSeed.ChildAsync(factory, parent.Id);
        var creator = await factory.AddUserAsync(UserRole.ContentCreator);
        var outsider = await factory.AddUserAsync(UserRole.ContentCreator);
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var assistant = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var parentClient = factory.ClientFor(parent);
        var creatorClient = factory.ClientFor(creator);
        var outsiderClient = factory.ClientFor(outsider);
        var moderatorClient = factory.ClientFor(moderator);
        var assistantClient = factory.ClientFor(assistant);
        var now = DateTime.UtcNow;
        int eventId, storyId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new ContentCategory { Code = $"reverse-{Guid.NewGuid():N}", DisplayName = "Sáng tác", IsActive = true };
            db.ContentCategories.Add(category);
            await db.SaveChangesAsync();
            var campaign = new Event
            {
                Title = "Đọc truyện Parent", Slug = $"reverse-{Guid.NewGuid():N}", Description = "Bình chọn",
                MainCategoryId = category.Id, Status = EventStatus.Published, Format = EventFormat.SharedCreatorParent,
                ScoringMode = EventScoringMode.CreatorVoteAverage, MinVotesPerSubmission = 1,
                CreatedByUserId = moderator.Id, AssignedModeratorId = moderator.Id,
                CreatorVotingScheduleJson = JsonSerializer.Serialize(new EventVotingScheduleDto(
                    now.AddHours(1), now.AddHours(2), now.AddHours(3), now.AddHours(5)))
            };
            var story = new Story
            {
                AuthorUserId = parent.Id, ChildProfileId = child.Id, Scope = StoryScope.Parent, Status = StoryStatus.Ready,
                Title = "Bài Parent", AgeBand = "6-8", Language = "vi"
            };
            db.Set<Event>().Add(campaign);
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            eventId = campaign.Id;
            storyId = story.Id;
            db.StoryVersions.Add(new StoryVersion
            {
                StoryId = storyId, VersionNo = 1, IsCurrent = true, Title = story.Title, Content = "Văn bản dự thi được chốt."
            });
            var rule = new EventRuleVersion { EventId = eventId, IsActive = true, RulesText = "Bài phải được duyệt." };
            var tracks = new[] { EventTrackType.Parent, EventTrackType.Creator }.Select(type => new EventTrack
            {
                EventId = eventId, TrackType = type, RegistrationStartAt = now.AddDays(-1), RegistrationEndAt = now.AddHours(1),
                ExecutionStartAt = now.AddDays(-1), ExecutionEndAt = now.AddDays(1)
            }).ToArray();
            db.Set<EventRuleVersion>().Add(rule);
            db.Set<EventTrack>().AddRange(tracks);
            await db.SaveChangesAsync();
            db.Set<EventParticipation>().AddRange(new EventParticipation
            {
                EventId = eventId, TrackId = tracks[0].Id, AcceptedRuleVersionId = rule.Id, UserId = parent.Id
            }, new EventParticipation
            {
                EventId = eventId, TrackId = tracks[1].Id, AcceptedRuleVersionId = rule.Id, UserId = creator.Id
            });
            await db.SaveChangesAsync();
        }

        var url = $"/api/v1/events/{eventId}";
        var denied = await parentClient.PostAsJsonAsync($"{url}/parent-submit", new { storyId });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var submitResponse = await parentClient.PostAsJsonAsync($"{url}/parent-submit", new { storyId, consentToEventSharing = true });
        submitResponse.EnsureSuccessStatusCode();
        var submission = (await submitResponse.Content.ReadFromJsonAsync<ApiEnvelope<EventSubmissionDto>>())!.Data!;
        Assert.Null(submission.SubmittedText);
        var unapproved = await creatorClient.GetFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>($"{url}/mission-dashboard");
        Assert.Empty(unapproved!.Data!.Stories);

        var review = new { storyVersionId = submission.CurrentStoryVersionId, submission.SubmittedAt, approved = true };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await creatorClient.PostAsJsonAsync($"{url}/submissions/{submission.Id}/review-parent", review)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await assistantClient.PostAsJsonAsync($"{url}/submissions/{submission.Id}/review-parent", review)).StatusCode);
        (await factory.ClientFor(admin).PutAsJsonAsync($"{url}/lead-moderator",
            new { moderatorId = moderator.Id, assistantModeratorIds = new[] { assistant.Id } })).EnsureSuccessStatusCode();
        (await assistantClient.PostAsJsonAsync($"{url}/submissions/{submission.Id}/review-parent", review)).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var campaign = await db.Set<Event>().FindAsync(eventId);
            campaign!.CreatorVotingScheduleJson = JsonSerializer.Serialize(new EventVotingScheduleDto(
                now.AddHours(-3), now.AddHours(-2), now.AddHours(-1), now.AddHours(1)));
            var version = await db.StoryVersions.SingleAsync(row => row.StoryId == storyId);
            version.Content = "Bản riêng đã sửa sau khi nộp.";
            await db.SaveChangesAsync();
        }

        var ballot = await creatorClient.GetFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>($"{url}/mission-dashboard");
        Assert.Equal("Văn bản dự thi được chốt.", Assert.Single(ballot!.Data!.Stories).VotingText);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.PostAsJsonAsync($"{url}/votes", new { storyId, stars = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await parentClient.PostAsJsonAsync($"{url}/votes", new { storyId, stars = 5 })).StatusCode);
        (await creatorClient.PostAsJsonAsync($"{url}/votes", new { storyId, stars = 4 })).EnsureSuccessStatusCode();
        (await creatorClient.PostAsJsonAsync($"{url}/votes", new { storyId, stars = 5 })).EnsureSuccessStatusCode();
        var votes = await moderatorClient.GetFromJsonAsync<ApiEnvelope<List<EventVoteDto>>>($"{url}/votes");
        var vote = Assert.Single(votes!.Data!);
        Assert.Equal(5, vote.Stars);
        Assert.Null(vote.ReadingSessionId);
        Assert.Equal(EventTrackType.Creator, vote.VoterTrackType);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"{url}/leaderboard")).StatusCode);
        var results = await moderatorClient.GetFromJsonAsync<ApiEnvelope<EventLeaderboardDto>>($"{url}/leaderboard");
        Assert.Equal(EventTrackType.Parent, Assert.Single(results!.Data!.Entries).AuthorTrackType);
    }

    [Theory]
    [InlineData(12, false, false, false, HttpStatusCode.OK)]
    [InlineData(16, false, false, false, HttpStatusCode.OK)]
    [InlineData(17, false, false, false, HttpStatusCode.BadRequest)]
    [InlineData(12, true, false, false, HttpStatusCode.OK)]
    [InlineData(12, true, false, true, HttpStatusCode.BadRequest)]
    [InlineData(12, false, true, false, HttpStatusCode.OK)]
    [InlineData(12, true, true, false, HttpStatusCode.OK)]
    public async Task CreateEvent_ValidatesEachRoleCalendarAndVotingWindow(
        int parentStartDay, bool parentVoting, bool creatorVoting, bool votingBeforeReview, HttpStatusCode expected)
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var client = factory.ClientFor(moderator);
        int categoryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new ContentCategory { Code = $"event-{Guid.NewGuid():N}", DisplayName = "Truyện kể", IsActive = true };
            db.ContentCategories.Add(category);
            await db.SaveChangesAsync();
            categoryId = category.Id;
        }

        static DateTime VietnamTime(int day, int hour = 0) =>
            new DateTimeOffset(2026, 12, day, hour, 0, 0, TimeSpan.FromHours(7)).UtcDateTime;

        var end = VietnamTime(17).AddMilliseconds(-1);
        var request = new CreateEventRequestDto
        {
            Title = $"Lịch tham gia {Guid.NewGuid():N}",
            Description = "Lịch theo từng nhóm tham gia",
            MainCategoryId = categoryId,
            Format = parentVoting || creatorVoting ? EventFormat.SharedCreatorParent : EventFormat.Standard,
            ScoringMode = (parentVoting, creatorVoting) switch
            {
                (true, true) => EventScoringMode.CrossRoleVoteAverage,
                (true, false) => EventScoringMode.ParentVoteAverage,
                (false, true) => EventScoringMode.CreatorVoteAverage,
                _ => EventScoringMode.MissionCompletion
            },
            CreatorVotingSchedule = creatorVoting ? new EventVotingScheduleDto(VietnamTime(16, 10), VietnamTime(16, 12),
                VietnamTime(16, 14), end) : null,
            SubmissionCutoffAt = parentVoting ? VietnamTime(16, 9) : null,
            ReviewCutoffAt = parentVoting ? VietnamTime(16, 12) : null,
            VotingStartAt = parentVoting ? VietnamTime(16, votingBeforeReview ? 11 : 13) : null,
            VotingEndAt = parentVoting ? end : null,
            Tracks =
            [
                new()
                {
                    TrackType = EventTrackType.Creator, RegistrationStartAt = VietnamTime(12), RegistrationEndAt = end,
                    ExecutionStartAt = VietnamTime(12), ExecutionEndAt = end
                },
                new()
                {
                    TrackType = EventTrackType.Parent, RegistrationStartAt = VietnamTime(parentStartDay), RegistrationEndAt = end,
                    ExecutionStartAt = parentVoting && !creatorVoting ? VietnamTime(16, 13) : VietnamTime(parentStartDay), ExecutionEndAt = end
                }
            ]
        };

        using var response = await client.PostAsJsonAsync("/api/v1/events", request);
        if (expected == HttpStatusCode.OK)
        {
            response.EnsureSuccessStatusCode();
            var created = (await response.Content.ReadFromJsonAsync<ApiEnvelope<EventDetailDto>>())!.Data!;
            Assert.Equal(request.ScoringMode, created.ScoringMode);
            Assert.Equal(request.Format, created.Format);
            Assert.Equal(request.CreatorVotingSchedule, created.CreatorVotingSchedule);
        }
        else Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SharedCreatorParentEvent_SectionDraftsVotingModerationRankingAndRewards_WorkEndToEnd()
    {
        var moderator = await factory.AddUserAsync(UserRole.Moderator);
        var admin = await factory.AddUserAsync(UserRole.Administrator);
        var creator1 = await factory.AddUserAsync(UserRole.ContentCreator);
        var creator2 = await factory.AddUserAsync(UserRole.ContentCreator);
        var parent1 = await factory.AddUserAsync(UserRole.User);
        var parent2 = await factory.AddUserAsync(UserRole.User);
        var child1 = await ApiSeed.ChildAsync(factory, parent1.Id);
        var child2 = await ApiSeed.ChildAsync(factory, parent2.Id);

        var modClient = factory.ClientFor(moderator);
        var adminClient = factory.ClientFor(admin);
        var c1Client = factory.ClientFor(creator1);
        var c2Client = factory.ClientFor(creator2);
        var p1Client = factory.ClientFor(parent1);
        var p2Client = factory.ClientFor(parent2);

        int categoryId;
        int story1Id, story1OldVerId, story1VerId;
        int story2Id, story2VerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = new ContentCategory { Code = "shared-ev-" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Truyện hay", IsActive = true };
            db.ContentCategories.Add(cat);
            db.Set<SubscriptionPlan>().Add(new SubscriptionPlan
            {
                Name = "Creator event pack", Audience = PlanAudience.Creator, PlanType = PlanType.CreatorPack,
                PriceVnd = 100_000, AiCredits = 5, SubmitCredits = 2
            });
            await db.SaveChangesAsync();
            categoryId = cat.Id;

            var s1 = new Story { AuthorUserId = creator1.Id, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Ready, IsPublished = true, Title = "Truyện 1", AgeBand = "6-8", Language = "vi" };
            var s2 = new Story { AuthorUserId = creator2.Id, Scope = StoryScope.Creator, Source = StorySource.Manual, Status = StoryStatus.Ready, IsPublished = true, Title = "Truyện 2", AgeBand = "6-8", Language = "vi" };
            db.Stories.AddRange(s1, s2);
            await db.SaveChangesAsync();
            story1Id = s1.Id;
            story2Id = s2.Id;

            var s1Old = new StoryVersion { StoryId = s1.Id, VersionNo = 1, IsCurrent = false, Title = "Truyện 1 v1", Content = "Bản cũ", Moderation = new() { ReviewStatus = VersionReviewStatus.Approved } };
            var s1Cur = new StoryVersion { StoryId = s1.Id, VersionNo = 2, IsCurrent = true, Title = "Truyện 1 v2", Content = "Bản chính thức một", Moderation = new() { ReviewStatus = VersionReviewStatus.Approved } };
            var s2Cur = new StoryVersion { StoryId = s2.Id, VersionNo = 1, IsCurrent = true, Title = "Truyện 2 v1", Content = "Bản chính thức hai", Moderation = new() { ReviewStatus = VersionReviewStatus.Approved } };
            db.StoryVersions.AddRange(s1Old, s1Cur, s2Cur);
            await db.SaveChangesAsync();
            story1OldVerId = s1Old.Id;
            story1VerId = s1Cur.Id;
            story2VerId = s2Cur.Id;
        }

        // 1. Moderator creates draft with only title & category, then saves each section separately.
        var createRes = await modClient.PostAsJsonAsync("/api/v1/events", new
        {
            title = "Sự kiện Chung 2026",
            description = "Creator sáng tác, Parent bình chọn",
            mainCategoryId = categoryId,
            targetAgeBands = "6-8,9-10",
            format = "SharedCreatorParent",
            scoringMode = "ParentVoteAverage",
            minVotesPerSubmission = 2,
            budgetVnd = 1_000_000m
        });
        createRes.EnsureSuccessStatusCode();
        var created = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<EventDetailDto>>())!.Data!;
        var eventId = created.Id;

        var now = DateTime.UtcNow;
        var rulesRes = await modClient.PutAsJsonAsync($"/api/v1/events/{eventId}/rules-section", new
        {
            rulesText = "Thể lệ chung cho Creator và Parent.",
            rewardEligibilityText = "Hoàn thành nhiệm vụ để xét thưởng.",
            tieBreakerPolicyText = "Ưu tiên số phiếu hợp lệ cao hơn, rồi nộp sớm hơn.",
            insufficientVotesPolicyText = "Dưới ngưỡng 2 phiếu không xếp giải.",
            scoringMode = "ParentVoteAverage",
            minVotesPerSubmission = 2
        });
        rulesRes.EnsureSuccessStatusCode();

        var scheduleRes = await modClient.PutAsJsonAsync($"/api/v1/events/{eventId}/schedule-section", new
        {
            format = "SharedCreatorParent",
            targetAgeBands = "6-8,9-10",
            maxParentRegistrations = 2,
            submissionCutoffAt = now.AddDays(5),
            reviewCutoffAt = now.AddDays(5),
            votingStartAt = now.AddMinutes(-5), // active for test after submission
            votingEndAt = now.AddDays(10),
            tracks = Array.Empty<object>()
        });
        // VotingStartAt < ReviewCutoffAt must be rejected (400).
        Assert.Equal(HttpStatusCode.BadRequest, scheduleRes.StatusCode);

        scheduleRes = await modClient.PutAsJsonAsync($"/api/v1/events/{eventId}/schedule-section", new
        {
            format = "SharedCreatorParent",
            targetAgeBands = "6-8,9-10",
            maxParentRegistrations = 2,
            submissionCutoffAt = now.AddDays(1),
            reviewCutoffAt = now.AddDays(1),
            votingStartAt = now.AddDays(1),
            votingEndAt = now.AddDays(5),
            tracks = new object[]
            {
                new { trackType = "Creator", registrationStartAt = now.AddDays(-5), registrationEndAt = now.AddDays(4), executionStartAt = now.AddDays(-5), executionEndAt = now.AddDays(5), maxSubmissionsPerUser = 1, grantAiCredits = 0, grantSubmitCredits = 0, waiveWeeklyLibraryLimit = false, claimWindowHours = 72, maxReallocationRounds = 0 },
                new { trackType = "Parent", registrationStartAt = now.AddDays(-5), registrationEndAt = now.AddDays(4), executionStartAt = now.AddDays(1), executionEndAt = now.AddDays(5), maxSubmissionsPerUser = 1, grantAiCredits = 0, grantSubmitCredits = 0, waiveWeeklyLibraryLimit = true, claimWindowHours = 72, maxReallocationRounds = 0 }
            }
        });
        scheduleRes.EnsureSuccessStatusCode();

        var tasksRes = await modClient.PutAsJsonAsync($"/api/v1/events/{eventId}/tasks-section", new
        {
            completionMode = "AllMandatory",
            tasks = new object[]
            {
                new { taskCode = "C01", targetTrackType = "Creator", title = "Nộp 1 truyện được duyệt", description = "Hoàn tất F-10", sortOrder = 1, verifierType = "SubmitApprovedStory", targetValue = 1, isMandatory = true, points = 10, allowMultiplePerStory = false },
                new { taskCode = "P01", targetTrackType = "Parent", title = "Đọc và đánh giá 2 truyện", description = "Bình chọn 1-5 sao", sortOrder = 2, verifierType = "VoteEventStories", targetValue = 2, isMandatory = true, points = 10, allowMultiplePerStory = false }
            }
        });
        tasksRes.EnsureSuccessStatusCode();

        // Awards: CreatorPackage (2 slots) + Parent PlatformCredits (2 slots matching maxParentRegistrations = 2)
        var awardsRes = await modClient.PutAsJsonAsync($"/api/v1/events/{eventId}/awards-section", new
        {
            budgetVnd = 500_000m,
            awardAllParentMissionCompleters = true,
            awardSlots = new object[]
            {
                new { trackType = "Creator", rankOrTier = "Giải Nhất", slotCount = 2, rewardType = "CreatorPackage", rewardName = "Creator event pack", unitValueVnd = 100_000m, creditGrantAmount = 99, submitCreditGrantAmount = 99 },
                new { trackType = "Parent", rankOrTier = "Hoàn thành", slotCount = 2, rewardType = "PlatformCredits", rewardName = "Quà AI", unitValueVnd = 50_000m, creditGrantAmount = 3, submitCreditGrantAmount = 0 }
            }
        });
        awardsRes.EnsureSuccessStatusCode();

        (await modClient.PostAsync($"/api/v1/events/{eventId}/submit-approval", null)).EnsureSuccessStatusCode();
        (await adminClient.PostAsJsonAsync($"/api/v1/events/{eventId}/approve", new { approved = true })).EnsureSuccessStatusCode();

        // Publish with pre-seeded stories or creator submissions
        await modClient.PostAsJsonAsync($"/api/v1/events/{eventId}/seed-stories", new { storyIds = new[] { story1Id, story2Id } });
        (await modClient.PostAsync($"/api/v1/events/{eventId}/publish", null)).EnsureSuccessStatusCode();

        // 2. Simultaneous registration + submission
        (await c1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { trackType = "Creator", acceptRules = true })).EnsureSuccessStatusCode();
        (await c2Client.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { trackType = "Creator", acceptRules = true })).EnsureSuccessStatusCode();
        (await p1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { trackType = "Parent", acceptRules = true })).EnsureSuccessStatusCode();
        (await p2Client.PostAsJsonAsync($"/api/v1/events/{eventId}/register", new { trackType = "Parent", acceptRules = true })).EnsureSuccessStatusCode();

        (await c1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/creator-submit", new { storyId = story1Id })).EnsureSuccessStatusCode();
        (await c2Client.PostAsJsonAsync($"/api/v1/events/{eventId}/creator-submit", new { storyId = story2Id })).EnsureSuccessStatusCode();

        // Creator sees Creator mission dashboard
        var c1Dash = (await (await c1Client.GetAsync($"/api/v1/events/{eventId}/mission-dashboard")).Content.ReadFromJsonAsync<ApiEnvelope<EventMissionDashboardDto>>())!.Data!;
        Assert.Single(c1Dash.Tasks);
        Assert.True(c1Dash.AllRequirementsMet);

        // 3. Reading evidence & voting (open voting window now that submissions are locked)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ev = await db.Events.FindAsync(eventId);
            ev!.VotingStartAt = now.AddMinutes(-5);
            foreach (var tr in db.EventTracks.Where(t => t.EventId == eventId && t.TrackType == EventTrackType.Parent))
                tr.ExecutionStartAt = now.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        // Vote without reading -> 409
        var noReadVote = await p1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = story1Id, stars = 5 });
        Assert.Equal(HttpStatusCode.Conflict, noReadVote.StatusCode);

        // Read wrong version -> 409; read exact versions -> succeeds
        int cred1Id, cred2Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cred1 = new ChildAccessCredential { ChildProfileId = child1.Id, CreatedByUserId = parent1.Id };
            var cred2 = new ChildAccessCredential { ChildProfileId = child2.Id, CreatedByUserId = parent2.Id };
            db.ChildAccessCredentials.AddRange(cred1, cred2);
            await db.SaveChangesAsync();
            cred1Id = cred1.Id;
            cred2Id = cred2.Id;

            db.ReadingSessions.Add(new ReadingSession { ChildProfileId = child1.Id, StoryId = story1Id, StoryVersionId = story1OldVerId, Status = SessionStatus.Completed, StartedAt = now, CompletedAt = now, ChildAccessCredentialId = cred1Id });
            await db.SaveChangesAsync();
        }
        var wrongVerVote = await p1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = story1Id, stars = 5 });
        Assert.Equal(HttpStatusCode.Conflict, wrongVerVote.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ReadingSessions.AddRange(
                new ReadingSession { ChildProfileId = child1.Id, StoryId = story1Id, StoryVersionId = story1VerId, Status = SessionStatus.Completed, StartedAt = now, CompletedAt = now, ChildAccessCredentialId = cred1Id },
                new ReadingSession { ChildProfileId = child1.Id, StoryId = story2Id, StoryVersionId = story2VerId, Status = SessionStatus.Completed, StartedAt = now, CompletedAt = now, ChildAccessCredentialId = cred1Id },
                new ReadingSession { ChildProfileId = child2.Id, StoryId = story1Id, StoryVersionId = story1VerId, Status = SessionStatus.Completed, StartedAt = now, CompletedAt = now, ChildAccessCredentialId = cred2Id });
            await db.SaveChangesAsync();
        }

        // Parent1 votes story1 (4 -> updated to 5) & story2 (4); Parent2 votes story1 (5).
        (await p1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = story1Id, stars = 4 })).EnsureSuccessStatusCode();
        (await p1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = story1Id, stars = 5, comment = "Rất hay" })).EnsureSuccessStatusCode();
        (await p1Client.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = story2Id, stars = 4 })).EnsureSuccessStatusCode();
        (await p2Client.PostAsJsonAsync($"/api/v1/events/{eventId}/votes", new { storyId = story1Id, stars = 5 })).EnsureSuccessStatusCode();

        // Leaderboard is hidden for Parent during voting
        var hiddenLb = (await (await p1Client.GetAsync($"/api/v1/events/{eventId}/leaderboard")).Content.ReadFromJsonAsync<ApiEnvelope<EventLeaderboardDto>>())!.Data!;
        Assert.True(hiddenLb.Hidden);
        Assert.Empty(hiddenLb.Entries);
        var creatorSubmission = (await (await c1Client.GetAsync($"/api/v1/events/{eventId}/my-submission"))
            .Content.ReadFromJsonAsync<ApiEnvelope<EventSubmissionDto>>())!.Data!;
        Assert.Equal(0, creatorSubmission.ValidVoteCount);
        Assert.Null(creatorSubmission.AverageStars);

        // Close voting window in DB to run verification, ranking & rewards
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ev = await db.Events.FindAsync(eventId);
            ev!.VotingEndAt = DateTime.UtcNow.AddSeconds(-5);
            foreach (var tr in db.EventTracks.Where(t => t.EventId == eventId))
                tr.ExecutionEndAt = DateTime.UtcNow.AddSeconds(-5);
            await db.SaveChangesAsync();
        }

        // Verify: creator1 & creator2 & parent1 are Verified; story1 has 2 votes (meets threshold 2), story2 has 1 vote (below threshold)
        (await modClient.PostAsync($"/api/v1/events/{eventId}/verify", null)).EnsureSuccessStatusCode();
        var proposed = (await (await modClient.PostAsync($"/api/v1/events/{eventId}/awards/propose", null)).Content.ReadFromJsonAsync<ApiEnvelope<List<EventAwardSlotDto>>>())!.Data!;
        var creatorAwards = proposed.Where(a => a.RewardType == EventRewardType.CreatorPackage).ToList();
        Assert.Equal(1, creatorAwards.Count(a => a.ParticipationId.HasValue)); // 2nd slot left unassigned!

        var approvalUrl = $"/api/v1/events/{eventId}/awards/approve";
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsJsonAsync(approvalUrl, new { })).StatusCode);
        // Advance only the test fixture's verification timestamp past the appeal window.
        await factory.WithDbAsync(async db =>
        {
            var failed = await db.Set<EventParticipation>().Where(p => p.EventId == eventId &&
                p.Status == EventParticipationStatus.VerificationFailed).ToListAsync();
            Assert.NotEmpty(failed);
            foreach (var p in failed) p.VerifiedAt = DateTime.UtcNow.AddHours(-49);
            await db.SaveChangesAsync();
        });
        var approved = (await (await adminClient.PostAsync($"/api/v1/events/{eventId}/awards/approve", null)).Content.ReadFromJsonAsync<ApiEnvelope<List<EventAwardSlotDto>>>())!.Data!;
        var creator1Award = approved.Single(a => a.RewardType == EventRewardType.CreatorPackage);
        var storedVotes = (await (await modClient.GetAsync($"/api/v1/events/{eventId}/votes"))
            .Content.ReadFromJsonAsync<ApiEnvelope<List<EventVoteDto>>>())!.Data!;
        var frozenVote = await modClient.PostAsJsonAsync($"/api/v1/events/votes/{storedVotes[0].Id}/disqualify",
            new { reason = "Vote audit test" });
        Assert.Equal(HttpStatusCode.Conflict, frozenVote.StatusCode);

        // Claim CreatorPackage twice -> grants 5 AI + 2 Submit credits once; second claim returns 409 Conflict
        (await c1Client.PostAsJsonAsync($"/api/v1/events/awards/{creator1Award.Id}/claim", new { })).EnsureSuccessStatusCode();
        var secondClaim = await c1Client.PostAsJsonAsync($"/api/v1/events/awards/{creator1Award.Id}/claim", new { });
        Assert.Equal(HttpStatusCode.Conflict, secondClaim.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var lots = await db.CreditLots.Where(l => l.UserId == creator1.Id && l.Source == CreditSource.EventReward && l.EventId == eventId).ToListAsync();
            Assert.Equal(5, lots.Where(l => l.CreditType == CreditType.AI).Sum(l => l.Total));
            Assert.Equal(2, lots.Where(l => l.CreditType == CreditType.Submit).Sum(l => l.Total));
        }

        // Summary trial metrics
        var sum = (await (await modClient.GetAsync($"/api/v1/events/{eventId}/summary")).Content.ReadFromJsonAsync<ApiEnvelope<EventReportSummaryDto>>())!.Data!;
        Assert.Equal(2, sum.FamiliesCompletedFirstRead);
        Assert.Equal(0.5m, sum.SecondStoryReturnRate);
        Assert.Equal(0.5m, sum.SubmissionsMeetingVoteThresholdRate);
    }
}
