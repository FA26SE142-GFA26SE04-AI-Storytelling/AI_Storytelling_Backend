using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests.Features.Moderation;

public sealed class GrayZoneContentCheckQueryTests
{
    [Theory]
    [InlineData(ModerationItemStatus.Pending)]
    [InlineData(ModerationItemStatus.Claimed)]
    public async Task Creator_revision_can_be_checked_while_reports_on_previous_version_remain_open(ModerationItemStatus status)
    {
        var h = new PublicStoryReviewHarness();
        h.Items.Clear();
        var story = h.Stories.Single();
        var version = h.Versions.Single();
        story.Status = StoryStatus.ContentReview;
        version.BaseVersionId = version.Id - 1;
        h.Items.Add(new() { Id = 21, StoryId = story.Id, StoryVersionId = version.BaseVersionId,
            ItemType = ModerationItemType.ContentReport, Status = status });
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        var reachedProvider = new ApplicationException("fixture-provider-reached");
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>())).ThrowsAsync(reachedProvider);
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object, null!, new Mock<IChildProfileAccessGuard>().Object);

        var error = await Record.ExceptionAsync(() => checks.CheckAsync(story.AuthorUserId, story.Id, version.Id, default));

        Assert.Same(reachedProvider, error);
        Assert.Equal(status, h.Items.Single().Status);
        Assert.Equal(version.BaseVersionId, h.Items.Single().StoryVersionId);
    }

    [Theory]
    [InlineData(ModerationItemType.ContentReport, false, ModerationItemStatus.Pending)]
    [InlineData(ModerationItemType.ContentReport, false, ModerationItemStatus.Claimed)]
    [InlineData(ModerationItemType.ContentReport, true, ModerationItemStatus.Pending)]
    [InlineData(ModerationItemType.PublicStoryReview, true, ModerationItemStatus.Pending)]
    [InlineData(ModerationItemType.PublicStoryRemoval, true, ModerationItemStatus.Claimed)]
    public async Task Creator_current_report_or_publication_work_still_locks_safety_check(
        ModerationItemType type, bool unspecifiedVersion, ModerationItemStatus status)
    {
        var h = new PublicStoryReviewHarness();
        h.Items.Clear();
        var story = h.Stories.Single();
        var version = h.Versions.Single();
        story.Status = StoryStatus.ContentReview;
        h.Items.Add(new() { Id = 21, StoryId = story.Id, StoryVersionId = unspecifiedVersion ? null : version.Id,
            ItemType = type, Status = status });
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object, null!, new Mock<IChildProfileAccessGuard>().Object);

        var error = await Assert.ThrowsAsync<ConflictException>(() => checks.CheckAsync(story.AuthorUserId, story.Id, version.Id, default));

        Assert.Equal("MODERATION_SOURCE_LOCKED", error.Message);
        evaluator.Verify(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parent_confirmation_translates_blocked_categories_before_calling_provider(bool hasBlockedRules)
    {
        var h = new PublicStoryReviewHarness();
        var story = h.Stories.Single();
        story.Scope = StoryScope.Parent;
        story.ChildProfileId = 1;
        h.Users.Single(x => x.Id == story.AuthorUserId).Role = UserRole.User;
        var policies = new Mock<IGenericRepository<SafetyPolicy>>();
        policies.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SafetyPolicy { Id = 2, ChildProfileId = 1, ConsentRecorded = true, ConsentRecordedAt = DateTime.UtcNow, ConsentPolicyVersion = 1 });
        h.UnitOfWork.Setup(x => x.Repository<SafetyPolicy>()).Returns(policies.Object);
        var rules = new Mock<IGenericRepository<SafetyPolicyCategory>>();
        rules.Setup(x => x.FindAsync(It.IsAny<Expression<Func<SafetyPolicyCategory, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasBlockedRules ? [new() { ContentCategoryId = 7 }, new() { ContentCategoryId = 8 }] : []);
        h.UnitOfWork.Setup(x => x.Repository<SafetyPolicyCategory>()).Returns(rules.Object);
        h.ContentCategories.AddRange([
            new() { Id = 7, Code = "blocked", DisplayName = "Chủ đề bị chặn", IsActive = true },
            new() { Id = 8, Code = "inactive", DisplayName = "Đã ngừng dùng", IsActive = false },
            new() { Id = 9, Code = "allowed", DisplayName = "Được phép", IsActive = true }
        ]);
        // Generate SQL with the real Npgsql translator; no database connection or migration is performed.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=query_translation_only").Options);
        string? translatedSql = null;
        Mock.Get(h.UnitOfWork.Object.Repository<ContentCategory>())
            .Setup(x => x.FindAsync(It.IsAny<Expression<Func<ContentCategory, bool>>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<ContentCategory, bool>> predicate, string? _, CancellationToken _) =>
            {
                translatedSql = db.Set<ContentCategory>().Where(predicate).ToQueryString();
                return (IReadOnlyList<ContentCategory>)h.ContentCategories.Where(predicate.Compile()).ToList();
            });
        var evaluator = new Mock<IGrayZoneEvaluator>();
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        GrayZoneScan? captured = null;
        var stopBeforePersistence = new ApplicationException("fixture-stop-before-persistence");
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .Callback<GrayZoneScan, CancellationToken>((scan, _) => captured = scan).ThrowsAsync(stopBeforePersistence);
        var checks = new GrayZoneContentCheckService(h.UnitOfWork.Object, evaluator.Object, null!, new Mock<IChildProfileAccessGuard>().Object);

        var error = await Record.ExceptionAsync(() => checks.CheckAsync(story.AuthorUserId, story.Id, h.Versions.Single().Id, default));

        Assert.Same(stopBeforePersistence, error);
        Assert.Contains("SELECT", translatedSql);
        Assert.NotNull(captured);
        Assert.Equal(hasBlockedRules ? ["blocked", "Chủ đề bị chặn"] : Array.Empty<string>(), captured.BlockedTerms);
        h.UnitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
