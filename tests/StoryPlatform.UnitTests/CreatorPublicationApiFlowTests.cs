using System.Net;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
namespace StoryPlatform.UnitTests;

/// <summary>HTTP/JWT and real application services; fake persistence, no live DB/provider.</summary>
public sealed class CreatorPublicationApiFlowTests
{
    [Fact]
    public async Task PublishedCreatorStory_RequiresModeratorRemoval_ThenNewEditableRevision()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();
        var story = new Story
        {
            Id = 200,
            Scope = StoryScope.Creator,
            Source = StorySource.Manual,
            AuthorUserId = 13,
            Status = StoryStatus.Ready,
            IsPublished = true,
            Title = "Chiếc diều xanh",
            Content = "An cùng bố làm diều."
        };
        var version = new StoryVersion
        {
            Id = 201,
            StoryId = story.Id,
            VersionNo = 1,
            IsCurrent = true,
            Title = story.Title,
            Content = story.Content,
            Moderation = new StoryVersionModerationState
            {
                ReviewStatus = VersionReviewStatus.Approved,
                ReviewSubmittedAt = DateTime.UtcNow.AddDays(-1)
            }
        };
        host.Store.Items<Story>().Add(story); host.Store.Items<StoryVersion>().Add(version);
        const string root = "/api/v1/creator/stories/200";
        await host.SendAsync(HttpMethod.Post, root + "/revisions", new { sourceVersionId = 201 }, host.Creator, HttpStatusCode.Conflict);
        await host.SendAsync(HttpMethod.Post, root + "/removal-requests", new { reason = "Sửa lại phần kết" }, host.Parent, HttpStatusCode.Forbidden);
        var request = await host.SendAsync(HttpMethod.Post, root + "/removal-requests", new { reason = "Sửa lại phần kết" }, host.Creator, HttpStatusCode.OK);
        int itemId = request.GetProperty("itemId").GetInt32();
        var publication = await host.SendAsync(HttpMethod.Get, root + "/publication", null, host.Creator, HttpStatusCode.OK);
        Assert.True(publication.GetProperty("isPublished").GetBoolean());
        await host.SendAsync(HttpMethod.Post, root + "/revisions", new { sourceVersionId = 201 }, host.Creator, HttpStatusCode.Conflict);
        await host.SendAsync(HttpMethod.Post, root + "/manual/versions", new
        {
            operationId = "blocked-published-write",
            revisionToken = story.AuthoringRevisionToken,
            baseVersionId = 201,
            title = "Đổi tên",
            content = "Đổi nội dung"
        }, host.Creator, HttpStatusCode.Conflict);
        string decision = $"/api/v1/moderation/public-story-removals/{itemId}/decision";
        await host.SendAsync(HttpMethod.Post, decision, new { approve = true, reason = "Gỡ để chỉnh sửa", rowVersion = 0 }, host.Creator, HttpStatusCode.Forbidden);
        var claim = await host.SendAsync(HttpMethod.Post, $"/api/v1/ModerationQueue/{itemId}/claim", null, host.Moderator, HttpStatusCode.OK);
        await host.SendAsync(HttpMethod.Post, decision, new { approve = true, reason = "Gỡ để chỉnh sửa", rowVersion = claim.GetProperty("rowVersion").GetInt32() }, host.Moderator, HttpStatusCode.OK);
        var revision = await host.SendAsync(HttpMethod.Post, root + "/revisions", new { sourceVersionId = 201 }, host.Creator, HttpStatusCode.OK);
        Assert.NotEqual(201, revision.GetProperty("versionId").GetInt32());
        Assert.Equal("Draft", revision.GetProperty("status").GetString());
        var saved = await host.SendAsync(HttpMethod.Post, root + "/manual/versions", new
        {
            operationId = "save-after-removal",
            revisionToken = revision.GetProperty("revisionToken").GetString(),
            baseVersionId = revision.GetProperty("versionId").GetInt32(),
            title = "Chiếc diều xanh",
            content = "An cùng bố làm diều. Hai bố con cùng mang diều ra sân.",
            sourceDeclaration = "Tự sáng tác",
            rightsDeclaration = "Tôi sở hữu nội dung"
        }, host.Creator, HttpStatusCode.Created);
        Assert.Equal(3, saved.GetProperty("version").GetProperty("versionNo").GetInt32());
        Assert.Equal("An cùng bố làm diều.", host.Store.Items<StoryVersion>().Single(x => x.Id == 201).Content);
        Assert.Equal(VersionReviewStatus.Approved, host.Store.Items<StoryVersion>().Single(x => x.Id == 201).Moderation.ReviewStatus);
        Assert.False(host.Store.Items<Story>().Single().IsPublished);
    }
}
