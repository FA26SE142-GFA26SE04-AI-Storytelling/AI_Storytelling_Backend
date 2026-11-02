using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.Moderation;
using StoryPlatform.Application.Features.Outline.DTOs;
using StoryPlatform.Application.Features.Outline.Guardrails;
using StoryPlatform.Application.Features.Outline.Interfaces;
using StoryPlatform.Application.Features.Outline.Services;
using StoryPlatform.Contracts.AI.Models;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Contracts.AI.Responses;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;
using Store = StoryPlatform.UnitTests.OutlineServiceTests.FakeUnitOfWork;

namespace StoryPlatform.UnitTests.Features.Outline;

/// <summary>One test per guard/exception branch of OutlineService (Report 5, Việc 3.1).</summary>
public sealed class OutlineServiceBranchTests
{
    private const string ActiveJobMessage = "Đang có một thao tác outline khác hoạt động cho Story này.";
    private const string NotReviewMessage = "Story không ở trạng thái outline_review.";
    private const string ForbiddenMessage = "Bạn không có quyền truy cập tài nguyên này.";

    // ---- Read endpoints & review authorization ----

    [Fact]
    public async Task GetVersionsAsync_ChildOwner_ReturnsAllVersionsInOrder()
    {
        var (store, service, _, _) = await ReadyAsync();
        await service.EditAsync(1, 1, 1, Edit("Tiêu đề hai"));

        var versions = await service.GetVersionsAsync(1, 1);

        Assert.Equal([1, 2], versions.Select(item => item.VersionNo).ToArray());
        Assert.False(versions[0].IsCurrent);
        Assert.True(versions[1].IsCurrent);
        Assert.Equal("Tiêu đề hai", versions[1].Title);
    }

    [Fact]
    public async Task GetVersionAsync_ExistingVersionNo_ReturnsThatVersion()
    {
        var (_, service, _, _) = await ReadyAsync();

        var version = await service.GetVersionAsync(1, 1, 1);

        Assert.Equal(1, version.VersionNo);
        Assert.Equal(VersionEditType.Initial.ToString(), version.EditType);
        Assert.Equal("Mở đầu", version.Opening);
    }

    [Fact]
    public async Task GetVersionAsync_UnknownVersionNo_ThrowsNotFound()
    {
        var (_, service, _, _) = await ReadyAsync();

        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.GetVersionAsync(1, 1, 9));

        Assert.Equal("Không tìm thấy StoryVersion với mã định danh [9].", error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_UnknownStory_ThrowsNotFound()
    {
        var error = await Assert.ThrowsAsync<NotFoundException>(() => Service(OutlineServiceTests.Seed()).GetCurrentAsync(1, 42));

        Assert.Equal("Không tìm thấy Story với mã định danh [42].", error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_ParentManualEditorStory_ThrowsConflict()
    {
        var store = OutlineServiceTests.Seed();
        var story = store.Items<Story>().Single();
        story.Source = StorySource.Manual;
        story.CreateOperationId = "manual-create-1";

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(store).GetCurrentAsync(1, 1));

        Assert.Equal("PARENT_MANUAL_USE_AUTHORING_API", error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_UnknownUser_ThrowsForbidden()
    {
        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(OutlineServiceTests.Seed()).GetCurrentAsync(77, 1));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_SuspendedParent_ThrowsForbidden()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<UserAccount>().Single().Status = AccountStatus.Suspended;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(store).GetCurrentAsync(1, 1));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_UserIsNotParent_ThrowsForbidden()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<UserAccount>().Single().Role = UserRole.Moderator;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(store).GetCurrentAsync(1, 1));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_UserNotChildOwner_ThrowsForbidden()
    {
        var store = OutlineServiceTests.Seed();
        store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(store).GetCurrentAsync(2, 1));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_ChildProfileDeleted_ThrowsForbidden()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<ChildProfile>().Single().Status = ChildProfileStatus.Deleted;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(store).GetCurrentAsync(1, 1));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    [Fact]
    public async Task GetCurrentAsync_ParentStoryWithoutChildProfile_ThrowsForbidden()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<Story>().Single().ChildProfileId = null;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => Service(store).GetCurrentAsync(1, 1));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    // ---- Edit ----

