using Moq;
using System.Net;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>Real JWT/HTTP/controllers/services with fake persistence; no provider or PostgreSQL.</summary>
public sealed class CreatorGenerationApiFlowTests
{
    [Fact]
    public async Task Creator_ConfirmationRouteIsSeparateFromApprovalAndValidatesVersion()
    {
        var review = new Moq.Mock<StoryPlatform.Application.Features.StoryReview.Interfaces.IStoryReviewService>(Moq.MockBehavior.Strict);
        review.Setup(x => x.ConfirmCreatorContentAsync(13, 42,
                Moq.It.Is<StoryPlatform.Application.Features.StoryReview.DTOs.ConfirmCreatorContentRequestDto>(r => r.VersionId == 7),
                Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoryPlatform.Application.Features.StoryReview.DTOs.ConfirmCreatorContentResponseDto
            { StoryId = 42, VersionId = 7, Status = "MediaProcessing", ConfirmedAt = DateTime.UtcNow });
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync(review.Object);
        const string path = "/api/v1/creator/stories/42/generation/confirm-content";
        await host.SendAsync(HttpMethod.Post, path, new { versionId = 7 }, host.Parent, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Post, path, new { versionId = 7 }, host.Child, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Post, path, new { versionId = 0 }, host.Creator, HttpStatusCode.BadRequest);
        await host.SendAsync(HttpMethod.Post, "/api/v1/stories/42/review/approve", null, host.Creator, HttpStatusCode.Forbidden);
        var data = await host.SendAsync(HttpMethod.Post, path, new { versionId = 7 }, host.Creator, HttpStatusCode.Accepted);
        Assert.Equal("MediaProcessing", data.GetProperty("status").GetString());
        Assert.False(data.TryGetProperty("approvedAt", out _));
        review.VerifyAll();
        review.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Creator_InputApiUsesAudienceAndNeverBorrowsAChild()
    {
        await using var host = await ParentManualApiFlowTests.ApiHost.StartAsync();
        const string root = "/api/v1/creator/ai-story-input";
        await host.SendAsync(HttpMethod.Get, root + "/context", null, host.Parent, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Get, root + "/context", null, host.Child, HttpStatusCode.Forbidden);
        var context = await host.SendAsync(HttpMethod.Get, root + "/context?ageBand=9-10&readingLevel=4", null,
            host.Creator, HttpStatusCode.OK);
        Assert.Equal("9-10", context.GetProperty("ageBand").GetString());
        var result = await host.SendAsync(HttpMethod.Post, root + "/requests", new
        {
            idempotencyKey = "creator-http-generation-001", ageBand = "9-10", readingLevel = 4,
            topic = "Các bạn giúp nhau làm vườn", lesson = "Biết cùng nhau chia sẻ công việc",
            vocabularyLevel = "level_4", language = "vi", targetLength = 500,
            outputMode = "reading_media_only", characterMode = "ai_suggested", settingMode = "ai_suggested"
        }, host.Creator, HttpStatusCode.Accepted);
        Assert.Equal("input_accepted", result.GetProperty("inputStatus").GetString());
        var story = Assert.Single(host.Store.Items<Story>());
        Assert.Equal(StoryScope.Creator, story.Scope);
        Assert.Equal("9-10", story.AgeBand);
        Assert.Null(story.ChildProfileId);
        Assert.False(story.IsPublished);
        var detail = await host.SendAsync(HttpMethod.Get, $"/api/v1/creator/stories/{story.Id}", null,
            host.Creator, HttpStatusCode.OK);
        Assert.Equal("Ai", detail.GetProperty("source").GetString());
        Assert.Equal("Draft", detail.GetProperty("status").GetString());
        Assert.Single(host.Store.Items<StoryGenerationRequest>());
        Assert.Single(host.Store.Items<StoryGenerationJob>());
        Assert.Empty(host.Store.Items<StoryVersion>());
        await host.SendAsync(HttpMethod.Post, $"/api/v1/creator/stories/{story.Id}/manual/versions", new
        { operationId = "cannot-overwrite-generation", revisionToken = story.AuthoringRevisionToken,
            baseVersionId = (int?)null, title = "Không thay outline", content = "Nội dung khác" },
            host.Creator, HttpStatusCode.Conflict);
        Assert.Empty(host.Store.Items<StoryVersion>());
        await host.SendAsync(HttpMethod.Get, $"/api/v1/ai-story-input/children/3/context", null,
            host.Creator, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Get, $"/api/v1/parent/stories/{story.Id}", null,
            host.Parent, HttpStatusCode.NotFound);
    }
}
