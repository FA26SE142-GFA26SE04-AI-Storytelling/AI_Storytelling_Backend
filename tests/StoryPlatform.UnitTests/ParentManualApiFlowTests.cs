using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using StoryPlatform.Application.Features.CreatorAssistance;
using StoryPlatform.Application.Features.CreatorAssistance.Services;
using StoryPlatform.Application.Features.CreatorAssistance.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Services;
using StoryPlatform.Contracts.AI.CreatorAssistance;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Api.Middleware;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ParentAuthoring;
using StoryPlatform.Application.Features.Stories.Interfaces;
using StoryPlatform.Application.Features.Stories.Services;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Infrastructure.Security;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>
/// Loopback HTTP/Kestrel tests of production controllers, JWT authentication/authorization,
/// exception middleware and services. Persistence is transactional in-memory fake, NOT PostgreSQL.
/// No production Program startup, migrations, seeds, AI provider or external database is invoked.
/// </summary>
public sealed class ParentManualApiFlowTests
{
    private const string Body = "Mi mở chiếc hộp giấy.\r\n\r\nMẹ cùng Mi dán lại góc hộp. 🌱\n\nMi cất những tấm thiệp vào hộp.";

    [Fact]
    public async Task Manual_HTTP_flow_pins_child_package_through_save_restore_and_revocation()
    {
        await using var host = await ApiHost.StartAsync();
        var guide = await host.SendAsync(HttpMethod.Get, "/api/v1/authoring-guides?locale=vi&purpose=family_memory&group=opening", null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(3, guide.GetProperty("items").GetArrayLength());
        var rendered = await host.SendAsync(HttpMethod.Post, "/api/v1/authoring-guides/dialogue-1/render",
            new { values = new Dictionary<string, string> { ["person"] = "Mẹ", ["words"] = "Mẹ con mình cùng lau nhé" } }, host.Parent, HttpStatusCode.OK);
        Assert.True(rendered.GetProperty("isPlainText").GetBoolean());
        var draft = await host.CreateAsync();
        var id = draft.GetProperty("story").GetProperty("storyId").GetInt32();
        var r0 = draft.GetProperty("story").GetProperty("revisionToken").GetString()!;
        var path = $"/api/v1/parent/stories/{id}";
        Assert.Equal(3, (await host.SendAsync(HttpMethod.Get, path, null, host.Parent, HttpStatusCode.OK)).GetProperty("childProfileId").GetInt32());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("story").GetProperty("selectedVersionId").ValueKind);
        Assert.Empty((await host.SendAsync(HttpMethod.Get, path + "/content/versions", null, host.Parent, HttpStatusCode.OK)).EnumerateArray());
        await host.SendAsync(HttpMethod.Get, "/api/v1/child/manual-stories", null, host.Parent, HttpStatusCode.Forbidden);

        var saveRequest = new { operationId = "save-v1", revisionToken = r0, baseVersionId = (int?)null, title = "Chiếc hộp của Mi", content = Body };
        var saved = await host.SendAsync(HttpMethod.Post, path + "/manual/versions", saveRequest, host.Parent, HttpStatusCode.Created);
        var v1 = saved.GetProperty("version").GetProperty("versionId").GetInt32();
        var r1 = saved.GetProperty("story").GetProperty("revisionToken").GetString()!;
        var replay = await host.SendAsync(HttpMethod.Post, path + "/manual/versions", saveRequest, host.Parent, HttpStatusCode.Created);
        Assert.Equal(v1, replay.GetProperty("version").GetProperty("versionId").GetInt32());
        await host.SendAsync(HttpMethod.Post, path + "/manual/versions", saveRequest with { operationId = "stale-tab" }, host.Parent, HttpStatusCode.Conflict);
        await host.SendAsync(HttpMethod.Post, path + "/manual/versions", saveRequest with { content = "Changed" }, host.Parent, HttpStatusCode.Conflict);

        var check = await host.SendAsync(HttpMethod.Post, path + "/content/checks", new { operationId = "check", revisionToken = r1, baseVersionId = v1 }, host.Parent, HttpStatusCode.OK);
        Assert.True(check.GetProperty("canPrepare").GetBoolean());
        var confirmation = await host.SendAsync(HttpMethod.Post, path + "/content/confirm", new
        {
            operationId = "confirm-content",
            revisionToken = r1,
            baseVersionId = v1,
            checkId = check.GetProperty("checkId").GetInt32(),
            checklist = new { intendedMeaning = true, suitableForChild = true, privacyChecked = true, endingChecked = true }
        }, host.Parent, HttpStatusCode.OK);
        var package = await host.SendAsync(HttpMethod.Post, path + "/preparations", new
        { operationId = "prepare", revisionToken = r1, baseVersionId = v1, contentConfirmationId = confirmation.GetProperty("confirmationId").GetInt32() }, host.Parent, HttpStatusCode.Created);
        var preparationId = package.GetProperty("preparationId").GetInt32();
        Assert.True(package.GetProperty("isReady").GetBoolean());
        Assert.Equal(Body, Text(package));
        Assert.Equal(Body, Text(await host.SendAsync(HttpMethod.Get, path + $"/preparations/{preparationId}", null, host.Parent, HttpStatusCode.OK)));
        Assert.Empty((await host.SendAsync(HttpMethod.Get, "/api/v1/child/manual-stories", null, host.Child, HttpStatusCode.OK)).EnumerateArray());

        var familyRequest = new { operationId = "family", revisionToken = r1, baseVersionId = v1, preparationId };
        var grant = await host.SendAsync(HttpMethod.Post, path + "/family-confirmations", familyRequest, host.Parent, HttpStatusCode.Created);
        var grantId = grant.GetProperty("confirmationId").GetInt32();
        var grantReplay = await host.SendAsync(HttpMethod.Post, path + "/family-confirmations", familyRequest, host.Parent, HttpStatusCode.Created);
        Assert.Equal(grantId, grantReplay.GetProperty("confirmationId").GetInt32());
        var readPath = $"/api/v1/child/manual-stories/{grantId}/package";
        Assert.Equal(Body, Text(await host.SendAsync(HttpMethod.Get, readPath, null, host.Child, HttpStatusCode.OK)));
        await host.SendAsync(HttpMethod.Get, readPath, null, host.OtherChild, HttpStatusCode.NotFound);
        await host.SendAsync(HttpMethod.Get, $"/api/v1/Story/{id}", null, null, HttpStatusCode.NotFound);

        var readOnly = await host.SendAsync(HttpMethod.Get, path, null, host.Parent, HttpStatusCode.OK);
        Assert.True(readOnly.GetProperty("isReadOnly").GetBoolean());
        await host.SendAsync(HttpMethod.Post, path + "/manual/versions", new
        { operationId = "blocked-save", revisionToken = r1, baseVersionId = v1, title = "Bản mới", content = "Chưa thu hồi." }, host.Parent, HttpStatusCode.Conflict);
        await host.SendAsync(HttpMethod.Post, path + "/family-confirmations/revoke", new
        { operationId = "revoke", revisionToken = r1, baseVersionId = v1, confirmationId = grantId }, host.Parent, HttpStatusCode.OK);
        await host.SendAsync(HttpMethod.Get, readPath, null, host.Child, HttpStatusCode.NotFound);
        var reopened = await host.SendAsync(HttpMethod.Get, path, null, host.Parent, HttpStatusCode.OK);
        Assert.False(reopened.GetProperty("isReadOnly").GetBoolean());
        var reopenedToken = reopened.GetProperty("story").GetProperty("revisionToken").GetString();
        Assert.NotEqual(r1, reopenedToken);
        var edited = await host.SendAsync(HttpMethod.Post, path + "/manual/versions", new
        { operationId = "save-v2", revisionToken = reopenedToken, baseVersionId = v1, title = "Bản nháp mới", content = "Nội dung chưa xác nhận." }, host.Parent, HttpStatusCode.Created);
        var v2 = edited.GetProperty("version").GetProperty("versionId").GetInt32();
        var r2 = edited.GetProperty("story").GetProperty("revisionToken").GetString()!;
        await host.SendAsync(HttpMethod.Get, readPath, null, host.Child, HttpStatusCode.NotFound);
        await host.SendAsync(HttpMethod.Get, path + $"/preparations/{preparationId}", null, host.Parent, HttpStatusCode.NotFound);
        var restored = await host.SendAsync(HttpMethod.Post, path + "/content/restore", new
        { operationId = "restore", revisionToken = r2, baseVersionId = v2, sourceVersionId = v1 }, host.Parent, HttpStatusCode.Created);
        Assert.Equal(3, restored.GetProperty("version").GetProperty("versionNo").GetInt32());
        Assert.Equal(Body, restored.GetProperty("version").GetProperty("content").GetString());
        var history = await host.SendAsync(HttpMethod.Get, path + "/content/versions", null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(3, history.GetArrayLength());
        var original = await host.SendAsync(HttpMethod.Get, path + $"/content/versions/{v1}", null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(Body, original.GetProperty("content").GetString());
        var workflow = await host.SendAsync(HttpMethod.Get, path + "/workflow", null, host.Parent, HttpStatusCode.OK);
        Assert.False(workflow.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == preparationId).GetProperty("sourceIsCurrent").GetBoolean());
        var setup = await host.SendAsync(new HttpMethod("PATCH"), path + "/setup", new
        {
            operationId = "setup",
            revisionToken = restored.GetProperty("story").GetProperty("revisionToken").GetString(),
            baseVersionId = restored.GetProperty("version").GetProperty("versionId").GetInt32(),
            purpose = "message"
        }, host.Parent, HttpStatusCode.OK);
        await host.SendAsync(HttpMethod.Get, readPath, null, host.Child, HttpStatusCode.NotFound);
        Assert.Empty((await host.SendAsync(HttpMethod.Get, "/api/v1/child/manual-stories", null, host.Child, HttpStatusCode.OK)).EnumerateArray());
        Assert.Equal(3, host.Store.Items<StoryVersion>().Count);
        Assert.Single(host.Store.Items<StoryVersion>(), x => x.IsCurrent);
        Assert.Empty(host.Store.Items<StoryGenerationJob>()); Assert.Empty(host.Store.Items<StoryGenerationRequest>());
        Assert.Empty(host.Store.Items<ReadingSession>()); Assert.False(host.Store.Items<Story>()[0].IsPublished);
    }

    [Fact]
    public async Task HTTP_authentication_roles_validation_and_ownership_are_enforced()
    {
        await using var host = await ApiHost.StartAsync();
        var payload = new { operationId = "create", childProfileId = 3 };
        await host.SendAsync(HttpMethod.Post, "/api/v1/parent/stories/manual", payload, null, HttpStatusCode.Unauthorized);
        await host.SendAsync(HttpMethod.Post, "/api/v1/parent/stories/manual", payload, host.Child, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Post, "/api/v1/parent/stories/manual", payload, host.OtherParent, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Post, "/api/v1/parent/stories/manual", payload with { childProfileId = 0 }, host.Parent, HttpStatusCode.BadRequest);
        var draft = await host.CreateAsync(); var id = draft.GetProperty("story").GetProperty("storyId").GetInt32();
        await host.SendAsync(HttpMethod.Get, $"/api/v1/parent/stories/{id}", null, host.OtherParent, HttpStatusCode.NotFound);
        await host.SendAsync(HttpMethod.Get, $"/api/v1/parent/stories/{id}", null, host.Child, HttpStatusCode.Forbidden);
        host.Store.Items<UserAccount>()[0].TokenVersion++;
        await host.SendAsync(HttpMethod.Get, $"/api/v1/parent/stories/{id}", null, host.Parent, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HTTP_multipart_import_preview_confirm_and_replay_create_only_one_version()
    {
        await using var host = await ApiHost.StartAsync();
        var draft = await host.CreateAsync(); var story = draft.GetProperty("story");
        var id = story.GetProperty("storyId").GetInt32(); var token = story.GetProperty("revisionToken").GetString()!;
        var path = $"/api/v1/parent/stories/{id}";
        var pastePreview = await host.SendAsync(HttpMethod.Post, path + "/imports/preview", new
        { operationId = "preview-paste", revisionToken = token, content = Body }, host.Parent, HttpStatusCode.OK);
        Assert.False(string.IsNullOrWhiteSpace(pastePreview.GetProperty("content").GetString()));
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(token), "RevisionToken");
        multipart.Add(new StringContent("preview-file"), "OperationId");
        multipart.Add(new StringContent(Body, Encoding.UTF8, "text/plain"), "File", "truyen.txt");
        using var upload = new HttpRequestMessage(HttpMethod.Post, path + "/imports/preview-file") { Content = multipart };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", host.Parent);
        using var response = await host.Client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Empty(host.Store.Items<StoryVersion>());
        var request = new
        {
            operationId = "import-confirm",
            revisionToken = token,
            baseVersionId = (int?)null,
            previewToken = preview.GetProperty("previewToken").GetString(),
            content = preview.GetProperty("content").GetString(),
            title = "Chiếc hộp của Mi",
            sourceDeclaration = "Tự sáng tác",
            rightsDeclaration = "Tôi có quyền sử dụng"
        };
        await host.SendAsync(HttpMethod.Post, path + "/imports/confirm", request with { previewToken = "invalid" }, host.Parent, HttpStatusCode.BadRequest);
        var saved = await host.SendAsync(HttpMethod.Post, path + "/imports/confirm", request, host.Parent, HttpStatusCode.Created);
        var replay = await host.SendAsync(HttpMethod.Post, path + "/imports/confirm", request, host.Parent, HttpStatusCode.Created);
        Assert.Equal(saved.GetProperty("version").GetProperty("versionId").GetInt32(), replay.GetProperty("version").GetProperty("versionId").GetInt32());
        Assert.Single(host.Store.Items<StoryVersion>());
    }

    [Fact]
    public async Task HTTP_blocked_title_cannot_be_confirmed_and_revoked_child_does_not_break_listing()
    {
        await using var host = await ApiHost.StartAsync();
        host.Store.Items<ContentCategory>().Add(new() { Id = 1, Code = "violence", DisplayName = "bạo lực" });
        host.Store.Items<SafetyPolicyCategory>().Add(new() { Id = 1, SafetyPolicyId = 1, ContentCategoryId = 1, Rule = PolicyRule.Blocked });
        var draft = await host.CreateAsync(); var story = draft.GetProperty("story");
        var id = story.GetProperty("storyId").GetInt32(); var path = $"/api/v1/parent/stories/{id}";
        var saved = await host.SendAsync(HttpMethod.Post, path + "/manual/versions", new
        { operationId = "save", revisionToken = story.GetProperty("revisionToken").GetString(), baseVersionId = (int?)null, title = "VIOLENCE", content = "Mẹ cùng con lau bàn." }, host.Parent, HttpStatusCode.Created);
        var r1 = saved.GetProperty("story").GetProperty("revisionToken").GetString(); var v1 = saved.GetProperty("version").GetProperty("versionId").GetInt32();
        var check = await host.SendAsync(HttpMethod.Post, path + "/content/checks", new { operationId = "check", revisionToken = r1, baseVersionId = v1 }, host.Parent, HttpStatusCode.OK);
        Assert.False(check.GetProperty("canPrepare").GetBoolean());
        Assert.Contains("BLOCKED_CATEGORY:violence", check.GetProperty("errors").EnumerateArray().Select(x => x.GetString()));
        await host.SendAsync(HttpMethod.Post, path + "/content/confirm", new
        {
            operationId = "confirm",
            revisionToken = r1,
            baseVersionId = v1,
            checkId = check.GetProperty("checkId").GetInt32(),
            checklist = new { intendedMeaning = true, suitableForChild = true, privacyChecked = true, endingChecked = true }
        }, host.Parent, HttpStatusCode.Conflict);
        var otherDraft = await host.CreateAsync("create-other-child", 4);
        host.Store.Items<SupervisionRelationship>().Single(x => x.ChildProfileId == 3).RevokedAt = DateTime.UtcNow;
        var list = await host.SendAsync(HttpMethod.Get, "/api/v1/parent/stories?pageIndex=1&pageSize=1", null, host.Parent, HttpStatusCode.OK);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(otherDraft.GetProperty("story").GetProperty("storyId").GetInt32(), list.GetProperty("items")[0].GetProperty("storyId").GetInt32());
    }

    private static string Text(JsonElement package) => string.Concat(package.GetProperty("segments").EnumerateArray().Select(x => x.GetProperty("text").GetString()));

    [Theory]
    [InlineData("semantic-safety")]
    [InlineData("listening")]
    public async Task HTTP_missing_policy_evidence_or_audio_cannot_grant_a_package(string scenario)
    {
        await using var host = await ApiHost.StartAsync();
        if (scenario == "semantic-safety") host.Store.Items<SafetyPolicy>()[0].SafetyScoreThreshold = 80;
        var draft = await host.SendAsync(HttpMethod.Post, "/api/v1/parent/stories/manual", new
        { operationId = "create", childProfileId = 3, experienceMode = scenario == "listening" ? "listening" : "reading" }, host.Parent, HttpStatusCode.Created);
        var path = $"/api/v1/parent/stories/{draft.GetProperty("story").GetProperty("storyId").GetInt32()}";
        var saved = await host.SendAsync(HttpMethod.Post, path + "/manual/versions", new
        {
            operationId = "save",
            revisionToken = draft.GetProperty("story").GetProperty("revisionToken").GetString(),
            baseVersionId = (int?)null,
            title = "Chiếc hộp của Mi",
            content = Body
        }, host.Parent, HttpStatusCode.Created);
        var r1 = saved.GetProperty("story").GetProperty("revisionToken").GetString();
        var v1 = saved.GetProperty("version").GetProperty("versionId").GetInt32();
        var check = await host.SendAsync(HttpMethod.Post, path + "/content/checks", new
        { operationId = "check", revisionToken = r1, baseVersionId = v1 }, host.Parent, HttpStatusCode.OK);
        var confirmRequest = new
        {
            operationId = "confirm",
            revisionToken = r1,
            baseVersionId = v1,
            checkId = check.GetProperty("checkId").GetInt32(),
            checklist = new { intendedMeaning = true, suitableForChild = true, privacyChecked = true, endingChecked = true }
        };
        if (scenario == "semantic-safety")
        {
            Assert.False(check.GetProperty("canPrepare").GetBoolean());
            Assert.Contains("SEMANTIC_SAFETY_REVIEW_REQUIRED", check.GetProperty("errors").EnumerateArray().Select(x => x.GetString()));
            await host.SendAsync(HttpMethod.Post, path + "/content/confirm", confirmRequest, host.Parent, HttpStatusCode.Conflict);
        }
        else
        {
            var confirm = await host.SendAsync(HttpMethod.Post, path + "/content/confirm", confirmRequest, host.Parent, HttpStatusCode.OK);
            var package = await host.SendAsync(HttpMethod.Post, path + "/preparations", new
            { operationId = "prepare", revisionToken = r1, baseVersionId = v1, contentConfirmationId = confirm.GetProperty("confirmationId").GetInt32() }, host.Parent, HttpStatusCode.Created);
            Assert.False(package.GetProperty("isReady").GetBoolean());
            await host.SendAsync(HttpMethod.Post, path + "/family-confirmations", new
            { operationId = "grant", revisionToken = r1, baseVersionId = v1, preparationId = package.GetProperty("preparationId").GetInt32() }, host.Parent, HttpStatusCode.Conflict);
        }
        Assert.DoesNotContain(host.Store.Items<ParentStoryOperation>(), x => x.Kind == "FamilyConfirmation");
        Assert.Empty(host.Store.Items<StoryGenerationJob>()); Assert.Empty(host.Store.Items<StoryGenerationRequest>());
    }

    [Theory]
    [InlineData("ContinueWriting")]
    [InlineData("SupportSelection")]
    [InlineData("ReviewDraft")]
    public async Task Parent_assistance_HTTP_endpoints_and_provenance_work_without_generation(string entry)
    {
        await using var host = await ApiHost.StartAsync();
        var policy = host.Store.Items<SafetyPolicy>()[0];
        policy.ConsentRecorded = true; policy.ConsentRecordedAt = DateTime.UtcNow;
        var draft = await host.CreateAsync();
        Assert.True(draft.GetProperty("canUseAiAssistance").GetBoolean());
        var story = draft.GetProperty("story"); var id = story.GetProperty("storyId").GetInt32();
        var revision = story.GetProperty("revisionToken").GetString();
        var path = $"/api/v1/parent/stories/{id}/assistance-tasks";
        var payload = new
        {
            operationId = "task",
            revisionToken = revision,
            baseVersionId = (int?)null,
            entryPoint = entry,
            editorSnapshot = new { title = "Chiếc hộp", content = Body },
            goal = "Khám phá hướng tiếp theo",
            target = new
            {
                kind = entry == "ContinueWriting" ? "cursor" : entry == "SupportSelection" ? "selection" : "wholeDraft",
                start = entry == "ContinueWriting" ? Body.Length : 0,
                length = entry == "ContinueWriting" ? 0 : entry == "SupportSelection" ? 2 : Body.Length
            }
        };
        await host.SendAsync(HttpMethod.Post, path, payload, host.Child, HttpStatusCode.Forbidden);
        await host.SendAsync(HttpMethod.Post, path, payload, host.OtherParent, HttpStatusCode.NotFound);
        var task = await host.SendAsync(HttpMethod.Post, path, payload, host.Parent, HttpStatusCode.Created);
        var taskId = task.GetProperty("taskId").GetInt32();
        await host.SendAsync(HttpMethod.Get, $"/api/v1/creator/stories/{id}/assistance-tasks/{taskId}", null, host.Parent, HttpStatusCode.NotFound);
        var options = await host.SendAsync(HttpMethod.Post, path + $"/{taskId}/suggestions", new
        { operationId = "suggest", expectedTaskRevision = task.GetProperty("taskRevision").GetInt32() }, host.Parent, HttpStatusCode.OK);
        Assert.Empty(host.Store.Items<StoryVersion>());
        if (entry != "ReviewDraft")
        {
            var direction = await host.SendAsync(HttpMethod.Post, path + $"/{taskId}/direction", new
            { operationId = "direction", expectedTaskRevision = options.GetProperty("taskRevision").GetInt32(), optionsStepId = options.GetProperty("stepId").GetInt32(), optionId = "a" }, host.Parent, HttpStatusCode.OK);
            var trial = await host.SendAsync(HttpMethod.Post, path + $"/{taskId}/trials", new
            { operationId = "trial", expectedTaskRevision = direction.GetProperty("taskRevision").GetInt32(), directionStepId = direction.GetProperty("stepId").GetInt32() }, host.Parent, HttpStatusCode.OK);
            await host.SendAsync(HttpMethod.Post, $"/api/v1/parent/stories/{id}/manual/versions", new
            {
                operationId = "save",
                revisionToken = revision,
                baseVersionId = (int?)null,
                title = "Chiếc hộp",
                content = Body + " Mi đã sửa xong.",
                assistanceReferences = new[] { new { taskId, stepId = trial.GetProperty("stepId").GetInt32(), usage = "TrialUsed" } }
            }, host.Parent, HttpStatusCode.Created);
            Assert.Single(host.Store.Items<StoryVersion>());
            Assert.NotNull(host.Store.Items<StoryVersion>()[0].AuthoringMetadata.AssistanceProvenanceJson);
        }
        else Assert.Equal("ReviewReady", options.GetProperty("status").GetString());
        await host.SendAsync(HttpMethod.Get, path + $"/{taskId}/steps", null, host.Parent, HttpStatusCode.OK);
        await host.SendAsync(HttpMethod.Get, path, null, host.Parent, HttpStatusCode.OK);
        Assert.Empty(host.Store.Items<StoryGenerationRequest>()); Assert.Empty(host.Store.Items<StoryGenerationJob>());
    }

    internal sealed class ApiHost(WebApplication app, HttpClient client, ParentAuthoringTestStore store,
        string parent, string otherParent, string child, string otherChild) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public ParentAuthoringTestStore Store { get; } = store;
        public string Parent { get; } = parent;
        public string OtherParent { get; } = otherParent;
        public string Child { get; } = child;
        public string OtherChild { get; } = otherChild;
        public string Creator { get; private set; } = string.Empty;
        public string Moderator { get; private set; } = string.Empty;
        public string Administrator { get; private set; } = string.Empty;

        public static async Task<ApiHost> StartAsync(StoryPlatform.Application.Features.StoryReview.Interfaces.IStoryReviewService? reviewService = null)
        {
            var store = new ParentAuthoringTestStore();
            store.Items<UserAccount>().Add(new() { Id = 13, Role = UserRole.ContentCreator, Status = AccountStatus.Active });
            store.Items<UserAccount>().Add(new() { Id = 14, Role = UserRole.Moderator, Status = AccountStatus.Active });
            store.Items<ChildProfile>().Add(new() { Id = 4, OwnerUserId = 1, Language = "vi", AgeBand = AgeBand.Age_6_8, Status = ChildProfileStatus.Active });
            store.Items<SupervisionRelationship>().Add(new() { Id = 2, ChildProfileId = 4, SupervisorUserId = 1, SupervisorRole = SupervisorRole.Owner });
            store.Items<SafetyPolicy>().Add(new() { Id = 2, ChildProfileId = 4, MaxStoryLength = 5000 });
            store.Items<ChildSession>().AddRange([
                new() { Id = 1, ChildProfileId = 3, SessionKey = "http-child-3", LastActivityAt = DateTime.UtcNow },
                new() { Id = 2, ChildProfileId = 4, SessionKey = "http-child-4", LastActivityAt = DateTime.UtcNow }
            ]);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(ParentAuthoringController).Assembly.FullName, EnvironmentName = "Testing", ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
                ["JwtSettings:Issuer"] = "ParentApiHttpTest",
                ["JwtSettings:Audience"] = "ParentApiHttpTestClient",
                ["JwtSettings:ExpiryMinutes"] = "15",
                ["JwtSettings:RefreshTokenExpiryDays"] = "1",
                ["JwtSettings:ChildTokenExpiryMinutes"] = "15"
            });
            builder.Services.AddControllers().AddApplicationPart(typeof(ParentAuthoringController).Assembly);
            builder.Services.AddSingleton<IUnitOfWork>(store.Uow.Object);
            if (reviewService is not null)
            {
                builder.Services.AddSingleton(reviewService);
                builder.Services.AddSingleton(new Mock<StoryPlatform.Application.Features.StoryReview.Interfaces.IProposalService>(MockBehavior.Strict).Object);
            }
            var audit = new Mock<StoryPlatform.Application.Features.AuditLogs.Interfaces.IAuditLogWriter>();
            var queue = new StoryPlatform.Application.Features.Moderation.Services.ModerationQueueService(store.Uow.Object, audit.Object);
            builder.Services.AddSingleton<StoryPlatform.Application.Features.Moderation.Interfaces.IModerationQueueService>(queue);
            builder.Services.AddSingleton(new Mock<StoryPlatform.Application.Features.Moderation.Interfaces.IModerationReviewViewService>().Object);
            builder.Services.AddSingleton<StoryPlatform.Application.Features.Moderation.Interfaces.ICreatorPublicationService>(
                new StoryPlatform.Application.Features.Moderation.Services.CreatorPublicationService(store.Uow.Object, queue, audit.Object));
            builder.Services.AddSingleton(store.Service);
            builder.Services.AddSingleton<StoryPlatform.Application.Features.CreatorAuthoring.Interfaces.ICreatorAuthoringService>(store.Creator);
            builder.Services.AddSingleton<StoryPlatform.Application.Features.AIStoryInput.Interfaces.IAIStoryInputService>(
                new StoryPlatform.Application.Features.AIStoryInput.Services.AIStoryInputService(store.Uow.Object,
                    new StoryPlatform.Application.Features.AIStoryInput.Guardrails.RuleBasedInputGuardrail(),
                    new Mock<StoryPlatform.Application.Features.TokenQuota.Interfaces.ITokenQuotaService>(MockBehavior.Strict).Object));
            var ai = new Mock<ICreatorAssistanceAiClient>();
            ai.Setup(x => x.GenerateAsync(It.IsAny<AssistanceAiRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AssistanceAiRequest, CancellationToken>((r, _) => Task.FromResult(
                    r.Mode == "Trial" ? new AssistanceAiResult { ResultType = "Trial", Patch = new("ignored", 0, 0, " Mi cùng mẹ thử thêm một lần.", "") } :
                    r.EntryPoint == "ReviewDraft" ? new AssistanceAiResult { ResultType = "ReviewFindings", ChecksPerformed = ["Continuity"] } :
                    new AssistanceAiResult { ResultType = "Options", Options = [new("a", "Hợp tác", "Mi cùng mẹ sửa hộp."), new("b", "Kiên trì", "Mi thử cách mới.")] }));
            builder.Services.AddSingleton(new ParentAssistanceService(store.Uow.Object, ai.Object, new(), TimeProvider.System,
                new SupervisionAccessGuard(store.Uow.Object), new(), NullLogger<ParentAssistanceService>.Instance));
            builder.Services.AddSingleton<ICreatorAssistanceService>(new CreatorAssistanceService(store.Uow.Object, ai.Object, new(), TimeProvider.System));
            builder.Services.AddSingleton(new AuthoringGuideService(store.Uow.Object));
            builder.Services.AddSingleton<StoryPlatform.Application.Features.AuthoringGuideManagement.Interfaces.IAuthoringGuideManagementService>(
                new StoryPlatform.Application.Features.AuthoringGuideManagement.Services.AuthoringGuideManagementService(store.Uow.Object, audit.Object));
            builder.Services.AddSingleton<IStoryService>(new StoryService(store.Uow.Object));
            builder.Services.AddJwtAuthentication(builder.Configuration);
            builder.Services.AddAppAuthorization();
            var app = builder.Build();
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var generator = new JwtTokenGenerator(builder.Configuration);
            return new ApiHost(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) }, store,
                generator.GenerateAccessToken(store.Items<UserAccount>()[0]), generator.GenerateAccessToken(store.Items<UserAccount>()[1]),
                generator.GenerateChildAccessToken(3, "http-child-3"), generator.GenerateChildAccessToken(4, "http-child-4"))
            {
                Creator = generator.GenerateAccessToken(store.Items<UserAccount>().Single(x => x.Id == 13)),
                Moderator = generator.GenerateAccessToken(store.Items<UserAccount>().Single(x => x.Id == 14)),
                Administrator = generator.GenerateAccessToken(store.Items<UserAccount>().Single(x => x.Id == 9))
            };
        }

        public Task<JsonElement> CreateAsync(string operationId = "create", int childId = 3) =>
            SendAsync(HttpMethod.Post, "/api/v1/parent/stories/manual", new { operationId, childProfileId = childId }, Parent, HttpStatusCode.Created);

        public async Task<JsonElement> SendAsync(HttpMethod method, string path, object? payload, string? token, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, path);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await Client.SendAsync(request);
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{method} {path}: expected {(int)expected}, got {(int)response.StatusCode}; {raw}");
            if (expected != HttpStatusCode.OK && expected != HttpStatusCode.Created && expected != HttpStatusCode.Accepted) return default;
            var envelope = JsonSerializer.Deserialize<JsonElement>(raw);
            Assert.True(envelope.GetProperty("success").GetBoolean());
            return envelope.GetProperty("data").Clone();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await app.StopAsync(); await app.DisposeAsync();
        }
    }
}