    [Fact]
    public async Task EditAsync_EmptyTitle_ThrowsBadRequest()
    {
        var (store, service, _, _) = await ReadyAsync();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.EditAsync(1, 1, 1, Edit("")));

        Assert.Contains("Title", error.Message);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task EditAsync_ActiveOutlineJob_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Seed(PendingJob(50, GenerationJobOperation.RegenerateOutline));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.EditAsync(1, 1, 1, Edit("Mới")));

        Assert.Equal(ActiveJobMessage, error.Message);
    }

    [Fact]
    public async Task EditAsync_ContextSnapshotIsNullJson_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Items<StoryGenerationRequest>().Single().ContextSnapshotJson = "null";

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.EditAsync(1, 1, 1, Edit("Mới")));

        Assert.Equal("Context Snapshot không hợp lệ.", error.Message);
    }

    [Fact]
    public async Task EditAsync_UserNotChildOwner_ThrowsForbidden()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => service.EditAsync(2, 1, 1, Edit("Mới")));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    // ---- Regenerate ----

    [Fact]
    public async Task RegenerateAsync_NewOperationKey_QueuesRegenerateJobOnCurrentVersion()
    {
        var (store, service, _, _) = await ReadyAsync();

        var progress = await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));

        var job = Assert.Single(store.Items<StoryGenerationJob>(), item => item.Operation == GenerationJobOperation.RegenerateOutline);
        Assert.Equal(store.Items<StoryVersion>().Single().Id, job.BaseStoryVersionId);
        Assert.Equal(JobStage.OutlinePending, job.Stage);
        Assert.Equal("regenerate_outline", progress.ActiveOperation);
        Assert.Equal("pending", progress.ActiveJobStatus);
    }

    [Fact]
    public async Task RegenerateAsync_SameKeyForSameVersion_DoesNotQueueSecondJob()
    {
        var (store, service, _, _) = await ReadyAsync();
        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));
        // Mark the first job finished so only the idempotency branch can stop a duplicate.
        store.Items<StoryGenerationJob>().Single(item => item.Operation == GenerationJobOperation.RegenerateOutline)
            .Status = GenerationJobStatus.Completed;

        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));

        Assert.Single(store.Items<StoryGenerationJob>(), item => item.Operation == GenerationJobOperation.RegenerateOutline);
    }

    [Fact]
    public async Task RegenerateAsync_KeyUsedForAnotherBaseVersion_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Seed(new StoryGenerationJob
        {
            Id = 60, StoryId = 1, RequestedByUserId = 1, OperationKey = "regen-key-0001", BaseStoryVersionId = 99,
            Operation = GenerationJobOperation.RegenerateOutline, Status = GenerationJobStatus.Completed
        });

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001")));

        Assert.Equal("Operation key đã được sử dụng cho Story hoặc base version khác.", error.Message);
    }

    [Fact]
    public async Task RegenerateAsync_ActiveOutlineJob_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Seed(PendingJob(50, GenerationJobOperation.GenerateOutline));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001")));

        Assert.Equal(ActiveJobMessage, error.Message);
    }

    [Fact]
    public async Task RegenerateAsync_OperationKeyShorterThanEight_ThrowsBadRequest()
    {
        var (_, service, _, _) = await ReadyAsync();

        var error = await Assert.ThrowsAsync<BadRequestException>(() => service.RegenerateAsync(1, 1, 1, Regenerate("short")));

        Assert.Contains("OperationKey", error.Message);
    }

    [Fact]
    public async Task RegenerateAsync_StoryStillDraft_ThrowsConflict()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Service(OutlineServiceTests.Seed()).RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001")));

        Assert.Equal(NotReviewMessage, error.Message);
    }

    [Fact]
    public async Task RegenerateAsync_VersionNoLongerCurrent_ThrowsConflict()
    {
        var (_, service, _, _) = await ReadyAsync();
        await service.EditAsync(1, 1, 1, Edit("Bản hai"));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001")));

        Assert.Equal("StoryVersion không còn là current version.", error.Message);
    }

    [Fact]
    public async Task RegenerateAsync_NoAcceptedInputSnapshot_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Items<StoryGenerationRequest>().Single().Status = GenerationInputStatus.InputBlocked;

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001")));

        Assert.Equal("Story không có Accepted Input Snapshot hợp lệ.", error.Message);
    }

    // ---- Retry initial generation ----

    [Fact]
    public async Task RetryInitialAsync_StoryAlreadyHasOutline_ThrowsConflict()
    {
        var (_, service, _, _) = await ReadyAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.RetryInitialAsync(1, 1, Retry("retry-key-0001")));

        Assert.Equal("Chỉ Story draft chưa có outline mới có thể retry initial generation.", error.Message);
    }

    [Fact]
    public async Task RetryInitialAsync_KeyUsedForAnotherStory_ThrowsConflict()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationJob>().Single().Status = GenerationJobStatus.Failed;
        store.Seed(new StoryGenerationJob
        {
            Id = 70, StoryId = 2, RequestedByUserId = 1, OperationKey = "retry-key-0001",
            Operation = GenerationJobOperation.GenerateOutline, Status = GenerationJobStatus.Completed
        });

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(store).RetryInitialAsync(1, 1, Retry("retry-key-0001")));

        Assert.Equal("Operation key đã được sử dụng cho Story khác.", error.Message);
    }

    [Fact]
    public async Task RetryInitialAsync_NoFailedInitialJob_ThrowsConflict()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationJob>().Single().Status = GenerationJobStatus.Cancelled;

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(store).RetryInitialAsync(1, 1, Retry("retry-key-0001")));

        Assert.Equal("Story không có initial outline generation thất bại để retry.", error.Message);
    }

    [Fact]
    public async Task RetryInitialAsync_InitialJobStillPending_ThrowsConflict()
    {
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Service(OutlineServiceTests.Seed()).RetryInitialAsync(1, 1, Retry("retry-key-0001")));

        Assert.Equal(ActiveJobMessage, error.Message);
    }

    // ---- Approve ----

    [Fact]
    public async Task ApproveAsync_VersionAlreadyHasContent_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Items<StoryVersion>().Single().Content = "Nội dung đã sinh";

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ApproveAsync(1, 1, 1, Approve("approve-key-0001")));

        Assert.Equal("Chỉ outline chưa có content mới được approve trong Phase 2.", error.Message);
    }

    [Fact]
    public async Task ApproveAsync_SameKeyRepeated_ReturnsProgressWithoutSecondHandoff()
    {
        var (store, service, _, _) = await ReadyAsync();
        await service.ApproveAsync(1, 1, 1, Approve("approve-key-0001"));

        var progress = await service.ApproveAsync(1, 1, 1, Approve("approve-key-0001"));

        Assert.Single(store.Items<StoryGenerationJob>(), item => item.Operation == GenerationJobOperation.GenerateContent);
        Assert.NotNull(progress.CurrentVersion!.OutlineApprovedAt);
    }

    [Fact]
    public async Task ApproveAsync_KeyUsedForAnotherVersion_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Seed(new StoryGenerationJob
        {
            Id = 80, StoryId = 1, RequestedByUserId = 1, OperationKey = "approve-key-0001", BaseStoryVersionId = 99,
            Operation = GenerationJobOperation.GenerateContent, Status = GenerationJobStatus.Completed
        });

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ApproveAsync(1, 1, 1, Approve("approve-key-0001")));

        Assert.Equal("Approval key đã được sử dụng cho StoryVersion khác.", error.Message);
    }

    [Fact]
    public async Task ApproveAsync_VersionAlreadyApprovedWithDifferentKey_ThrowsConflict()
    {
        var (store, service, _, _) = await ReadyAsync();
        await service.ApproveAsync(1, 1, 1, Approve("approve-key-0001"));

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.ApproveAsync(1, 1, 1, Approve("approve-key-0002")));

        Assert.Equal("StoryVersion này đã được approve bằng một approval key khác.", error.Message);
        Assert.Single(store.Items<StoryGenerationJob>(), item => item.Operation == GenerationJobOperation.GenerateContent);
    }

    [Fact]
    public async Task ApproveAsync_UnknownUser_ThrowsForbidden()
    {
        var (_, service, _, _) = await ReadyAsync();

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveAsync(77, 1, 1, Approve("approve-key-0001")));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    [Fact]
    public async Task ApproveAsync_SuspendedParent_ThrowsForbidden()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Items<UserAccount>().Single().Status = AccountStatus.Suspended;

        var error = await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveAsync(1, 1, 1, Approve("approve-key-0001")));

        Assert.Equal(ForbiddenMessage, error.Message);
        Assert.Null(store.Items<StoryVersion>().Single().OutlineApprovedAt);
    }

    // ---- Reject ----

    [Fact]
    public async Task RejectAsync_CurrentOutline_MarksStoryRejectedAndSourceJobOutlineRejected()
    {
        var (store, service, _, _) = await ReadyAsync();
        var job = store.Items<StoryGenerationJob>().Single();
        var tokenBefore = job.ConcurrencyToken;

        var progress = await service.RejectAsync(1, 1, 1, new RejectOutlineRequestDto { Reason = "  Không phù hợp  " });

        Assert.Equal(StoryStatus.Rejected, store.Items<Story>().Single().Status);
        Assert.Equal("rejected", progress.StoryStatus);
        Assert.Equal(JobStage.OutlineRejected, job.Stage);
        Assert.Equal("Không phù hợp", job.FallbackMessage);
        Assert.NotEqual(tokenBefore, job.ConcurrencyToken);
    }

    [Fact]
    public async Task RejectAsync_HumanEditedVersionWithoutSourceJob_RejectsStoryOnly()
    {
        var (store, service, _, _) = await ReadyAsync();
        await service.EditAsync(1, 1, 1, Edit("Bản hai"));
        var job = store.Items<StoryGenerationJob>().Single();

        await service.RejectAsync(1, 1, 2, new RejectOutlineRequestDto { Reason = "Không phù hợp" });

        Assert.Equal(StoryStatus.Rejected, store.Items<Story>().Single().Status);
        Assert.Equal(JobStage.OutlineGenerated, job.Stage);
        Assert.Null(job.FallbackMessage);
    }

    [Fact]
    public async Task RejectAsync_ReasonShorterThanTwo_ThrowsBadRequest()
    {
        var (store, service, _, _) = await ReadyAsync();

        var error = await Assert.ThrowsAsync<BadRequestException>(() =>
            service.RejectAsync(1, 1, 1, new RejectOutlineRequestDto { Reason = "x" }));

        Assert.Contains("Reason", error.Message);
        Assert.Equal(StoryStatus.OutlineReview, store.Items<Story>().Single().Status);
    }

    [Fact]
    public async Task RejectAsync_ApprovedOutline_ThrowsConflict()
    {
        var (_, service, _, _) = await ReadyAsync();
        await service.ApproveAsync(1, 1, 1, Approve("approve-key-0001"));

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RejectAsync(1, 1, 1, new RejectOutlineRequestDto { Reason = "Không phù hợp" }));

        Assert.Equal("Outline đã duyệt và bàn giao; không thể chỉnh sửa trong Phase 2.", error.Message);
    }

    [Fact]
    public async Task RejectAsync_UserNotChildOwner_ThrowsForbidden()
    {
        var (store, service, _, _) = await ReadyAsync();
        store.Seed(new UserAccount { Id = 2, Role = UserRole.User, Status = AccountStatus.Active });

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.RejectAsync(2, 1, 1, new RejectOutlineRequestDto { Reason = "Không phù hợp" }));

        Assert.Equal(ForbiddenMessage, error.Message);
    }

    // ---- Worker: ProcessNextAsync ----

    [Fact]
    public async Task ProcessNextAsync_NoClaimableJob_ReturnsFalse()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationJob>().Single().Status = GenerationJobStatus.Completed;

        Assert.False(await Service(store).ProcessNextAsync());
    }

    [Fact]
    public async Task ProcessNextAsync_ProcessingJobWithLiveLease_IsNotReclaimed()
    {
        var store = OutlineServiceTests.Seed();
        var job = store.Items<StoryGenerationJob>().Single();
        job.Status = GenerationJobStatus.Processing;
        job.Stage = JobStage.OutlineGenerating;
        job.LeaseExpiresAt = DateTime.UtcNow.AddMinutes(3);

        Assert.False(await Service(store).ProcessNextAsync());
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_ClaimCommitFails_ReturnsFalseWithoutGenerating()
    {
        var store = OutlineServiceTests.Seed();
        store.FailCommitNumber = 1;
        var ai = new ScriptedAIClient();

        Assert.False(await Service(store, ai).ProcessNextAsync());
        Assert.Empty(ai.Requests);
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_RegenerateJob_CreatesAiRegeneratedVersionAndRetiresPrevious()
    {
        var (store, service, ai, _) = await ReadyAsync();
        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));
        ai.Responses.Enqueue(Outline("Cáo nhỏ dũng cảm"));

        Assert.True(await service.ProcessNextAsync());

        var versions = store.Items<StoryVersion>().OrderBy(item => item.VersionNo).ToArray();
        Assert.False(versions[0].IsCurrent);
        Assert.True(versions[1].IsCurrent);
        Assert.Equal(VersionEditType.AiRegenerated, versions[1].EditType);
        Assert.Equal("Cáo nhỏ dũng cảm", store.Items<Story>().Single().Title);
        Assert.Equal("outline-job-2", ai.Requests.Last().RequestId);
    }

    [Fact]
    public async Task ProcessNextAsync_RegenerateWhenBaseVersionNoLongerCurrent_FailsWithStaleBaseVersion()
    {
        var (store, service, _, finalizer) = await ReadyAsync();
        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));
        store.Items<StoryVersion>().Single().IsCurrent = false;

        Assert.True(await service.ProcessNextAsync());

        Assert.Equal("STALE_BASE_VERSION", finalizer.ErrorCode);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_RegenerateJobWithoutBaseVersionId_FailsWithStaleBaseVersion()
    {
        var (store, service, _, finalizer) = await ReadyAsync();
        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));
        store.Items<StoryGenerationJob>().Single(item => item.Operation == GenerationJobOperation.RegenerateOutline)
            .BaseStoryVersionId = null;

        Assert.True(await service.ProcessNextAsync());

        Assert.Equal("STALE_BASE_VERSION", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_RegenerateWhenStoryLeftOutlineReview_FailsWithStaleBaseVersion()
    {
        var (store, service, _, finalizer) = await ReadyAsync();
        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));
        store.Items<Story>().Single().Status = StoryStatus.Rejected;

        Assert.True(await service.ProcessNextAsync());

        Assert.Equal("STALE_BASE_VERSION", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_BaseVersionReplacedWhileAiIsGenerating_FailsWithStaleBaseVersion()
    {
        var (store, service, ai, finalizer) = await ReadyAsync();
        await service.RegenerateAsync(1, 1, 1, Regenerate("regen-key-0001"));
        ai.OnCall = () => store.Items<StoryVersion>().Single().IsCurrent = false;

        Assert.True(await service.ProcessNextAsync());

        Assert.Equal("STALE_BASE_VERSION", finalizer.ErrorCode);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_InitialJobButStoryNoLongerDraft_FailsWithStoryStateChanged()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<Story>().Single().Status = StoryStatus.OutlineReview;
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer).ProcessNextAsync());

        Assert.Equal("STORY_STATE_CHANGED", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_RequestNotAccepted_FailsWithInvalidPhase1Handoff()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationRequest>().Single().Status = GenerationInputStatus.CheckingInput;
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer).ProcessNextAsync());

        Assert.Equal("INVALID_PHASE1_HANDOFF", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_StoryArchived_FailsWithInvalidPhase1Handoff()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<Story>().Single().Status = StoryStatus.Rejected;
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer).ProcessNextAsync());

        Assert.Equal("INVALID_PHASE1_HANDOFF", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_JobWithoutRequestId_ResolvesRequestThroughHandoffLink()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationJob>().Single().GenerationRequestId = null;
        store.Items<StoryGenerationRequest>().Single().HandoffJobId = 1;

        Assert.True(await Service(store).ProcessNextAsync());

        Assert.Equal(GenerationJobStatus.Completed, store.Items<StoryGenerationJob>().Single().Status);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_AcceptedInputJsonIsNull_FailsWithAcceptedInputMissing()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationRequest>().Single().AcceptedInputJson = "null";
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer).ProcessNextAsync());

        Assert.Equal("ACCEPTED_INPUT_MISSING", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_ContextSnapshotJsonIsNull_FailsWithContextSnapshotMissing()
    {
        var store = OutlineServiceTests.Seed();
        store.Items<StoryGenerationRequest>().Single().ContextSnapshotJson = "null";
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer).ProcessNextAsync());

        Assert.Equal("CONTEXT_SNAPSHOT_MISSING", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_AiOutlineContainsEmail_FailsWithGuardrailCode()
    {
        var store = OutlineServiceTests.Seed();
        var ai = new ScriptedAIClient();
        ai.Responses.Enqueue(Outline("Gửi thư tới be@example.com"));
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, ai, finalizer).ProcessNextAsync());

        Assert.Equal("OUTLINE_CONTAINS_PII", finalizer.ErrorCode);
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Theory]
    [InlineData("STALE_BASE_VERSION")]
    [InlineData("INVALID_PHASE1_HANDOFF")]
    [InlineData("ACCEPTED_INPUT_MISSING")]
    [InlineData("CONTEXT_SNAPSHOT_MISSING")]
    [InlineData("STORY_STATE_CHANGED")]
    [InlineData("OUTLINE_BLOCKED_CONTENT")]
    [InlineData("OUTLINE_RESTRICTED_CONTENT")]
    [InlineData("OUTLINE_SCHEMA_INVALID")]
    [InlineData("MODERATION_HARD_BLOCK")]
    [InlineData("OUTLINE_GRAY_ZONE_UNRESOLVED")]
    [InlineData("MODERATION_NOT_CONFIGURED")]
    [InlineData("MODERATION_PROVIDER_NOT_REGISTERED")]
    [InlineData("MODERATION_RESPONSE_INVALID")]
    [InlineData("OUTLINE_CONTAINS_PII")]
    [InlineData("OUTLINE_PROMPT_LEAKAGE")]
    public async Task ProcessNextAsync_KnownErrorCodeThrown_IsPublishedVerbatim(string code)
    {
        var finalizer = await FailWithAsync(new InvalidOperationException(code));

        Assert.Equal(code, finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_GrayZoneAppException_PublishesItsMessage()
    {
        var finalizer = await FailWithAsync(new ConflictException("GRAY_ZONE_REVIEW_REQUIRED"));

        Assert.Equal("GRAY_ZONE_REVIEW_REQUIRED", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_GrayZonePrefixOnNonAppException_IsMaskedAsGenerationFailed()
    {
        var finalizer = await FailWithAsync(new InvalidOperationException("GRAY_ZONE_REVIEW_REQUIRED"));

        Assert.Equal("OUTLINE_GENERATION_FAILED", finalizer.ErrorCode);
    }

    [Fact]
    public async Task ProcessNextAsync_AiServiceRequestException_PublishesProviderErrorCode()
    {
        var finalizer = await FailWithAsync(new AIServiceRequestException(503, "AI_PROVIDER_UNAVAILABLE", "provider down"));

        Assert.Equal("AI_PROVIDER_UNAVAILABLE", finalizer.ErrorCode);
    }

    // ---- Worker: gray-zone moderation loop ----

    [Fact]
    public async Task ProcessNextAsync_ModerationNotConfigured_SkipsGrayZoneScan()
    {
        var store = OutlineServiceTests.Seed();
        var moderation = new ScriptedModeration(configured: false);

        Assert.True(await Service(store, moderation: moderation).ProcessNextAsync());

        Assert.Empty(moderation.Scans);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_ModerationSafe_PersistsOutlineAfterOneScan()
    {
        var store = OutlineServiceTests.Seed();
        var moderation = new ScriptedModeration(configured: true, ModerationZone.Safe);

        Assert.True(await Service(store, moderation: moderation).ProcessNextAsync());

        var scan = Assert.Single(moderation.Scans);
        Assert.Equal("phase2", scan.Phase);
        Assert.Equal(4, scan.Parts.Count);
        Assert.Single(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_ModerationHardBlock_FailsWithModerationHardBlock()
    {
        var store = OutlineServiceTests.Seed();
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer,
            moderation: new ScriptedModeration(configured: true, ModerationZone.HardBlock)).ProcessNextAsync());

        Assert.Equal("MODERATION_HARD_BLOCK", finalizer.ErrorCode);
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_GrayZoneThenSafe_RetriesOnceWithStrongSafetyGuidance()
    {
        var store = OutlineServiceTests.Seed();
        var ai = new ScriptedAIClient();
        ai.Responses.Enqueue(Outline("Bản đầu"));
        ai.Responses.Enqueue(Outline("Bản an toàn"));

        Assert.True(await Service(store, ai, moderation: new ScriptedModeration(configured: true,
            ModerationZone.GrayZone, ModerationZone.Safe)).ProcessNextAsync());

        Assert.Equal(2, ai.Requests.Count);
        var retry = ai.Requests[1];
        Assert.True(retry.StrongSafetyGuidance);
        Assert.Equal("outline-job-1-safety-retry", retry.RequestId);
        Assert.Contains("scary", retry.Constraints.RestrictedTopics);
        Assert.Equal("Bản an toàn", store.Items<StoryVersion>().Single().Title);
    }

    [Fact]
    public async Task ProcessNextAsync_GrayZoneTwice_FailsWithGrayZoneUnresolved()
    {
        var store = OutlineServiceTests.Seed();
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, failureFinalizer: finalizer, moderation: new ScriptedModeration(configured: true,
            ModerationZone.GrayZone, ModerationZone.GrayZone)).ProcessNextAsync());

        Assert.Equal("OUTLINE_GRAY_ZONE_UNRESOLVED", finalizer.ErrorCode);
        Assert.Empty(store.Items<StoryVersion>());
    }

    [Fact]
    public async Task ProcessNextAsync_GrayZoneRetryReturnsUnsafeOutline_FailsWithGuardrailCode()
    {
        var store = OutlineServiceTests.Seed();
        var ai = new ScriptedAIClient();
        ai.Responses.Enqueue(Outline("Bản đầu"));
        ai.Responses.Enqueue(Outline("Gọi 0912345678 nhé"));
        var finalizer = new CapturingFinalizer(store);

        Assert.True(await Service(store, ai, finalizer, new ScriptedModeration(configured: true, ModerationZone.GrayZone))
            .ProcessNextAsync());

        Assert.Equal("OUTLINE_CONTAINS_PII", finalizer.ErrorCode);
    }

    // ---- helpers ----

    private static async Task<(Store Store, OutlineService Service, ScriptedAIClient Ai, CapturingFinalizer Finalizer)> ReadyAsync()
    {
        var store = OutlineServiceTests.Seed();
        var ai = new ScriptedAIClient();
        var finalizer = new CapturingFinalizer(store);
        var service = Service(store, ai, finalizer);
        Assert.True(await service.ProcessNextAsync());
        return (store, service, ai, finalizer);
    }

    private static async Task<CapturingFinalizer> FailWithAsync(Exception exception)
    {
        var store = OutlineServiceTests.Seed();
        var finalizer = new CapturingFinalizer(store);
        Assert.True(await Service(store, new ScriptedAIClient { Failure = exception }, finalizer).ProcessNextAsync());
        Assert.Equal(GenerationJobStatus.Failed, store.Items<StoryGenerationJob>().Single().Status);
        return finalizer;
    }

    private static OutlineService Service(
        Store store,
        IAIStoryGenerationClient? ai = null,
        IOutlineJobFailureFinalizer? failureFinalizer = null,
        IGrayZoneEvaluator? moderation = null) =>
        new(store, ai ?? new ScriptedAIClient(), new RuleBasedOutlineReviewGuardrail(),
            failureFinalizer ?? new CapturingFinalizer(store), moderation);

    private static StoryGenerationJob PendingJob(int id, GenerationJobOperation operation) => new()
    {
        Id = id, StoryId = 1, RequestedByUserId = 1, Operation = operation,
        Stage = JobStage.OutlinePending, Status = GenerationJobStatus.Pending
    };

    private static EditOutlineRequestDto Edit(string title) =>
        new() { Title = title, Opening = "Mở đầu", Development = "Phát triển", Ending = "Kết thúc" };

    private static RegenerateOutlineRequestDto Regenerate(string key) => new() { OperationKey = key };
    private static RetryOutlineRequestDto Retry(string key) => new() { OperationKey = key };
    private static ApproveOutlineRequestDto Approve(string key) => new() { ApprovalKey = key };

    private static GenerateOutlineResponse Outline(string title) => new()
    {
        RequestId = "r",
        GenerationId = "generation-1",
        Title = title,
        Outline = new StoryOutlineDto("Mở đầu", "Phát triển", "Kết thúc"),
        Metadata = new GenerationMetadataDto { ModelProvider = "test", Model = "test" }
    };

    private sealed class ScriptedAIClient : IAIStoryGenerationClient
    {
        public Queue<GenerateOutlineResponse> Responses { get; } = new();
        public List<GenerateOutlineRequest> Requests { get; } = [];
        public Exception? Failure { get; init; }
        public Action? OnCall { get; set; }

        public Task<GenerateOutlineResponse> GenerateOutlineAsync(GenerateOutlineRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            OnCall?.Invoke();
            if (Failure is not null) throw Failure;
            return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : Outline("Cáo nhỏ tốt bụng"));
        }

        public Task<GenerateStoryResponse> GenerateStoryAsync(GenerateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RefineStoryResponse> RefineStoryAsync(RefineStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EvaluateStoryResponse> EvaluateStoryAsync(EvaluateStoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ScriptedModeration(bool configured, params ModerationZone[] zones) : IGrayZoneEvaluator
    {
        private readonly Queue<ModerationZone> _zones = new(zones);
        public List<GrayZoneScan> Scans { get; } = [];
        public bool IsConfigured => configured;

        public Task<GrayZoneAssessment> EvaluateAsync(GrayZoneScan scan, CancellationToken ct)
        {
            Scans.Add(scan);
            var zone = _zones.Count > 0 ? _zones.Dequeue() : ModerationZone.Safe;
            IReadOnlyList<ModerationFinding> findings = zone == ModerationZone.Safe
                ? []
                : [new ModerationFinding("section", "opening", "scary", "test finding")];
            return Task.FromResult(new GrayZoneAssessment(zone, "fp", scan.Phase, "v1", "test", "test", null, findings));
        }
    }

    private sealed class CapturingFinalizer(Store store) : IOutlineJobFailureFinalizer
    {
        public string? ErrorCode { get; private set; }

        public Task MarkFailedAsync(int jobId, string expectedConcurrencyToken, string errorCode, CancellationToken cancellationToken = default)
        {
            ErrorCode = errorCode;
            var job = store.Items<StoryGenerationJob>().Single(item => item.Id == jobId);
            job.Status = GenerationJobStatus.Failed;
            job.Stage = JobStage.OutlineFailed;
            job.ErrorCode = errorCode;
            return Task.CompletedTask;
        }
    }
}
