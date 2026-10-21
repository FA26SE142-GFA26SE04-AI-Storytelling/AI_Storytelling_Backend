using System.Text;
using StoryPlatform.Application.Common;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class ParentAuthoringTests
{
    private static readonly CancellationToken Ct = default;
    private const string Text = "Bắp làm đổ nước.\r\n\r\nMẹ cùng Bắp lau bàn. 🌱\n\nHai mẹ con mỉm cười.";
    private static CreateParentDraftRequest Create(string op = "create") => new() { OperationId = op, ChildProfileId = 3 };
    private static SaveCreatorContentRequest Save(CreatorDraftDto draft, string op, string text = Text) => new()
    {
        OperationId = op, RevisionToken = draft.RevisionToken, BaseVersionId = draft.SelectedVersionId,
        Title = "Chiều bên con", Content = text
    };
    private static ParentCheckRequest Check(CreatorDraftDto draft, string op = "check") => new()
    { OperationId = op, RevisionToken = draft.RevisionToken, BaseVersionId = draft.SelectedVersionId };

    private static async Task<(CreatorContentSavedDto saved, ParentContentConfirmationDto confirmation)> ConfirmContent(ParentAuthoringTestStore s)
    {
        var draft = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var saved = await s.Service.SaveContentAsync(1, draft.Story.StoryId, Save(draft.Story, "save"), Ct);
        var check = await s.Service.CheckAsync(1, saved.Story.StoryId, Check(saved.Story), Ct);
        Assert.True(check.CanPrepare);
        var confirmation = await s.Service.ConfirmContentAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "content-confirm", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            CheckId = check.CheckId, Checklist = new(true, true, true, true)
        }, Ct);
        return (saved, confirmation);
    }

    [Fact]
    public async Task FinalizedCheckDoesNotCallProviderOrBreakChildReading()
    {
        var s = new ParentAuthoringTestStore();
        var (saved, confirmation) = await ConfirmContent(s);
        var package = await s.Service.PrepareAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "prepare-gray", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ContentConfirmationId = confirmation.ConfirmationId
        }, Ct);
        var grant = await s.Service.ConfirmFamilyAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "family-gray", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            PreparationId = package.PreparationId
        }, Ct);
        var evaluator = new Moq.Mock<StoryPlatform.Application.Features.Moderation.IGrayZoneEvaluator>(Moq.MockBehavior.Strict);
        evaluator.SetupGet(x => x.IsConfigured).Returns(true);
        var workflow = new StoryPlatform.Application.Features.Moderation.Services.GrayZoneWorkflowService(s.Uow.Object,
            new Moq.Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>().Object);
        var guard = new StoryPlatform.Application.Features.ChildProfiles.Supervision.Services.SupervisionAccessGuard(s.Uow.Object);
        var checker = new StoryPlatform.Application.Features.Moderation.Services.GrayZoneContentCheckService(s.Uow.Object, evaluator.Object, workflow, guard);
        var service = new ParentAuthoringService(s.Uow.Object, new(), s.Protector, guard,
            new StoryPlatform.Application.Features.MediaGeneration.Services.StorySegmentService(), checker);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CheckAsync(1, saved.Story.StoryId,
            Check(saved.Story, "finalized-gray-check"), Ct));
        Assert.Equal("PARENT_STORY_FINALIZED", error.Message);
        Assert.Empty(s.Items<ModerationItem>());
        Assert.Null(s.Items<StoryVersion>().Single().GrayZoneFlags);
        Assert.Equal(saved.Version.VersionId, (await service.ReadChildPackageAsync(3, grant.ConfirmationId, Ct)).StoryVersionId);
    }

    [Fact]
    public async Task Draft_has_child_no_version_no_AI_and_no_publication()
    {
        var s = new ParentAuthoringTestStore();
        var first = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var replay = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        Assert.Equal(first, replay); Assert.Null(first.Story.SelectedVersionId);
        var story = Assert.Single(s.Items<Story>());
        Assert.Equal(StoryScope.Parent, story.Scope); Assert.Equal(StorySource.Manual, story.Source);
        Assert.Equal(3, story.ChildProfileId); Assert.False(story.IsPublished); Assert.Null(story.ChildVisibleAt);
        Assert.Empty(s.Items<StoryVersion>()); Assert.Empty(s.Items<StoryGenerationJob>()); Assert.Empty(s.Items<StoryGenerationRequest>());
    }

    [Fact]
    public async Task Save_incomplete_replays_but_changed_payload_and_stale_tab_conflict()
    {
        var s = new ParentAuthoringTestStore(); var d = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var request = Save(d.Story, "save", "");
        var v1 = await s.Service.SaveContentAsync(1, d.Story.StoryId, request, Ct);
        Assert.Equal(v1, await s.Service.SaveContentAsync(1, d.Story.StoryId, request, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.SaveContentAsync(1, d.Story.StoryId, request with { Content = Text }, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.SaveContentAsync(1, d.Story.StoryId, request with { OperationId = "other-tab" }, Ct));
        Assert.Single(s.Items<StoryVersion>());
    }

    [Fact]
    public async Task Owner_of_other_story_or_revoked_supervisor_cannot_access_parent_draft()
    {
        var s = new ParentAuthoringTestStore(); var d = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetDraftAsync(2, d.Story.StoryId, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Creator.GetDraftAsync(1, d.Story.StoryId, Ct));
        s.Items<SupervisionRelationship>()[0].RevokedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.GetDraftAsync(1, d.Story.StoryId, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.CreateParentDraftAsync(1, Create(), Ct));
    }

    [Fact]
    public async Task File_preview_confirm_and_restore_share_writer_and_preserve_history()
    {
        var s = new ParentAuthoringTestStore(); var d = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(Text));
        var preview = await s.Service.PreviewFileAsync(1, d.Story.StoryId, d.Story.RevisionToken, "preview", "truyen.txt", bytes, Ct);
        Assert.Empty(s.Items<StoryVersion>());
        var req = new ConfirmCreatorImportRequest
        {
            OperationId = "import", RevisionToken = d.Story.RevisionToken, PreviewToken = preview.PreviewToken,
            Content = preview.Content, Title = "Kỷ niệm", SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền sử dụng"
        };
        var imported = await s.Service.ConfirmImportAsync(1, d.Story.StoryId, req, Ct);
        Assert.Equal(imported, await s.Service.ConfirmImportAsync(1, d.Story.StoryId, req, Ct));
        var second = await s.Service.SaveContentAsync(1, d.Story.StoryId, Save(imported.Story, "v2", "Bản mới"), Ct);
        var restored = await s.Service.RestoreVersionAsync(1, d.Story.StoryId, new()
        {
            OperationId = "restore", RevisionToken = second.Story.RevisionToken, BaseVersionId = second.Version.VersionId,
            SourceVersionId = imported.Version.VersionId
        }, Ct);
        Assert.Equal(3, restored.Version.VersionNo); Assert.Equal(imported.Version.Content, restored.Version.Content);
        Assert.Equal(imported.Version.VersionId, restored.Version.RestoredFromVersionId);
        Assert.Single(s.Items<StoryVersion>(), x => x.IsCurrent);
    }

    [Fact]
    public async Task Check_does_not_require_AI_consent_but_never_invents_semantic_safety_pass()
    {
        var s = new ParentAuthoringTestStore(); var d = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var saved = await s.Service.SaveContentAsync(1, d.Story.StoryId, Save(d.Story, "save"), Ct);
        s.Items<SafetyPolicy>()[0].SafetyScoreThreshold = 80;
        var check = await s.Service.CheckAsync(1, d.Story.StoryId, Check(saved.Story), Ct);
        Assert.False(check.CanPrepare); Assert.Contains("SEMANTIC_SAFETY_REVIEW_REQUIRED", check.Errors);
        Assert.Null(s.Items<StoryVersion>()[0].SafetyScore);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.ConfirmContentAsync(1, d.Story.StoryId, new()
        {
            OperationId = "confirm", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            CheckId = check.CheckId, Checklist = new(true, true, true, true)
        }, Ct));
        Assert.Empty(s.Items<StoryGenerationJob>());
    }

    [Fact]
    public async Task Saved_category_policy_is_honored_without_reapplying_optional_age_preset()
    {
        var s = new ParentAuthoringTestStore(); var d = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        s.Items<ContentCategory>().Add(new() { Id = 1, Code = "water", DisplayName = "nước", DefaultBlockedAgeBands = ["Age_6_8"] });
        s.Items<SafetyPolicyCategory>().Add(new() { Id = 1, SafetyPolicyId = 1, ContentCategoryId = 1, Rule = PolicyRule.Allowed });
        var saved = await s.Service.SaveContentAsync(1, d.Story.StoryId, Save(d.Story, "save"), Ct);
        var check = await s.Service.CheckAsync(1, d.Story.StoryId, Check(saved.Story), Ct);
        Assert.True(check.CanPrepare);
        s.Items<SafetyPolicyCategory>()[0].Rule = PolicyRule.Blocked;
        var blocked = await s.Service.CheckAsync(1, d.Story.StoryId, Check(saved.Story, "blocked-check"), Ct);
        Assert.Contains("BLOCKED_CATEGORY:water", blocked.Errors);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("content")]
    [InlineData("both")]
    public async Task Blocked_keyword_in_any_child_visible_text_prevents_content_confirmation(string field)
    {
        var s = new ParentAuthoringTestStore();
        s.Items<ContentCategory>().Add(new() { Id = 1, Code = "violence", DisplayName = "bạo lực" });
        s.Items<SafetyPolicyCategory>().Add(new() { Id = 1, SafetyPolicyId = 1, ContentCategoryId = 1, Rule = PolicyRule.Blocked });
        var draft = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var request = Save(draft.Story, "save", field is "content" or "both" ? "Nội dung có bạo lực." : "Mẹ cùng con lau bàn.")
            with { Title = field is "title" or "both" ? "VIOLENCE" : "Chiều bên con" };
        var saved = await s.Service.SaveContentAsync(1, draft.Story.StoryId, request, Ct);
        var check = await s.Service.CheckAsync(1, saved.Story.StoryId, Check(saved.Story), Ct);
        Assert.False(check.CanPrepare);
        Assert.Equal("BLOCKED_CATEGORY:violence", Assert.Single(check.Errors));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.ConfirmContentAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "confirm", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            CheckId = check.CheckId, Checklist = new(true, true, true, true)
        }, Ct));
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("inactive")]
    [InlineData("deleted-child")]
    [InlineData("deleted-relationship")]
    [InlineData("missing-permission")]
    [InlineData("deleted-permission")]
    public async Task List_filters_inaccessible_children_before_count_and_pagination(string reason)
    {
        var s = new ParentAuthoringTestStore();
        await s.Service.CreateParentDraftAsync(1, Create("inaccessible"), Ct);
        s.Items<ChildProfile>().Add(new() { Id = 4, OwnerUserId = 1, Status = ChildProfileStatus.Active, Language = "vi", AgeBand = AgeBand.Age_6_8 });
        s.Items<SupervisionRelationship>().Add(new() { Id = 2, ChildProfileId = 4, SupervisorUserId = 1, SupervisorRole = SupervisorRole.AdditionalSupervisor });
        s.Items<SupervisionPermission>().Add(new() { Id = 1, SupervisionRelationshipId = 2, Permission = Permission.GenerateStory });
        var a = await s.Service.CreateParentDraftAsync(1, Create("allowed-1") with { ChildProfileId = 4 }, Ct);
        var b = await s.Service.CreateParentDraftAsync(1, Create("allowed-2") with { ChildProfileId = 4 }, Ct);
        // Put the forbidden story first, so filtering after pagination would leave a hole.
        var origin = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        foreach (var story in s.Items<Story>()) story.UpdatedAt = origin.AddMinutes(10 - story.Id);
        switch (reason)
        {
            case "revoked": s.Items<SupervisionRelationship>()[0].RevokedAt = DateTime.UtcNow; break;
            case "inactive": s.Items<ChildProfile>()[0].Status = ChildProfileStatus.Draft; break;
            case "deleted-child": s.Items<ChildProfile>()[0].IsDeleted = true; break;
            case "deleted-relationship": s.Items<SupervisionRelationship>()[0].IsDeleted = true; break;
            case "missing-permission": s.Items<SupervisionRelationship>()[0].SupervisorRole = SupervisorRole.AdditionalSupervisor; break;
            case "deleted-permission":
                s.Items<SupervisionRelationship>()[0].SupervisorRole = SupervisorRole.AdditionalSupervisor;
                s.Items<SupervisionPermission>().Add(new() { Id = 2, SupervisionRelationshipId = 1, Permission = Permission.GenerateStory, IsDeleted = true });
                break;
        }
        var first = await s.Service.GetMineAsync(1, 1, 1, Ct);
        var second = await s.Service.GetMineAsync(1, 2, 1, Ct);
        Assert.Equal(2, first.TotalCount); Assert.Equal(2, second.TotalCount);
        Assert.Equal(a.Story.StoryId, Assert.Single(first.Items).StoryId);
        Assert.Equal(b.Story.StoryId, Assert.Single(second.Items).StoryId);
        Assert.Empty((await s.Service.GetMineAsync(1, 3, 1, Ct)).Items);
    }

    [Fact]
    public async Task Check_bound_to_policy_snapshot_and_saved_version()
    {
        var s = new ParentAuthoringTestStore(); var (saved, confirmation) = await ConfirmContent(s);
        s.Items<SafetyPolicy>()[0].MaxStoryLength = 1;
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.PrepareAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "prepare", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ContentConfirmationId = confirmation.ConfirmationId
        }, Ct));
    }

    [Fact]
    public async Task Preparation_preserves_all_UTF16_text_and_old_grant_after_new_draft_save()
    {
        var s = new ParentAuthoringTestStore(); var (saved, confirmation) = await ConfirmContent(s);
        var prepareReq = new ParentPrepareRequest { OperationId = "prepare", RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId, ContentConfirmationId = confirmation.ConfirmationId };
        var package = await s.Service.PrepareAsync(1, saved.Story.StoryId, prepareReq, Ct);
        Assert.True(package.IsReady); Assert.Equal(Text, string.Concat(package.Segments.Select(x => x.Text)));
        Assert.Equal(0, package.Segments[0].StartOffset); Assert.Equal(Text.Length, package.Segments[^1].EndOffset);
        Assert.DoesNotContain(s.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
        var request = new ParentFamilyConfirmRequest { OperationId = "family", RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId, PreparationId = package.PreparationId };
        var grant = await s.Service.ConfirmFamilyAsync(1, saved.Story.StoryId, request, Ct);
        Assert.Equal(grant, await s.Service.ConfirmFamilyAsync(1, saved.Story.StoryId, request, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.ReadChildPackageAsync(99, grant.ConfirmationId, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.SaveContentAsync(1, saved.Story.StoryId, Save(saved.Story, "v2", "Bản chưa xác nhận"), Ct));
        var childReads = await s.Service.ReadChildPackageAsync(3, grant.ConfirmationId, Ct);
        Assert.Equal(saved.Version.VersionId, childReads.StoryVersionId);
        Assert.Equal(Text, string.Concat(childReads.Segments.Select(x => x.Text)));
        Assert.Single(await s.Service.ChildLibraryAsync(3, Ct));
        var workflow = await s.Service.WorkflowAsync(1, saved.Story.StoryId, Ct);
        Assert.True(Assert.Single(workflow, x => x.Id == package.PreparationId).SourceIsCurrent);
        Assert.False(s.Items<Story>()[0].IsPublished); Assert.Equal(StoryStatus.Draft, s.Items<Story>()[0].Status);
        Assert.Empty(s.Items<StoryGenerationJob>()); Assert.Empty(s.Items<StoryGenerationRequest>());
    }

    [Fact]
    public async Task Listening_without_audio_remains_not_ready_and_cannot_be_granted()
    {
        var s = new ParentAuthoringTestStore(); var d = await s.Service.CreateParentDraftAsync(1, Create() with { ExperienceMode = "listening" }, Ct);
        var saved = await s.Service.SaveContentAsync(1, d.Story.StoryId, Save(d.Story, "save"), Ct);
        var check = await s.Service.CheckAsync(1, d.Story.StoryId, Check(saved.Story), Ct);
        var confirm = await s.Service.ConfirmContentAsync(1, d.Story.StoryId, new()
        {
            OperationId = "confirm", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            CheckId = check.CheckId, Checklist = new(true, true, true, true)
        }, Ct);
        var pkg = await s.Service.PrepareAsync(1, d.Story.StoryId, new()
        {
            OperationId = "prep", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ContentConfirmationId = confirm.ConfirmationId
        }, Ct);
        Assert.False(pkg.IsReady);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.ConfirmFamilyAsync(1, d.Story.StoryId, new()
        {
            OperationId = "grant", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            PreparationId = pkg.PreparationId
        }, Ct));
    }

    [Fact]
    public async Task Explicit_split_cannot_cut_surrogate_pair_or_drop_suffix()
    {
        var s = new ParentAuthoringTestStore(); var (saved, confirmation) = await ConfirmContent(s);
        var req = new ParentPrepareRequest { OperationId = "prep", RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId, ContentConfirmationId = confirmation.ConfirmationId,
            SegmentEnds = [Text.IndexOf("🌱", StringComparison.Ordinal) + 1, Text.Length] };
        await Assert.ThrowsAsync<BadRequestException>(() => s.Service.PrepareAsync(1, saved.Story.StoryId, req, Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => s.Service.PrepareAsync(1, saved.Story.StoryId,
            req with { OperationId = "prep-2", SegmentEnds = [Text.Length - 1] }, Ct));
    }

    [Fact]
    public async Task Purpose_only_change_does_not_invalidate_preparation_but_mode_change_does()
    {
        var s = new ParentAuthoringTestStore(); var (saved, confirmation) = await ConfirmContent(s);
        var pkg = await s.Service.PrepareAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "prep", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ContentConfirmationId = confirmation.ConfirmationId
        }, Ct);
        var setup = new ParentSetupRequest { OperationId = "purpose", RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId, Purpose = "message" };
        var changed = await s.Service.SetupAsync(1, saved.Story.StoryId, setup, Ct);
        var preview = await s.Service.PreviewAsync(1, saved.Story.StoryId, pkg.PreparationId, Ct);
        Assert.Equal(pkg.ContextFingerprint, preview.ContextFingerprint);
        Assert.Equal(pkg.Segments, preview.Segments);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.SetupAsync(1, saved.Story.StoryId, setup with { Purpose = "free" }, Ct));
        await s.Service.SetupAsync(1, saved.Story.StoryId, setup with { OperationId = "mode", RevisionToken = changed.Story.RevisionToken, ExperienceMode = "combined" }, Ct);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.PreviewAsync(1, saved.Story.StoryId, pkg.PreparationId, Ct));
    }

    [Fact]
    public async Task Rejected_command_rolls_back_receipt_and_new_content_needs_new_confirmation()
    {
        var s = new ParentAuthoringTestStore(); var (saved, confirmation) = await ConfirmContent(s);
        var second = await s.Service.SaveContentAsync(1, saved.Story.StoryId, Save(saved.Story, "v2"), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.PrepareAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "bad-prep", RevisionToken = second.Story.RevisionToken, BaseVersionId = second.Version.VersionId,
            ContentConfirmationId = confirmation.ConfirmationId
        }, Ct));
        Assert.DoesNotContain(s.Items<ParentStoryOperation>(), x => x.OperationId == "bad-prep");
    }

    [Fact]
    public async Task Revoked_issuing_permission_blocks_child_package_and_grant_replay()
    {
        var s = new ParentAuthoringTestStore(); var (saved, confirmation) = await ConfirmContent(s);
        var pkg = await s.Service.PrepareAsync(1, saved.Story.StoryId, new()
        {
            OperationId = "prep", RevisionToken = saved.Story.RevisionToken, BaseVersionId = saved.Version.VersionId,
            ContentConfirmationId = confirmation.ConfirmationId
        }, Ct);
        var request = new ParentFamilyConfirmRequest { OperationId = "grant", RevisionToken = saved.Story.RevisionToken,
            BaseVersionId = saved.Version.VersionId, PreparationId = pkg.PreparationId };
        var grant = await s.Service.ConfirmFamilyAsync(1, saved.Story.StoryId, request, Ct);
        s.Items<SupervisionRelationship>()[0].RevokedAt = DateTime.UtcNow;
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.ReadChildPackageAsync(3, grant.ConfirmationId, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => s.Service.ConfirmFamilyAsync(1, saved.Story.StoryId, request, Ct));
        Assert.Empty(await s.Service.ChildLibraryAsync(3, Ct));
    }

    [Fact]
    public async Task Cross_story_check_and_import_token_cannot_be_used()
    {
        var s = new ParentAuthoringTestStore(); var d1 = await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var preview = await s.Service.PreviewImportAsync(1, d1.Story.StoryId, new() { OperationId = "preview", RevisionToken = d1.Story.RevisionToken, Content = Text }, Ct);
        var d2 = await s.Service.CreateParentDraftAsync(1, Create("create-2"), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.ConfirmImportAsync(1, d2.Story.StoryId, new()
        {
            OperationId = "import", RevisionToken = d2.Story.RevisionToken, PreviewToken = preview.PreviewToken,
            Title = "Truyện", Content = preview.Content, SourceDeclaration = "Tự viết", RightsDeclaration = "Tôi có quyền"
        }, Ct));
        Assert.Empty(s.Items<StoryVersion>());
    }

    [Fact]
    public async Task Legacy_generation_context_refuses_new_family_workflow()
    {
        var s = new ParentAuthoringTestStore(); await s.Service.CreateParentDraftAsync(1, Create(), Ct);
        var ex = Assert.Throws<ConflictException>(() => ParentStoryContext.RequireChildId(s.Items<Story>()[0]));
        Assert.Equal("PARENT_MANUAL_USE_AUTHORING_API", ex.Message);
    }

    [Fact]
    public async Task Catalog_is_shared_deterministic_and_rendering_is_plain_text_not_recursive()
    {
        var guides = new ParentAuthoringTestStore().Guides;
        Assert.Equal(21, (await guides.GetAsync("vi", "family_memory", null, 1, 100, Ct)).TotalCount);
        var a = await guides.GetAsync("vi", "message", "dialogue", 1, 10, Ct);
        var b = await guides.GetAsync("vi", "message", "dialogue", 1, 10, Ct);
        // Each call deserializes fresh arrays, so compare content rather than array references.
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a.Items), System.Text.Json.JsonSerializer.Serialize(b.Items));
        Assert.Equal(3, a.TotalCount);
        Assert.Empty((await guides.GetAsync("en", null, null, 1, 10, Ct)).Items);
        var rendered = await guides.RenderAsync("dialogue-1", new(new() { ["person"] = "<b>An</b>", ["words"] = "{{person}}" }), Ct);
        Assert.True(rendered.IsPlainText); Assert.Contains("<b>An</b>", rendered.Text); Assert.Contains("{{person}}", rendered.Text);
        await Assert.ThrowsAsync<BadRequestException>(() => guides.RenderAsync("dialogue-1", new(new() { ["person"] = "An" }), Ct));
    }
}
