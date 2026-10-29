using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.Stories.DTOs;
using StoryPlatform.Application.Features.Stories.Services;
using StoryPlatform.Application.Features.StoryReview.Interfaces;
using StoryPlatform.Application.Features.StoryReview.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Infrastructure.Persistence;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ParentAuthoringBoundaryTests
{
    [Fact]
    public async Task Legacy_public_reads_and_mutations_cannot_reach_private_parent_manual_story()
    {
        var s = new ParentAuthoringTestStore();
        var d = await s.Service.CreateParentDraftAsync(1, new() { OperationId = "create", ChildProfileId = 3 }, default);
        var legacy = new StoryService(s.Uow.Object);
        Assert.Empty((await legacy.GetStoriesAsync(new())).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => legacy.GetStoryByIdAsync(d.Story.StoryId));
        await Assert.ThrowsAsync<NotFoundException>(() => legacy.UpdateStoryAsync(d.Story.StoryId, 1, new UpdateStoryRequestDto { Title = "Other" }));
        await Assert.ThrowsAsync<NotFoundException>(() => legacy.DeleteStoryAsync(d.Story.StoryId, 1));
        await Assert.ThrowsAsync<NotFoundException>(() => legacy.PublishStoryAsync(d.Story.StoryId, 1));
    }

    [Fact]
    public async Task Legacy_review_and_AI_proposals_refuse_manual_parent_before_provider_call()
    {
        var s = new ParentAuthoringTestStore();
        var d = await s.Service.CreateParentDraftAsync(1, new() { OperationId = "create", ChildProfileId = 3 }, default);
        var ai = new Mock<IAIStoryGenerationClient>(MockBehavior.Strict);
        var legacy = new StoryReviewService(s.Uow.Object, ai.Object, Mock.Of<IProposalCache>());
        await Assert.ThrowsAsync<ConflictException>(() => legacy.GetReviewPackageAsync(1, d.Story.StoryId));
        await Assert.ThrowsAsync<ConflictException>(() => legacy.GetStoryForReviewAsync(1, d.Story.StoryId));
        await Assert.ThrowsAsync<ConflictException>(() => legacy.ValidateAsync(1, d.Story.StoryId));
        await Assert.ThrowsAsync<ConflictException>(() => legacy.CreateProposalAsync(1, d.Story.StoryId, new()));
        Assert.Null(await legacy.EvaluateAndApplyAutoPublishAsync(d.Story.StoryId));
        ai.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Revocation_hides_pinned_package_and_setup_change_does_not_rewrite_existing_grant()
    {
        var s = new ParentAuthoringTestStore();
        var d = await s.Service.CreateParentDraftAsync(1, new() { OperationId = "create", ChildProfileId = 3 }, default);
        var saved = await s.Service.SaveContentAsync(1, d.Story.StoryId, new SaveCreatorContentRequest
        { OperationId = "save", RevisionToken = d.Story.RevisionToken, Title = "Kỷ niệm", Content = "Mẹ cùng con lau bàn." });
        var check = await s.Service.CheckAsync(1, d.Story.StoryId, new() { OperationId = "check", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId }, default);
        var confirm = await s.Service.ConfirmContentAsync(1, d.Story.StoryId, new()
        {
            OperationId = "confirm", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            CheckId = check.CheckId, Checklist = new(true, true, true, true)
        }, default);
        var package = await s.Service.PrepareAsync(1, d.Story.StoryId, new()
        {
            OperationId = "prep", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ContentConfirmationId = confirm.ConfirmationId
        }, default);
        var grant = await s.Service.ConfirmFamilyAsync(1, d.Story.StoryId, new()
        {
            OperationId = "grant", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            PreparationId = package.PreparationId
        }, default);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.SetupAsync(1, d.Story.StoryId, new()
        { OperationId = "setup", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId, ExperienceMode = "combined" }, default));
        Assert.Equal("reading", (await s.Service.ReadChildPackageAsync(3, grant.ConfirmationId, default)).ExperienceMode);
        await s.Service.RevokeFamilyAsync(1, d.Story.StoryId, new()
        {
            OperationId = "revoke", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ConfirmationId = grant.ConfirmationId
        }, default);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.ReadChildPackageAsync(3, grant.ConfirmationId, default));
    }

    [Fact]
    public void Model_has_pinned_operations_and_query_translates_without_database_access()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused").Options);
        var entity = context.Model.FindEntityType(typeof(ParentStoryOperation))!;
        Assert.Equal("parent_story_operations", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { "StoryId", "ActorUserId", "OperationId" }));
        Assert.Equal(4, entity.GetForeignKeys().Count());
        Assert.Null(context.Model.FindEntityType(typeof(Story))!.FindProperty("AuthoringWorkflow"));
        Assert.Contains("ChildProfileId", context.ParentStoryOperations.Where(x => x.ChildProfileId == 3 && x.Kind == "FamilyConfirmation").ToQueryString());
    }

    [Fact]
    public void Parent_listing_permission_filter_translates_to_SQL_before_pagination()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused").Options);
        var uow = new Mock<IUnitOfWork>();
        var children = new Mock<IGenericRepository<ChildProfile>>();
        children.Setup(x => x.Query()).Returns(context.ChildProfiles);
        uow.Setup(x => x.Repository<ChildProfile>()).Returns(children.Object);
        var requests = new Mock<IGenericRepository<StoryGenerationRequest>>();
        requests.Setup(x => x.Query()).Returns(context.Set<StoryGenerationRequest>());
        uow.Setup(x => x.Repository<StoryGenerationRequest>()).Returns(requests.Object);
        var policy = new ParentManualAuthoringPolicy(uow.Object, Mock.Of<IChildProfileAccessGuard>());
        var sql = context.Stories.Where(policy.VisibleStories(1)).OrderByDescending(x => x.CreatedAt)
            .Skip(1).Take(1).ToQueryString();
        Assert.Contains("EXISTS", sql); Assert.Contains("OwnerUserId", sql);
        Assert.Contains("child_profiles", sql);
        Assert.Contains("LIMIT", sql); Assert.Contains("OFFSET", sql);
    }

    [Fact]
    public void Routes_distinguish_parent_and_child_authentication()
    {
        Assert.Equal("User", typeof(ParentAuthoringController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Equal(ServiceExtensions.ChildSessionPolicy, typeof(ChildManualStoriesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy);
    }
}
