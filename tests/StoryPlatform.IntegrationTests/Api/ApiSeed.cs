using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.IntegrationTests.Api;

/// <summary>Minimal domain rows the API endpoints under test depend on.</summary>
internal static class ApiSeed
{
    public static Task<ChildProfile> ChildAsync(ApiFactory factory, int ownerUserId,
        ChildProfileStatus status = ChildProfileStatus.Active, bool withPolicies = true) =>
        factory.WithDbAsync(async db =>
        {
            var child = new ChildProfile
            {
                OwnerUserId = ownerUserId, Nickname = "Bé Mây", AgeBand = AgeBand.Age_6_8, Language = "vi", Status = status
            };
            db.ChildProfiles.Add(child);
            if (withPolicies)
            {
                db.LearningProfiles.Add(new LearningProfile { ChildProfile = child, ReadingLevel = 2 });
                db.SafetyPolicies.Add(new SafetyPolicy
                {
                    ChildProfile = child, MaxStoryLength = 700, RequiredApprovalMode = ApprovalMode.AlwaysManual,
                    ConsentRecorded = true, ConsentRecordedAt = DateTime.UtcNow, ConsentPolicyVersion = 1
                });
            }
            await db.SaveChangesAsync();
            return child;
        });

    public static Task<Story> ParentStoryAsync(ApiFactory factory, int authorUserId, int childProfileId, StoryStatus status,
        string content = "Thỏ con có một củ cà rốt. Thỏ chia cho bạn Rùa. Hai bạn cùng ăn. Hai bạn rất vui.") =>
        factory.WithDbAsync(async db =>
        {
            var story = new Story
            {
                AuthorUserId = authorUserId, ChildProfileId = childProfileId, Scope = StoryScope.Parent,
                Source = StorySource.Ai, Status = status, OutputMode = StoryOutputMode.ReadingMediaOnly,
                Title = "Thỏ và Rùa", Language = "vi", ReadingLevel = 2
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync();
            var version = new StoryVersion
            {
                StoryId = story.Id, VersionNo = 1, IsCurrent = true, Title = "Thỏ và Rùa", Content = content,
                Lesson = "Biết chia sẻ", SafetyScore = 100m
            };
            version.AuthoringMetadata.StoryId = story.Id;
            db.StoryVersions.Add(version);
            await db.SaveChangesAsync();
            return story;
        });

    public static Task<ModerationItem> PendingModerationItemAsync(ApiFactory factory, int? createdByUserId = null) =>
        factory.WithDbAsync(async db =>
        {
            var creator = createdByUserId ?? (await factory.AddUserAsync(UserRole.ContentCreator)).Id;
            var story = new Story
            {
                AuthorUserId = creator, Scope = StoryScope.Creator, Source = StorySource.Ai,
                Status = StoryStatus.Ready, Title = "Truyện cộng đồng", Language = "vi"
            };
            db.Stories.Add(story);
            var item = new ModerationItem
            {
                ItemType = ModerationItemType.PublicStoryReview, Status = ModerationItemStatus.Pending,
                Story = story, CreatedByUserId = creator, SlaDueAt = DateTime.UtcNow.AddDays(1)
            };
            db.ModerationItems.Add(item);
            await db.SaveChangesAsync();
            return item;
        });
}
