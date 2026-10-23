using Moq;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Application.Features.MediaGeneration.Services;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Moderation.Services;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.UnitTests.Features.Moderation;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ParentManualMediaTests
{
    private static (ParentAuthoringTestStore Store, ParentAuthoringService Parent, MediaGenerationService Media,
        Mock<IModelModerationProvider> Provider) Setup(bool consent = true)
    {
        var store = new ParentAuthoringTestStore();
        store.Items<UserAccount>().Single(x => x.Id == 1).Role = UserRole.Parent;
        var policy = store.Items<SafetyPolicy>().Single();
        policy.ConsentRecorded = consent;
        policy.ConsentRecordedAt = consent ? DateTime.UtcNow : null;
        policy.ConsentPolicyVersion = ConsentPolicy.CurrentVersion;
        policy.ConsentedByUserId = 1;
        var provider = new Mock<IModelModerationProvider>();
        provider.SetupGet(x => x.Name).Returns("test-only");
        provider.Setup(x => x.EvaluateAsync(It.IsAny<GrayZoneScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GrayZoneScan scan, CancellationToken _) => new ModelModerationResult(0, "test", []) { RubricVersion = scan.RubricVersion });
        var evaluator = new GrayZoneEvaluator(new() { Provider = "test-only", WarningThreshold = 50, BlockThreshold = 100, MaximumRiskScore = 100 },
            [provider.Object], RubricTestData.Source());
        var guard = new SupervisionAccessGuard(store.Uow.Object);
        var checks = new GrayZoneContentCheckService(store.Uow.Object, evaluator,
            new GrayZoneWorkflowService(store.Uow.Object, new Mock<IAuditLogWriter>().Object), guard);
        var media = MediaGenerationServiceTests.Create(store.Uow.Object, supervision: guard);
        var parent = new ParentAuthoringService(store.Uow.Object, new(), store.Protector, guard, new StorySegmentService(),
            checks, new MediaContextBuilder(), media, evaluator);
        return (store, parent, media, provider);
    }

    private static async Task<(int StoryId, int VersionId, string Revision, int ConfirmationId)> Content(ParentAuthoringService parent)
    {
        var draft = await parent.CreateParentDraftAsync(1, new() { OperationId = "draft", ChildProfileId = 3, ExperienceMode = "combined" }, default);
        var saved = await parent.SaveContentAsync(1, draft.Story.StoryId, new()
        {
            OperationId = "save",
            RevisionToken = draft.Story.RevisionToken,
            Title = "Buổi chiều",
            Content = "Mẹ cùng con tưới cây. Hai mẹ con vui vẻ ngắm chiếc lá mới."
        }, default);
        var check = await parent.CheckAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "check",
            BaseVersionId = saved.Version.VersionId,
            RevisionToken = saved.Story.RevisionToken
        }, default);
        var confirmed = await parent.ConfirmContentAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "content-confirm",
            BaseVersionId = saved.Version.VersionId,
            RevisionToken = saved.Story.RevisionToken,
            CheckId = check.CheckId,
            Checklist = new(true, true, true, true)
        }, default);
        return (saved.Story.StoryId, saved.Version.VersionId, saved.Story.RevisionToken, confirmed.ConfirmationId);
    }

    [Fact]
    public async Task ParentManualOptInUsesSharedWorkerAndChildOnlyReadsConfirmedGrantedPackage()
    {
        var (store, parent, media, _) = Setup();
        var content = await Content(parent);
        Assert.Empty(store.Items<StoryGenerationJob>());
        var request = new ParentMediaGenerateRequest
        {
            OperationId = "generate",
            RevisionToken = content.Revision,
            BaseVersionId = content.VersionId,
            ContentConfirmationId = content.ConfirmationId
        };
        var job = await parent.GenerateMediaAsync(1, content.StoryId, request, default);
        Assert.Equal(job, await parent.GenerateMediaAsync(1, content.StoryId, request, default));
        Assert.Single(store.Items<StoryGenerationJob>());
        Assert.Empty(store.Items<StoryGenerationRequest>());
        Assert.Null(store.Items<StoryVersion>().Single().OutlineOpening);
        await Assert.ThrowsAsync<ConflictException>(() => parent.SetupAsync(1, content.StoryId,
            new() { OperationId = "setup-active", RevisionToken = content.Revision, BaseVersionId = content.VersionId }, default));
        var processed = await media.RunPackageAsync();
        Assert.True(processed.Success, processed.ErrorCode);
        Assert.Equal(StoryStatus.MediaReview, store.Items<Story>().Single().Status);
        var unconfirmed = await parent.PrepareAsync(1, content.StoryId, new()
        {
            OperationId = "prepare-unconfirmed",
            RevisionToken = content.Revision,
            BaseVersionId = content.VersionId,
            ContentConfirmationId = content.ConfirmationId
        }, default);
        Assert.False(unconfirmed.IsReady);
        await Assert.ThrowsAsync<ConflictException>(() => parent.ConfirmFamilyAsync(1, content.StoryId,
            new()
            {
                OperationId = "grant-denied",
                RevisionToken = content.Revision,
                BaseVersionId = content.VersionId,
                PreparationId = unconfirmed.PreparationId
            }, default));
        Assert.True((await media.ConfirmAsync(1, content.StoryId, new(content.VersionId, 1))).IsReady);
        var eligibility = new StoryPlatform.Application.Features.ChildRoadmaps.Services.ChildRoadmapEligibility(store.Uow.Object);
        Assert.Equal(StoryPlatform.Domain.Enums.ChildRoadmapSkipReason.StoryUnavailable,
            await eligibility.EvaluateAsync(store.Items<ChildProfile>().Single(), store.Items<Story>().Single()));
        var preparation = await parent.PrepareAsync(1, content.StoryId, new()
        {
            OperationId = "prepare",
            RevisionToken = content.Revision,
            BaseVersionId = content.VersionId,
            ContentConfirmationId = content.ConfirmationId
        }, default);
        Assert.True(preparation.IsReady);
        Assert.Equal(1, preparation.MediaPackageRevision);
        var preview = await parent.PreviewAsync(1, content.StoryId, preparation.PreparationId, default);
        Assert.NotNull(preview.Media);
        Assert.All(preview.Media.Scenes.SelectMany(x => x.Audio), x => Assert.StartsWith("https://media.test/", x.Url));
        var grant = await parent.ConfirmFamilyAsync(1, content.StoryId, new()
        {
            OperationId = "grant",
            RevisionToken = content.Revision,
            BaseVersionId = content.VersionId,
            PreparationId = preparation.PreparationId
        }, default);
        Assert.NotNull((await parent.ReadChildPackageAsync(3, grant.ConfirmationId, default)).Media);
        await Assert.ThrowsAsync<NotFoundException>(() => parent.ReadChildPackageAsync(99, grant.ConfirmationId, default));
        Assert.True((await media.ConfirmAsync(1, content.StoryId, new(content.VersionId, 1))).IsReady);
        await Assert.ThrowsAsync<ConflictException>(() => media.RegenerateSegmentAudioAsync(1, content.StoryId,
            store.Items<StorySegment>().First().Id, new(content.VersionId, 1, true)));
        Assert.False(store.Items<Story>().Single().IsPublished);
    }

    [Fact]
    public async Task MissingConsentBlocksOptInBeforeProviderAndPolicyChangesInvalidateMediaConfirmation()
    {
        var (store, parent, media, provider) = Setup();
        var content = await Content(parent);
        var request = new ParentMediaGenerateRequest
        {
            OperationId = "generate",
            RevisionToken = content.Revision,
            BaseVersionId = content.VersionId,
            ContentConfirmationId = content.ConfirmationId
        };
        store.Items<SafetyPolicy>().Single().ConsentRecorded = false;
        var calls = provider.Invocations.Count;
        await Assert.ThrowsAnyAsync<Exception>(() => parent.GenerateMediaAsync(1, content.StoryId, request, default));
        Assert.Equal(calls, provider.Invocations.Count);
        Assert.Empty(store.Items<StoryGenerationJob>());
        store.Items<SafetyPolicy>().Single().ConsentRecorded = true;
        await parent.GenerateMediaAsync(1, content.StoryId, request, default);
        var processed = await media.RunPackageAsync();
        Assert.True(processed.Success, processed.ErrorCode);
        store.Items<SafetyPolicy>().Single().MaxStoryLength = 4000;
        await Assert.ThrowsAnyAsync<Exception>(() => media.ConfirmAsync(1, content.StoryId, new(content.VersionId, 1)));
        Assert.Null(store.Items<StoryVersion>().Single().MediaState.MediaConfirmedAt);
    }
}
