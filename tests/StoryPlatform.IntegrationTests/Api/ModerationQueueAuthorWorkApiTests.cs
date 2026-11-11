using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using static StoryPlatform.IntegrationTests.Api.AuthApiTests;

namespace StoryPlatform.IntegrationTests.Api;

public sealed class ModerationQueueAuthorWorkApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreatorSubmitWithdrawResubmit_ModeratorQueueClaimOpenAndPackage_ShowVersionTitleAndCreatorWithoutEmail()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator, "creator_real_flow");
        var moderator = await factory.AddUserAsync(UserRole.Moderator, "mod_real_flow");
        var reporter = await factory.AddUserAsync(UserRole.User, "parent_reporter_secret");

        await factory.WithDbAsync(async db =>
        {
            var c = await db.Users.FindAsync(creator.Id);
            c!.FullName = "Nguyễn Văn Tác Giả";
            var r = await db.Users.FindAsync(reporter.Id);
            r!.FullName = "Phụ Huynh Ẩn Danh";

            db.ContentCategories.Add(new ContentCategory
            {
                Id = 905,
                Code = "family_test",
                DisplayName = "Gia đình",
                IsActive = true
            });

            var story = new Story
            {
                Id = 944,
                AuthorUserId = creator.Id,
                Scope = StoryScope.Creator,
                Source = StorySource.Manual,
                Status = StoryStatus.Ready,
                Title = "Tên Story Đã Sửa Mới Hơn",
                CoverImageUrl = "https://cdn.test/cover.png",
                AgeBand = "6-8",
                Language = "vi"
            };
            db.Stories.Add(story);

            var version = new StoryVersion
            {
                Id = 9100,
                StoryId = story.Id,
                VersionNo = 1,
                IsCurrent = true,
                Title = "Mái Nhà Ấm Áp Sau Cơn Mưa",
                Content = "Cảnh 1",
                SafetyScore = 100m
            };
            version.AuthoringMetadata.StoryId = story.Id;
            db.StoryVersions.Add(version);

            var scene = new StoryScene
            {
                Id = 9200,
                StoryVersionId = version.Id,
                SceneIndex = 1,
                TextRangeStart = 0,
                TextRangeEnd = 6,
                SceneText = "Cảnh 1"
            };
            db.StoryScenes.Add(scene);

            var beat = new IllustrationBeat
            {
                Id = 9300,
                StorySceneId = scene.Id,
                BeatOrder = 1,
                StartOffset = 0,
                EndOffset = 6,
                VisualFocus = "Cảnh ấm áp"
            };
            db.IllustrationBeats.Add(beat);

            var segment = new StorySegment
            {
                Id = 9400,
                StorySceneId = scene.Id,
                SegmentOrder = 1,
                StartOffset = 0,
                EndOffset = 6,
                TextContent = "Cảnh 1"
            };
            db.StorySegments.Add(segment);

            db.MediaAssets.AddRange(
                new MediaAsset
                {
                    Id = 9500,
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    IllustrationBeatId = beat.Id,
                    Type = MediaType.Illustration,
                    Status = MediaStatus.Ready,
                    ValidationStatus = ValidationStatus.Passed,
                    IsCurrent = true,
                    Url = "https://cdn.test/img.png"
                },
                new MediaAsset
                {
                    Id = 9501,
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    StorySegmentId = segment.Id,
                    Type = MediaType.TtsAudio,
                    Status = MediaStatus.Ready,
                    IsCurrent = true,
                    Url = "https://cdn.test/audio.mp3"
                });

            var report = new ContentReport
            {
                Id = 9001,
                StoryId = story.Id,
                StoryVersionId = version.Id,
                ReporterUserId = reporter.Id,
                Reason = ContentReportReason.Other
            };
            db.ContentReports.Add(report);

            db.ModerationItems.Add(new ModerationItem
            {
                Id = 9600,
                ItemType = ModerationItemType.ContentReport,
                Status = ModerationItemStatus.Pending,
                Priority = ModerationPriority.High,
                StoryId = story.Id,
                StoryVersionId = version.Id,
                ContentReportId = report.Id,
                CreatedByUserId = reporter.Id
            });

            await db.SaveChangesAsync();
            return true;
        });

        var creatorClient = factory.ClientFor(creator);
        var submit1 = await creatorClient.PostAsJsonAsync("/api/v1/PublicStorySubmission/submit", new
        {
            storyId = 944,
            storyVersionId = 9100,
            contentCategoryIds = new[] { 905 }
        });
        var submit1Body = await submit1.Content.ReadAsStringAsync();
        Assert.True(submit1.StatusCode == HttpStatusCode.OK, submit1Body);
        var firstItemId = JsonDocument.Parse(submit1Body).RootElement.GetProperty("data").GetProperty("itemId").GetInt32();

        var withdraw = await creatorClient.PostAsync($"/api/v1/PublicStorySubmission/{firstItemId}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, withdraw.StatusCode);

        var submit2 = await creatorClient.PostAsJsonAsync("/api/v1/PublicStorySubmission/submit", new
        {
            storyId = 944,
            storyVersionId = 9100,
            contentCategoryIds = new[] { 905 }
        });
        Assert.Equal(HttpStatusCode.OK, submit2.StatusCode);
        var resubmittedItemId = (await ReadJsonAsync(submit2)).GetProperty("data").GetProperty("itemId").GetInt32();

        var modClient = factory.ClientFor(moderator);

        // Chưa nhận mục -> không mở được gói duyệt bản thảo.
        var prematureOpen = await modClient.GetAsync($"/api/v1/PublicStoryReview/{resubmittedItemId}");
        Assert.Equal(HttpStatusCode.Forbidden, prematureOpen.StatusCode);

        // Hàng đợi trả đúng tên phiên bản, tên Creator (không email) cho PublicStoryReview; ẩn danh tính ở ContentReport.
        var listResp = await modClient.GetAsync("/api/v1/ModerationQueue?pageIndex=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listRaw = await listResp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(creator.Email, listRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Phụ Huynh Ẩn Danh", listRaw, StringComparison.OrdinalIgnoreCase);

        var items = JsonDocument.Parse(listRaw).RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var publicRow = items.Single(x => x.GetProperty("id").GetInt32() == resubmittedItemId);
        Assert.Equal("Mái Nhà Ấm Áp Sau Cơn Mưa", publicRow.GetProperty("storyTitle").GetString());
        Assert.Equal(creator.Id, publicRow.GetProperty("createdByUserId").GetInt32());
        Assert.Equal("Nguyễn Văn Tác Giả", publicRow.GetProperty("createdByName").GetString());
        Assert.True(publicRow.GetProperty("isResubmission").GetBoolean());

        var reportRow = items.Single(x => x.GetProperty("id").GetInt32() == 9600);
        Assert.Equal(JsonValueKind.Null, reportRow.GetProperty("createdByUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, reportRow.GetProperty("createdByName").ValueKind);

        // Claim + Open + GetPackage đều trả thống nhất tên tác phẩm phiên bản gửi duyệt & tác giả.
        var claimResp = await modClient.PostAsync($"/api/v1/ModerationQueue/{resubmittedItemId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claimResp.StatusCode);
        var claimData = (await ReadJsonAsync(claimResp)).GetProperty("data");
        Assert.Equal("Mái Nhà Ấm Áp Sau Cơn Mưa", claimData.GetProperty("storyTitle").GetString());
        Assert.Equal("Nguyễn Văn Tác Giả", claimData.GetProperty("createdByName").GetString());

        var openResp = await modClient.GetAsync($"/api/v1/ModerationQueue/{resubmittedItemId}");
        Assert.Equal(HttpStatusCode.OK, openResp.StatusCode);
        var openData = (await ReadJsonAsync(openResp)).GetProperty("data");
        Assert.Equal("Mái Nhà Ấm Áp Sau Cơn Mưa", openData.GetProperty("storyTitle").GetString());
        Assert.Equal("Nguyễn Văn Tác Giả", openData.GetProperty("createdByName").GetString());

        var pkgResp = await modClient.GetAsync($"/api/v1/PublicStoryReview/{resubmittedItemId}");
        Assert.Equal(HttpStatusCode.OK, pkgResp.StatusCode);
        var pkgData = (await ReadJsonAsync(pkgResp)).GetProperty("data");
        Assert.Equal("Mái Nhà Ấm Áp Sau Cơn Mưa", pkgData.GetProperty("version").GetProperty("title").GetString());
        Assert.Equal(creator.Id, pkgData.GetProperty("createdByUserId").GetInt32());
        Assert.Equal("Nguyễn Văn Tác Giả", pkgData.GetProperty("createdByName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(pkgData.GetProperty("submittedAt").GetString()));
    }

    [Fact]
    public async Task RequestChanges_ListeningAndRoadmapStory_StoresExactTargetsAndDeliversToCreator()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator, "creator_accept_cr");
        var roadmapOwner = await factory.AddUserAsync(UserRole.Moderator, "mod_owner_cr");
        var reviewer = await factory.AddUserAsync(UserRole.Moderator, "mod_reviewer_cr");

        await factory.WithDbAsync(async db =>
        {
            db.ContentCategories.Add(new ContentCategory
            {
                Id = 906,
                Code = "nature_cr_accept",
                DisplayName = "Thiên nhiên nghiệm thu",
                IsActive = true
            });

            var draftStory = new Story
            {
                Id = 945,
                AuthorUserId = creator.Id,
                Scope = StoryScope.Creator,
                Source = StorySource.Manual,
                Status = StoryStatus.Ready,
                Title = "Truyện tương tác nghiệm thu",
                CoverImageUrl = "https://cdn.test/cover-accept.png",
                AgeBand = "6-8",
                Language = "vi"
            };
            db.Stories.Add(draftStory);

            var version = new StoryVersion
            {
                Id = 9110,
                StoryId = draftStory.Id,
                VersionNo = 1,
                IsCurrent = true,
                Title = "Truyện tương tác nghiệm thu",
                Content = "Đoạn mở đầu trời mưa.",
                SafetyScore = 100m
            };
            version.AuthoringMetadata.StoryId = draftStory.Id;
            db.StoryVersions.Add(version);

            var scene = new StoryScene
            {
                Id = 9210,
                StoryVersionId = version.Id,
                SceneIndex = 0,
                TextRangeStart = 0,
                TextRangeEnd = 20,
                SceneText = "Đoạn mở đầu trời mưa."
            };
            db.StoryScenes.Add(scene);

            var beat = new IllustrationBeat
            {
                Id = 9310,
                StorySceneId = scene.Id,
                BeatOrder = 1,
                StartOffset = 0,
                EndOffset = 20,
                VisualFocus = "Trời mưa ngoài hiên"
            };
            db.IllustrationBeats.Add(beat);

            var segment = new StorySegment
            {
                Id = 9410,
                StorySceneId = scene.Id,
                SegmentOrder = 1,
                StartOffset = 0,
                EndOffset = 20,
                TextContent = "Đoạn mở đầu trời mưa."
            };
            db.StorySegments.Add(segment);

            db.MediaAssets.AddRange(
                new MediaAsset
                {
                    Id = 9510,
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    IllustrationBeatId = beat.Id,
                    Type = MediaType.Illustration,
                    Status = MediaStatus.Ready,
                    ValidationStatus = ValidationStatus.Passed,
                    IsCurrent = true,
                    Url = "https://cdn.test/accept-img.png"
                },
                new MediaAsset
                {
                    Id = 9511,
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    StorySegmentId = segment.Id,
                    Type = MediaType.TtsAudio,
                    Status = MediaStatus.Ready,
                    IsCurrent = true,
                    Url = "https://cdn.test/accept-audio.mp3"
                });

            db.ListeningInteractions.Add(new ListeningInteraction
            {
                Id = 9710,
                StoryVersionId = version.Id,
                StorySceneId = scene.Id,
                Type = ListeningInteractionType.FillBlank,
                AnchorOffset = 20,
                Prompt = "Bé nghe thấy tiếng gì ngoài hiên?",
                SourceQuote = "Đoạn mở đầu trời mưa.",
                HiddenWord = "mưa"
            });

            foreach (var (id, title) in new[] { (946, "Truyện mẫu 1"), (947, "Truyện mẫu 2") })
            {
                db.Stories.Add(new Story
                {
                    Id = id,
                    AuthorUserId = creator.Id,
                    Scope = StoryScope.Creator,
                    Source = StorySource.Manual,
                    Status = StoryStatus.Ready,
                    IsPublished = true,
                    PublishedAt = DateTime.UtcNow.AddDays(-1),
                    Title = title,
                    AgeBand = "6-8",
                    ReadingLevel = 2,
                    Language = "vi"
                });
                db.StoryCategories.Add(new StoryCategory
                {
                    StoryId = id,
                    ContentCategoryId = 906,
                    Source = TagSource.Creator
                });
            }

            await db.SaveChangesAsync();
            return true;
        });

        var creatorClient = factory.ClientFor(creator);
        var ownerClient = factory.ClientFor(roadmapOwner);
        var reviewerClient = factory.ClientFor(reviewer);

        // 1. PublicStoryReview: gửi yêu cầu sửa một tương tác khi nghe (listening partId = 9710).
        var submitStory = await creatorClient.PostAsJsonAsync("/api/v1/PublicStorySubmission/submit", new
        {
            storyId = 945,
            storyVersionId = 9110,
            contentCategoryIds = new[] { 906 }
        });
        var submitStoryBody = await submitStory.Content.ReadAsStringAsync();
        Assert.True(submitStory.StatusCode == HttpStatusCode.OK, submitStoryBody);
        var storyReviewItemId = JsonDocument.Parse(submitStoryBody).RootElement.GetProperty("data").GetProperty("itemId").GetInt32();

        var claimStory = await reviewerClient.PostAsync($"/api/v1/ModerationQueue/{storyReviewItemId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claimStory.StatusCode);

        var storyPkg = await reviewerClient.GetAsync($"/api/v1/PublicStoryReview/{storyReviewItemId}");
        Assert.Equal(HttpStatusCode.OK, storyPkg.StatusCode);
        var pkgJson = (await ReadJsonAsync(storyPkg)).GetProperty("data");
        var listening = Assert.Single(pkgJson.GetProperty("listeningInteractions").EnumerateArray());
        Assert.Equal(9710, listening.GetProperty("id").GetInt32());

        const string listeningNote = "Cần đổi câu gợi ý điền từ cho rõ hơn.";
        var storyChangeResp = await reviewerClient.PostAsJsonAsync(
            $"/api/v1/PublicStoryReview/{storyReviewItemId}/request-changes",
            new
            {
                generalNote = listeningNote,
                parts = new[]
                {
                    new { partType = "listening", partId = 9710, note = listeningNote }
                }
            });
        var storyChangeBody = await storyChangeResp.Content.ReadAsStringAsync();
        Assert.True(storyChangeResp.StatusCode == HttpStatusCode.OK, storyChangeBody);
        Assert.Equal("ChangesRequested", JsonDocument.Parse(storyChangeBody).RootElement.GetProperty("data").GetProperty("decision").GetString());

        var pubStatusResp = await creatorClient.GetAsync("/api/v1/creator/stories/945/publication");
        Assert.Equal(HttpStatusCode.OK, pubStatusResp.StatusCode);
        var pubStatus = (await ReadJsonAsync(pubStatusResp)).GetProperty("data");
        Assert.Equal("ChangesRequested", pubStatus.GetProperty("reviewStatus").GetString());
        Assert.False(pubStatus.GetProperty("canResubmitLabels").GetBoolean());

        var creatorNotifResp = await creatorClient.GetAsync("/api/v1/Notification?group=moderation");
        Assert.Equal(HttpStatusCode.OK, creatorNotifResp.StatusCode);
        var creatorNotifs = (await ReadJsonAsync(creatorNotifResp)).GetProperty("data").EnumerateArray().ToList();
        Assert.Contains(creatorNotifs, n =>
        {
            if (n.GetProperty("type").GetString() != "ContentModerationResult") return false;
            using var p = JsonDocument.Parse(n.GetProperty("payload").GetString()!);
            return p.RootElement.GetProperty("itemId").GetInt32() == storyReviewItemId
                && p.RootElement.GetProperty("decision").GetString() == "changes_requested"
                && p.RootElement.GetProperty("reason").GetString() == listeningNote;
        });

        await factory.WithDbAsync(async db =>
        {
            var savedItem = await db.ModerationItems.FindAsync(storyReviewItemId);
            var savedVersion = await db.StoryVersions.FindAsync(9110);
            Assert.NotNull(savedItem?.Feedback);
            Assert.Equal(savedItem!.Feedback, savedVersion!.Moderation.ReviewFeedback);
            using var doc = JsonDocument.Parse(savedItem.Feedback!);
            var part = Assert.Single(doc.RootElement.GetProperty("parts").EnumerateArray());
            Assert.Equal("listening", part.GetProperty("partType").GetString());
            Assert.Equal(9710, part.GetProperty("partId").GetInt32());
            Assert.Equal(listeningNote, part.GetProperty("note").GetString());
            return true;
        });

        // 2. RoadmapTemplateReview: gửi yêu cầu sửa một truyện trong lộ trình (storyId = 947, action = replace).
        var createRoadmap = await ownerClient.PostAsJsonAsync("/api/v1/RoadmapTemplate", new
        {
            name = "Lộ trình nghiệm thu",
            description = "Mô tả lộ trình",
            ageBand = "6-8",
            mainCategoryId = 906,
            readingLevelMin = 1,
            readingLevelMax = 3,
            defaultDaysPerStory = 5
        });
        var createRoadmapBody = await createRoadmap.Content.ReadAsStringAsync();
        Assert.True(createRoadmap.StatusCode == HttpStatusCode.OK, createRoadmapBody);
        var roadmapId = JsonDocument.Parse(createRoadmapBody).RootElement.GetProperty("data").GetProperty("id").GetInt32();

        var setItems = await ownerClient.PutAsJsonAsync($"/api/v1/RoadmapTemplate/{roadmapId}/items", new
        {
            items = new[]
            {
                new { storyId = 946, days = 4 },
                new { storyId = 947, days = 5 }
            }
        });
        Assert.Equal(HttpStatusCode.OK, setItems.StatusCode);

        var submitRoadmap = await ownerClient.PostAsync($"/api/v1/RoadmapTemplate/{roadmapId}/submit", null);
        var submitRoadmapBody = await submitRoadmap.Content.ReadAsStringAsync();
        Assert.True(submitRoadmap.StatusCode == HttpStatusCode.OK, submitRoadmapBody);
        var roadmapItemId = JsonDocument.Parse(submitRoadmapBody).RootElement.GetProperty("data").GetProperty("itemId").GetInt32();

        var claimRoadmap = await reviewerClient.PostAsync($"/api/v1/ModerationQueue/{roadmapItemId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, claimRoadmap.StatusCode);

        const string roadmapStoryNote = "Thay truyện thứ hai bằng truyện nhịp đọc chậm hơn.";
        var roadmapChangeResp = await reviewerClient.PostAsJsonAsync(
            $"/api/v1/RoadmapReview/{roadmapItemId}/request-changes",
            new
            {
                generalNote = roadmapStoryNote,
                parts = new[]
                {
                    new { partType = "story", storyId = 947, action = "replace", note = roadmapStoryNote }
                }
            });
        var roadmapChangeBody = await roadmapChangeResp.Content.ReadAsStringAsync();
        Assert.True(roadmapChangeResp.StatusCode == HttpStatusCode.OK, roadmapChangeBody);
        Assert.Equal("ChangesRequested", JsonDocument.Parse(roadmapChangeBody).RootElement.GetProperty("data").GetProperty("decision").GetString());

        var ownerDetailResp = await ownerClient.GetAsync($"/api/v1/RoadmapTemplate/{roadmapId}");
        Assert.Equal(HttpStatusCode.OK, ownerDetailResp.StatusCode);
        var lastReview = (await ReadJsonAsync(ownerDetailResp)).GetProperty("data").GetProperty("lastReview");
        Assert.Equal(roadmapStoryNote, lastReview.GetProperty("generalNote").GetString());
        var rmPart = Assert.Single(lastReview.GetProperty("parts").EnumerateArray());
        Assert.Equal("story", rmPart.GetProperty("partType").GetString());
        Assert.Equal(947, rmPart.GetProperty("storyId").GetInt32());
        Assert.Equal("replace", rmPart.GetProperty("action").GetString());
        Assert.Equal(roadmapStoryNote, rmPart.GetProperty("note").GetString());
    }

    [Fact]
    public async Task CreatorStoriesList_ReturnsRejectedReviewStatusAfterModeratorRejects()
    {
        var creator = await factory.AddUserAsync(UserRole.ContentCreator, "creator_rejected_list");
        var moderator = await factory.AddUserAsync(UserRole.Moderator, "mod_rejected_list");

        await factory.WithDbAsync(async db =>
        {
            db.ContentCategories.Add(new ContentCategory
            {
                Id = 907,
                Code = "family_rejected_list",
                DisplayName = "Gia đình",
                IsActive = true
            });

            var story = new Story
            {
                Id = 948,
                AuthorUserId = creator.Id,
                Scope = StoryScope.Creator,
                Source = StorySource.Ai,
                Status = StoryStatus.Ready,
                Title = "Mái Nhà Ấm Áp Sau Cơn Mưa",
                CoverImageUrl = "https://cdn.test/cover-948.png",
                AgeBand = "9-10",
                Language = "vi"
            };
            db.Stories.Add(story);

            var version = new StoryVersion
            {
                Id = 9120,
                StoryId = story.Id,
                VersionNo = 4,
                IsCurrent = true,
                Title = "Mái Nhà Ấm Áp Sau Cơn Mưa",
                Content = "Nội dung V4",
                SafetyScore = 100m
            };
            version.AuthoringMetadata.StoryId = story.Id;
            db.StoryVersions.Add(version);

            var scene = new StoryScene
            {
                Id = 9220,
                StoryVersionId = version.Id,
                SceneIndex = 0,
                TextRangeStart = 0,
                TextRangeEnd = 11,
                SceneText = "Nội dung V4"
            };
            db.StoryScenes.Add(scene);

            var beat = new IllustrationBeat
            {
                Id = 9320,
                StorySceneId = scene.Id,
                BeatOrder = 1,
                StartOffset = 0,
                EndOffset = 11,
                VisualFocus = "Cảnh V4"
            };
            db.IllustrationBeats.Add(beat);

            var segment = new StorySegment
            {
                Id = 9420,
                StorySceneId = scene.Id,
                SegmentOrder = 1,
                StartOffset = 0,
                EndOffset = 11,
                TextContent = "Nội dung V4"
            };
            db.StorySegments.Add(segment);

            db.MediaAssets.AddRange(
                new MediaAsset
                {
                    Id = 9520,
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    IllustrationBeatId = beat.Id,
                    Type = MediaType.Illustration,
                    Status = MediaStatus.Ready,
                    ValidationStatus = ValidationStatus.Passed,
                    IsCurrent = true,
                    Url = "https://cdn.test/img-948.png"
                },
                new MediaAsset
                {
                    Id = 9521,
                    StoryVersionId = version.Id,
                    StorySceneId = scene.Id,
                    StorySegmentId = segment.Id,
                    Type = MediaType.TtsAudio,
                    Status = MediaStatus.Ready,
                    IsCurrent = true,
                    Url = "https://cdn.test/audio-948.mp3"
                });

            await db.SaveChangesAsync();
            return true;
        });

        var creatorClient = factory.ClientFor(creator);
        var modClient = factory.ClientFor(moderator);

        var submitResp = await creatorClient.PostAsJsonAsync("/api/v1/PublicStorySubmission/submit", new
        {
            storyId = 948,
            storyVersionId = 9120,
            contentCategoryIds = new[] { 907 }
        });
        Assert.Equal(HttpStatusCode.OK, submitResp.StatusCode);
        var itemId = (await ReadJsonAsync(submitResp)).GetProperty("data").GetProperty("itemId").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await modClient.PostAsync($"/api/v1/ModerationQueue/{itemId}/claim", null)).StatusCode);
        var rejectResp = await modClient.PostAsJsonAsync($"/api/v1/PublicStoryReview/{itemId}/reject", new { reason = "Chưa đạt chất lượng nội dung." });
        Assert.Equal(HttpStatusCode.OK, rejectResp.StatusCode);

        var listResp = await creatorClient.GetAsync("/api/v1/creator/stories?pageIndex=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var row = Assert.Single((await ReadJsonAsync(listResp)).GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal(948, row.GetProperty("storyId").GetInt32());
        Assert.Equal("Ready", row.GetProperty("status").GetString());
        Assert.False(row.GetProperty("isPublished").GetBoolean());
        Assert.Equal("Rejected", row.GetProperty("reviewStatus").GetString());
    }
}
