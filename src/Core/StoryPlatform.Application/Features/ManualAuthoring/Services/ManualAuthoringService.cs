using StoryPlatform.Application.Features.CreatorAuthoring;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Application.Features.CreatorAuthoring.DTOs;
using StoryPlatform.Application.Features.CreatorAuthoring.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ManualAuthoring.Services;

public partial class ManualAuthoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IUnitOfWork _uow;
    private readonly CreatorAuthoringOptions _options;
    private readonly IImportPreviewProtector _previews;
    private readonly IManualAuthoringPolicy _policy;

    public ManualAuthoringService(IUnitOfWork uow, CreatorAuthoringOptions options,
        IImportPreviewProtector previews, IManualAuthoringPolicy policy)
    {
        _uow = uow;
        _options = options;
        _previews = previews;
        _policy = policy;
    }

    public Task<CreatorDraftDto> CreateDraftAsync(int userId, CreateCreatorDraftRequest request, CancellationToken ct = default) =>
        CreateDraftWithContextAsync(userId, request, null, ct);

    public Task<CreatorDraftDto> CreateDraftWithContextAsync(int userId, CreateCreatorDraftRequest request, ManualDraftContext? context, CancellationToken ct = default) =>
        CommandAsync(userId, 0, "create-manual", request.OperationId, context is null ? request : new { request, context }, async _ =>
        {
            var language = Limited(request.Language, 10, "Ngôn ngữ");
            var ageBand = Limited(request.AgeBand, 20, "Nhóm tuổi");
            if (!_options.SupportedLanguages.Contains(language, StringComparer.Ordinal) ||
                !_options.SupportedAgeBands.Contains(ageBand, StringComparer.Ordinal))
                throw new BadRequestException("Ngôn ngữ hoặc nhóm tuổi không được hỗ trợ.");
            if (request.ExperienceMode is not ("reading" or "listening" or "combined"))
                throw new BadRequestException("Chế độ trải nghiệm không hợp lệ.");
            var story = new Story
            {
                Scope = _policy.Scope, Source = StorySource.Manual, ChildProfileId = null,
                AuthorUserId = userId, Title = Limited(request.Title, 200, "Tên truyện"),
                Description = Limited(request.Description, 1000, "Mô tả"), Genre = Limited(request.Genre, 50, "Thể loại"),
                Language = language, AgeBand = ageBand, ExperienceMode = request.ExperienceMode,
                OutputMode = StoryOutputMode.ReadingMediaOnly, Status = StoryStatus.Draft, IsPublished = false
            };
            await _policy.InitializeAsync(userId, story, context, ct);
            await _uow.Repository<Story>().AddAsync(story, ct);
            await _uow.SaveChangesAsync(ct);
            await AuditAsync(userId, "creator_manual_draft_created", nameof(Story), story.Id, null, new { story.Scope }, ct);
            return await ToDraftAsync(story, ct);
        }, ct);

    public async Task<PagedResult<CreatorDraftDto>> GetMineAsync(int userId, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        await RequireCreatorAsync(userId, ct);
        if (pageIndex < 1 || pageSize is < 1 or > 100) throw new BadRequestException("Phân trang không hợp lệ.");
        var rows = await _uow.Repository<Story>().GetPagedAsync(pageIndex, pageSize,
            _policy.VisibleStories(userId),
            q => q.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt), cancellationToken: ct);
        var drafts = new List<CreatorDraftDto>();
        foreach (var row in rows.Items)
        {
            await _policy.AuthorizeAsync(userId, row, ct);
            drafts.Add(await ToDraftAsync(row, ct));
        }
        return new PagedResult<CreatorDraftDto>(drafts, rows.TotalCount, pageIndex, pageSize);
    }

    public async Task<CreatorDraftDto> GetDraftAsync(int userId, int storyId, CancellationToken ct = default)
    {
        await RequireCreatorAsync(userId, ct);
        return await ToDraftAsync(await RequireStoryAsync(userId, storyId, ct), ct);
    }

    public Task<CreatorContentSavedDto> SaveContentAsync(int userId, int storyId, SaveCreatorContentRequest request, CancellationToken ct = default) =>
        CommandAsync(userId, storyId, "save-manual", request.OperationId, request, async story =>
        {
            await CheckSourceAsync(story!, request, ct);
            var assistance = await ValidateAssistanceAsync(userId, story!, request, ct);
            return await AddVersionAsync(userId, story!, new StoryVersion
            {
                Title = Limited(request.Title, 200, "Tên truyện"), Content = Content(request.Content, false),
                AuthoringOrigin = "Write", EditType = VersionEditType.HumanEdited,
                AssistanceProvenanceJson = assistance,
                SourceDeclaration = Limited(request.SourceDeclaration, 2000, "Nguồn"),
                RightsDeclaration = Limited(request.RightsDeclaration, 2000, "Quyền sử dụng")
            }, "creator_manual_content_saved", ct);
        }, ct);

    public async Task<IReadOnlyList<CreatorVersionDto>> GetVersionsAsync(int userId, int storyId, CancellationToken ct = default)
    {
        await RequireCreatorAsync(userId, ct);
        await RequireStoryAsync(userId, storyId, ct);
        var versions = await _uow.Repository<StoryVersion>().FindAsync(x => x.StoryId == storyId && !x.IsDeleted, cancellationToken: ct);
        return versions.OrderByDescending(x => x.VersionNo).Select(ToVersion).ToList();
    }

    public async Task<CreatorVersionDto> GetVersionAsync(int userId, int storyId, int versionId, CancellationToken ct = default)
    {
        await RequireCreatorAsync(userId, ct);
        await RequireStoryAsync(userId, storyId, ct);
        return ToVersion(await RequireVersionAsync(storyId, versionId, ct));
    }

    public Task<CreatorContentSavedDto> RestoreVersionAsync(int userId, int storyId, RestoreCreatorVersionRequest request, CancellationToken ct = default) =>
        CommandAsync(userId, storyId, "restore", request.OperationId, request, async story =>
        {
            await CheckSourceAsync(story!, request, ct);
            var source = await RequireVersionAsync(storyId, request.SourceVersionId, ct);
            return await AddVersionAsync(userId, story!, new StoryVersion
            {
                Title = source.Title, Content = source.Content, AuthoringOrigin = "Restore", EditType = VersionEditType.HumanEdited,
                RestoredFromVersionId = source.Id, SourceDeclaration = source.SourceDeclaration,
                AssistanceProvenanceJson = RestoreAssistance(source),
                RightsDeclaration = source.RightsDeclaration, ImportFileName = source.ImportFileName, ImportSourceHash = source.ImportSourceHash
            }, "creator_content_restored", ct);
        }, ct);

    private async Task<CreatorContentSavedDto> AddVersionAsync(int userId, Story story, StoryVersion version, string action, CancellationToken ct)
    {
        var current = await CurrentVersionAsync(story.Id, ct);
        version.StoryId = story.Id;
        version.BaseVersionId = current?.Id;
        version.AssistanceProvenanceJson ??= JsonSerializer.Serialize(
            new StoryPlatform.Application.Features.CreatorAssistance.DTOs.AssistanceProvenance(1, [], current?.Id, version.RestoredFromVersionId), JsonOptions);
        if (version.AssistanceProvenanceJson is not null)
        {
            var provenance = JsonSerializer.Deserialize<StoryPlatform.Application.Features.CreatorAssistance.DTOs.AssistanceProvenance>(version.AssistanceProvenanceJson, JsonOptions)!;
            version.AssistanceProvenanceJson = JsonSerializer.Serialize(provenance with { BaseVersionId = current?.Id }, JsonOptions);
        }
        var allVersions = await _uow.Repository<StoryVersion>().FindAsync(x => x.StoryId == story.Id, cancellationToken: ct);
        story.AuthoringLastVersionNo = Math.Max(story.AuthoringLastVersionNo, allVersions.Select(x => x.VersionNo).DefaultIfEmpty().Max());
        version.VersionNo = ++story.AuthoringLastVersionNo;
        version.AudienceRevision = story.AudienceRevision;
        version.EditorUserId = userId;
        version.IsCurrent = true;
        if (current is not null)
        {
            current.IsCurrent = false;
            _uow.Repository<StoryVersion>().Update(current);
            // Flush the old current flag before INSERT to satisfy the immediate unique index.
            await _uow.SaveChangesAsync(ct);
        }
        await _uow.Repository<StoryVersion>().AddAsync(version, ct);
        await _uow.SaveChangesAsync(ct);
        story.Title = version.Title;
        story.Content = version.Content;
        story.AuthoringRevisionToken = Guid.NewGuid().ToString("N");
        story.UpdatedAt = DateTime.UtcNow;
        _uow.Repository<Story>().Update(story);
        await AuditAsync(userId, action, nameof(Story), story.Id,
            new { selectedVersionId = current?.Id }, new { selectedVersionId = version.Id, origin = version.AuthoringOrigin }, ct);
        return new CreatorContentSavedDto(await ToDraftAsync(story, ct), ToVersion(version));
    }

    private async Task<T> CommandAsync<T>(int userId, int storyId, string kind, string operationId, object request,
        Func<Story?, Task<T>> action, CancellationToken ct)
    {
        if (userId <= 0 || storyId < 0 || (storyId == 0 && kind != "create-manual"))
            throw new BadRequestException("Mã định danh không hợp lệ.");
        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 100)
            throw new BadRequestException("OperationId cần từ 1 đến 100 ký tự.");
        var hash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { kind, storyId, request }, JsonOptions)));
        await _uow.BeginTransactionAsync(ct);
        try
        {
            await _uow.AcquireTransactionLockAsync(-userId, ct);
            if (storyId > 0) await _uow.AcquireTransactionLockAsync(storyId, ct);
            await RequireCreatorAsync(userId, ct);
            Story? story = null;
            string? receipt = null, priorHash = null;
            if (storyId == 0)
            {
                var created = await _uow.Repository<Story>().FirstOrDefaultAsync(x => x.AuthorUserId == userId && x.CreateOperationId == operationId, cancellationToken: ct);
                if (created is not null) await _policy.AuthorizeAsync(userId, created, ct);
                receipt = created?.CreateResponseJson;
                priorHash = created?.CreateRequestHash;
            }
            else
            {
                story = await RequireStoryAsync(userId, storyId, ct);
                if (_policy.Scope == StoryScope.Parent && await _uow.Repository<ParentStoryOperation>().ExistsAsync(
                    x => x.StoryId == storyId && x.ActorUserId == userId && x.OperationId == operationId, ct))
                    throw new ConflictException("OPERATION_ID_REUSED");
                var previous = await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId &&
                    x.OperationId == operationId && !x.IsDeleted, cancellationToken: ct);
                receipt = previous?.OperationResponseJson;
                priorHash = previous?.RequestHash;
            }
            if (priorHash is not null)
            {
                if (priorHash != hash) throw new ConflictException("OperationId đã được dùng cho yêu cầu khác.");
                var replay = JsonSerializer.Deserialize<T>(receipt ?? throw new InvalidOperationException("Missing operation receipt"), JsonOptions)
                    ?? throw new InvalidOperationException("Invalid operation receipt");
                await _uow.CommitTransactionAsync(ct);
                return replay;
            }
            if (story is not null) await _policy.EnsureWritableAsync(story, ct);
            var result = await action(story);
            var response = JsonSerializer.Serialize(result, JsonOptions);
            if (result is CreatorDraftDto draft)
            {
                var created = await _uow.Repository<Story>().GetByIdAsync(draft.StoryId, ct) ?? throw new InvalidOperationException();
                created.CreateOperationId = operationId;
                created.CreateRequestHash = hash;
                created.CreateResponseJson = response;
                // Already saved, so its generated ID is now permanent.
                _uow.Repository<Story>().Update(created);
            }
            else if (result is CreatorContentSavedDto saved)
            {
                var version = await _uow.Repository<StoryVersion>().GetByIdAsync(saved.Version.VersionId, ct) ?? throw new InvalidOperationException();
                version.OperationId = operationId;
                version.RequestHash = hash;
                version.OperationResponseJson = response;
                _uow.Repository<StoryVersion>().Update(version);
            }
            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
            return result;
        }
        catch { await _uow.RollbackTransactionAsync(CancellationToken.None); throw; }
    }

    private async Task RequireCreatorAsync(int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (userId <= 0 || !await _uow.Repository<UserAccount>().ExistsAsync(x => x.Id == userId && !x.IsDeleted &&
            x.Status == AccountStatus.Active &&
            (!x.LockedUntil.HasValue || x.LockedUntil <= now), ct))
            throw new ForbiddenException("Tài khoản không hoạt động hoặc đang bị khóa.");
    }

    private async Task<Story> RequireStoryAsync(int userId, int storyId, CancellationToken ct)
    {
        var story = await _uow.Repository<Story>().GetByIdAsync(storyId, ct);
        if (story is null || story.IsDeleted || story.Scope != _policy.Scope || story.AuthorUserId != userId)
            throw new NotFoundException("Story", storyId);
        await _policy.AuthorizeAsync(userId, story, ct);
        return story;
    }
    private async Task<StoryVersion> RequireVersionAsync(int storyId, int versionId, CancellationToken ct) =>
        await _uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.Id == versionId && x.StoryId == storyId && !x.IsDeleted, cancellationToken: ct)
        ?? throw new NotFoundException("StoryVersion", versionId);
    private Task<StoryVersion?> CurrentVersionAsync(int storyId, CancellationToken ct) =>
        _uow.Repository<StoryVersion>().FirstOrDefaultAsync(x => x.StoryId == storyId && x.IsCurrent && !x.IsDeleted, cancellationToken: ct);
    private async Task CheckSourceAsync(Story story, CreatorVersionCommand request, CancellationToken ct)
    {
        CheckRevision(story, request.RevisionToken);
        if (request.BaseVersionId != (await CurrentVersionAsync(story.Id, ct))?.Id)
            throw new ConflictException("Phiên bản nguồn đã thay đổi.");
    }
    private static void CheckRevision(Story story, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token != story.AuthoringRevisionToken)
            throw new ConflictException("Bản nháp đã thay đổi.");
    }
    private string Content(string? content, bool requireNonEmpty)
    {
        content ??= string.Empty;
        if (content.Length > _options.MaximumContentCharacters)
            throw new BadRequestException("Nội dung vượt giới hạn cho phép.");
        if (requireNonEmpty && string.IsNullOrWhiteSpace(content)) throw new BadRequestException("Nội dung nhập không được để trống.");
        return content;
    }

    private static string Limited(string? value, int length, string label)
    {
        value ??= string.Empty;
        if (value.Length > length) throw new BadRequestException($"{label} vượt {length} ký tự.");
        return value.Trim();
    }

    private Task<AuditLog> AuditAsync(int userId, string action, string type, int entityId, object? before, object after, CancellationToken ct) =>
        _uow.Repository<AuditLog>().AddAsync(new AuditLog
        {
            ActorUserId = userId, Action = _policy.Scope == StoryScope.Parent ? action.Replace("creator_", "parent_", StringComparison.Ordinal) : action, EntityType = type, EntityId = entityId,
            BeforeState = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterState = JsonSerializer.Serialize(after, JsonOptions), OccurredAt = DateTime.UtcNow
        }, ct);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private async Task<CreatorDraftDto> ToDraftAsync(Story x, CancellationToken ct)
    {
        var lastVersionNo = x.AuthoringLastVersionNo;
        if (x.Scope == StoryScope.Creator && x.Source == StorySource.Ai)
            lastVersionNo = (await _uow.Repository<StoryVersion>().FindAsync(v => v.StoryId == x.Id && !v.IsDeleted,
                cancellationToken: ct)).Select(v => v.VersionNo).DefaultIfEmpty().Max();
        return new(x.Id, x.AuthorUserId, x.Title ?? string.Empty, x.Description ?? string.Empty,
            x.Language, x.AgeBand, x.Genre ?? string.Empty, x.ExperienceMode, x.AudienceRevision,
            x.AuthoringRevisionToken, (await CurrentVersionAsync(x.Id, ct))?.Id, lastVersionNo,
            x.CreatedAt, x.UpdatedAt, x.Source.ToString(), x.Status.ToString());
    }
    private static CreatorVersionDto ToVersion(StoryVersion x) => new(x.Id, x.StoryId, x.VersionNo,
        x.BaseVersionId, x.RestoredFromVersionId, x.EditorUserId ?? 0, x.Title, x.Content ?? string.Empty,
        x.AuthoringOrigin ?? "Write", x.SourceDeclaration, x.RightsDeclaration, x.ImportFileName, x.ImportSourceHash,
        x.AudienceRevision, x.CreatedAt, x.AssistanceProvenanceJson is null ? null :
            JsonSerializer.Deserialize<StoryPlatform.Application.Features.CreatorAssistance.DTOs.AssistanceProvenance>(x.AssistanceProvenanceJson, JsonOptions));
}
