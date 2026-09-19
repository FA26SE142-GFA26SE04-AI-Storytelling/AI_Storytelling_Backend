using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StoryPlatform.Application.Abstractions.AI;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Supervision.Interfaces;
using StoryPlatform.Application.Features.ExistingStories.DTOs;
using StoryPlatform.Application.Features.ExistingStories.Helpers;
using StoryPlatform.Application.Features.ExistingStories.Interfaces;
using StoryPlatform.Contracts.AI.Requests;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ExistingStories.Services;

/// <summary>
/// Triển khai IExistingStoryService.
///
/// Các nguyên tắc:
///   - StoryVersion cũ KHÔNG BAO GIỜ bị overwrite.
///   - Mọi mutation đều lock theo storyId (AcquireTransactionLockAsync).
///   - Phase 2 bị skip hoàn toàn (SKIP OUTLINE).
///   - Sau khi có stable version -> gọi IStableVersionArtifactHandoffService
///     (KHÔNG gọi trực tiếp ContentGenerationService).
/// </summary>
public sealed class ExistingStoryService : IExistingStoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISupervisionAccessGuard _accessGuard;
    private readonly IAIStoryGenerationClient _aiClient;
    private readonly IStableVersionArtifactHandoffService _handoff;
    private readonly IAuditLogWriter _auditLog;

    public ExistingStoryService(
        IUnitOfWork unitOfWork,
        ISupervisionAccessGuard accessGuard,
        IAIStoryGenerationClient aiClient,
        IStableVersionArtifactHandoffService handoff,
        IAuditLogWriter auditLog)
    {
        _unitOfWork = unitOfWork;
        _accessGuard = accessGuard;
        _aiClient = aiClient;
        _handoff = handoff;
        _auditLog = auditLog;
    }

    #region Import

    public async Task<ImportStoryResponseDto> ImportAsync(
        int userId, ImportStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        ValidateImportRequest(request);

        var normalized = StoryContentNormalizer.Normalize(request.Content);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new BadRequestException("Nội dung truyện rỗng sau khi chuẩn hoá.");
        var warnings = new List<string>();
        if (normalized!.Length != request.Content?.Length)
            warnings.Add("Đã chuẩn hoá khoảng trắng/newline trong nội dung.");

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(request.ChildProfileId, cancellationToken)
                    ?? throw new NotFoundException("ChildProfile", request.ChildProfileId);
        if (child.Status != ChildProfileStatus.Active)
            throw new BadRequestException("ChildProfile chưa ở trạng thái Active.");
        await _accessGuard.EnsurePermissionAsync(child.Id, userId, Permission.GenerateStory, cancellationToken);

        // Pre-import safety scan: nếu nội dung quá dài so với policy -> reject sớm.
        var wordCount = StoryContentNormalizer.CountWords(normalized);
        if (wordCount > 20000)
            throw new BadRequestException("Nội dung vượt quá giới hạn cho phép (20000 từ).");

        // Idempotency: nếu cùng user + child + content hash đã import -> trả về bản gốc.
        var idempotencyKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.IdempotencyKey!
            : ExistingStoryIdempotencyKeys.ForImport(userId, child.Id, normalized);

        var existingRequest = await _unitOfWork.Repository<StoryGenerationRequest>().FirstOrDefaultAsync(
            req => req.SubmittedByUserId == userId && req.IdempotencyKey == idempotencyKey,
            cancellationToken: cancellationToken);
        if (existingRequest is not null)
        {
            var existingStory = await _unitOfWork.Repository<Story>().GetByIdAsync(existingRequest.StoryId, cancellationToken);
            var existingVersion = existingStory is null ? null : (await _unitOfWork.Repository<StoryVersion>().FindAsync(
                v => v.StoryId == existingStory.Id && v.IsCurrent, cancellationToken: cancellationToken))
                .OrderByDescending(v => v.VersionNo).FirstOrDefault();
            if (existingStory is not null && existingVersion is not null)
            {
                return new ImportStoryResponseDto
                {
                    StoryId = existingStory.Id,
                    StoryVersionId = existingVersion.Id,
                    StoryStatus = existingStory.Status.ToString(),
                    EditType = existingVersion.EditType.ToString(),
                    ContentLength = existingVersion.Content?.Length
                };
            }
        }

        Story story = null!;
        StoryVersion v1 = null!;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            story = new Story
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Câu chuyện" : request.Title.Trim(),
                Description = null,
                Content = normalized,
                Genre = null,
                MoralLesson = null,
                AgeBand = MapAgeBand(child.AgeBand),
                Language = string.IsNullOrWhiteSpace(request.Language) ? "vi" : request.Language,
                VocabularyLevel = "level_2",
                Source = StorySource.Manual,
                Status = StoryStatus.Draft,
                IsPublished = false,
                AuthorUserId = userId,
                ChildProfileId = child.Id
            };
            await _unitOfWork.Repository<Story>().AddAsync(story, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            v1 = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = 1,
                EditType = VersionEditType.Initial,
                EditorUserId = userId,
                Title = story.Title,
                Content = normalized,
                Lesson = string.Empty,
                IsCurrent = true,
                OutlineApprovedAt = null
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(v1, cancellationToken);

            var genRequest = new StoryGenerationRequest
            {
                StoryId = story.Id,
                SubmittedByUserId = userId,
                IdempotencyKey = idempotencyKey,
                InputFingerprint = ShortHash(normalized),
                ContextFingerprint = ShortHash($"{child.Id}:{story.AgeBand}:{story.Language}"),
                ContextSnapshotJson = JsonSerializer.Serialize(new
                {
                    childProfileId = child.Id,
                    ageBand = story.AgeBand,
                    language = story.Language,
                    source = "existing_story_import"
                }, JsonOptions),
                AcceptedInputJson = JsonSerializer.Serialize(new { title = story.Title, length = normalized.Length }, JsonOptions),
                Status = GenerationInputStatus.InputAccepted,
                AttemptCount = 0,
                MaxAttempts = 1,
                GuardrailDecision = "Allow",
                GuardrailCheckVersion = "existing-story-import/v1",
                GuardrailCheckedAt = DateTime.UtcNow,
                CanRetry = false
            };
            await _unitOfWork.Repository<StoryGenerationRequest>().AddAsync(genRequest, cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.Import", nameof(Story), story.Id,
            beforeState: null, afterState: new { storyId = story.Id, versionId = v1.Id, words = wordCount },
            cancellationToken: cancellationToken);

        return new ImportStoryResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = v1.Id,
            StoryStatus = story.Status.ToString(),
            EditType = v1.EditType.ToString(),
            ContentLength = normalized.Length,
            Warnings = warnings
        };
    }

    private static void ValidateImportRequest(ImportStoryRequestDto request)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (request.ChildProfileId <= 0)
            throw new BadRequestException("ChildProfileId không hợp lệ.");
        var method = request.InputMethod?.Trim().ToLowerInvariant();
        if (method is not ("paste" or "txt"))
            throw new BadRequestException("InputMethod không được hỗ trợ (chỉ 'paste' hoặc 'txt').");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new BadRequestException("Nội dung truyện rỗng.");
        if (request.Content!.Length > 200_000)
            throw new BadRequestException("Nội dung vượt quá giới hạn (200000 ký tự).");
    }

    private static string MapAgeBand(AgeBand band) => band switch
    {
        AgeBand.Age_6_8 => "6-8",
        AgeBand.Age_9_12 => "9-12",
        _ => "6-8"
    };

    private static string ShortHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    #endregion

    #region Adapt

    public async Task<VersionMutationResponseDto> AdaptAsync(
        int userId, AdaptExistingStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (request.BaseStoryVersionId <= 0)
            throw new BadRequestException("BaseStoryVersionId không hợp lệ.");

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        StoryVersion? created = null;
        int? newVersionId = null;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);

            var baseVersion = await _unitOfWork.Repository<StoryVersion>()
                .GetByIdAsync(request.BaseStoryVersionId, cancellationToken)
                ?? throw new NotFoundException("StoryVersion", request.BaseStoryVersionId);
            if (baseVersion.StoryId != story.Id || !baseVersion.IsCurrent)
                throw new ConflictException("STALE_BASE_VERSION");
            if (string.IsNullOrWhiteSpace(baseVersion.Content))
                throw new BadRequestException("Base version chưa có nội dung.");

            // Gọi AI để refine.
            var refineRequest = new RefineStoryContentRequest
            {
                RequestId = $"adapt-{story.Id}-v{baseVersion.VersionNo}-{Guid.NewGuid():N}",
                Story = new StoryPlatform.Contracts.AI.Models.StoryContentDto
                {
                    Title = baseVersion.Title,
                    Lesson = baseVersion.Lesson ?? string.Empty,
                    StorySections = [new StoryPlatform.Contracts.AI.Models.StorySectionDto(1, string.Empty, baseVersion.Content!)]
                },
                Language = story.Language ?? "vi",
                AgeBand = story.AgeBand,
                Reasons = string.IsNullOrWhiteSpace(request.Guideline)
                    ? ["Điều chỉnh cho phù hợp với độ tuổi và trình độ đọc."]
                    : [request.Guideline!]
            };
            var refined = await _aiClient.RefineStoryContentAsync(refineRequest, cancellationToken);
            var newContent = ExtractContent(refined.Story);

            var versions = await _unitOfWork.Repository<StoryVersion>().FindAsync(
                v => v.StoryId == story.Id, cancellationToken: cancellationToken);
            var nextVersionNo = versions.Select(v => v.VersionNo).DefaultIfEmpty().Max() + 1;

            baseVersion.IsCurrent = false;
            _unitOfWork.Repository<StoryVersion>().Update(baseVersion);

            created = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = nextVersionNo,
                EditType = VersionEditType.AiRefined,
                EditorUserId = userId,
                Title = refined.Story.Title.Trim(),
                Content = newContent,
                Lesson = (refined.Story.Lesson ?? baseVersion.Lesson ?? string.Empty).Trim(),
                IsCurrent = true
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(created, cancellationToken);

            // Cập nhật header Story cho phù hợp với current version (không phải overwrite content canonical).
            story.Title = created.Title;
            story.Content = created.Content;
            story.MoralLesson = created.Lesson;
            _unitOfWork.Repository<Story>().Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            newVersionId = created.Id;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.Adapt", nameof(StoryVersion), newVersionId!.Value,
            beforeState: new { baseVersionId = request.BaseStoryVersionId },
            afterState: new { newVersionId = newVersionId, editType = created!.EditType.ToString() },
            cancellationToken: cancellationToken);

        // Không tự gọi handoff: caller (controller) sẽ gọi evaluate trước rồi mới handoff.
        return new VersionMutationResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = newVersionId.Value,
            StoryStatus = story.Status.ToString(),
            EditType = created!.EditType.ToString()
        };
    }

    private static string ExtractContent(StoryPlatform.Contracts.AI.Models.StoryContentDto story)
    {
        if (story.StorySections.Count == 0) return string.Empty;
        return string.Join("\n\n", story.StorySections.OrderBy(s => s.Order).Select(s =>
            string.IsNullOrWhiteSpace(s.Heading) ? s.Content.Trim() : $"{s.Heading.Trim()}\n{s.Content.Trim()}"));
    }

    #endregion

    #region Manual Edit

    public async Task<VersionMutationResponseDto> UpdateContentAsync(
        int userId, ManualEditRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (request.BaseStoryVersionId <= 0)
            throw new BadRequestException("BaseStoryVersionId không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content))
            throw new BadRequestException("Title và Content là bắt buộc.");
        var normalized = StoryContentNormalizer.Normalize(request.Content)!;

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        StoryVersion? created = null;
        int newVersionId = 0;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);

            var baseVersion = await _unitOfWork.Repository<StoryVersion>()
                .GetByIdAsync(request.BaseStoryVersionId, cancellationToken)
                ?? throw new NotFoundException("StoryVersion", request.BaseStoryVersionId);
            if (baseVersion.StoryId != story.Id || !baseVersion.IsCurrent)
                throw new ConflictException("STALE_BASE_VERSION");

            var versions = await _unitOfWork.Repository<StoryVersion>().FindAsync(
                v => v.StoryId == story.Id, cancellationToken: cancellationToken);
            var nextVersionNo = versions.Select(v => v.VersionNo).DefaultIfEmpty().Max() + 1;

            baseVersion.IsCurrent = false;
            _unitOfWork.Repository<StoryVersion>().Update(baseVersion);

            created = new StoryVersion
            {
                StoryId = story.Id,
                VersionNo = nextVersionNo,
                EditType = VersionEditType.HumanEdited,
                EditorUserId = userId,
                Title = request.Title.Trim(),
                Content = normalized,
                Lesson = (request.Lesson ?? string.Empty).Trim(),
                IsCurrent = true
            };
            await _unitOfWork.Repository<StoryVersion>().AddAsync(created, cancellationToken);

            story.Title = created.Title;
            story.Content = created.Content;
            story.MoralLesson = created.Lesson;
            _unitOfWork.Repository<Story>().Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            newVersionId = created.Id;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.ManualEdit", nameof(StoryVersion), newVersionId,
            beforeState: new { baseVersionId = request.BaseStoryVersionId },
            afterState: new { newVersionId, editType = created!.EditType.ToString() },
            cancellationToken: cancellationToken);

        return new VersionMutationResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = newVersionId,
            StoryStatus = story.Status.ToString(),
            EditType = created!.EditType.ToString()
        };
    }

    #endregion

    #region Keep Original

    public async Task<VersionMutationResponseDto> KeepOriginalAsync(
        int userId, KeepOriginalRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");
        if (string.IsNullOrWhiteSpace(request.OverrideReason))
            throw new BadRequestException("OverrideReason là bắt buộc.");

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        var version = await _unitOfWork.Repository<StoryVersion>().GetByIdAsync(request.StoryVersionId, cancellationToken)
                      ?? throw new NotFoundException("StoryVersion", request.StoryVersionId);
        if (version.StoryId != story.Id)
            throw new BadRequestException("StoryVersion không thuộc Story.");
        if (!version.IsCurrent)
            throw new BadRequestException("StoryVersion không phải current.");

        // Audit log đầy đủ cho keep-original.
        await _auditLog.LogAsync(
            userId, "ExistingStory.KeepOriginal", nameof(Story), story.Id,
            beforeState: new { versionId = version.Id },
            afterState: new { overrideReason = request.OverrideReason, keptVersionId = version.Id },
            cancellationToken: cancellationToken);

        return new VersionMutationResponseDto
        {
            StoryId = story.Id,
            StoryVersionId = version.Id,
            StoryStatus = story.Status.ToString(),
            EditType = version.EditType.ToString(),
            Decision = "KeepOriginal"
        };
    }

    #endregion

    #region Archive

    public async Task<bool> ArchiveAsync(
        int userId, ArchiveExistingStoryRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new BadRequestException("Thiếu body request.");

        var story = await _unitOfWork.Repository<Story>().GetByIdAsync(request.StoryId, cancellationToken)
                    ?? throw new NotFoundException("Story", request.StoryId);
        if (story.Source != StorySource.Manual)
            throw new BadRequestException("Story không thuộc nhánh Existing.");
        await _accessGuard.EnsurePermissionAsync(story.ChildProfileId, userId, Permission.GenerateStory, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(story.Id, cancellationToken);

            story.Status = StoryStatus.Archived;
            story.ArchivedReason = ArchivedReason.SafetyConcern;
            _unitOfWork.Repository<Story>().Update(story);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        await _auditLog.LogAsync(
            userId, "ExistingStory.Archive", nameof(Story), story.Id,
            beforeState: new { status = StoryStatus.Draft.ToString() },
            afterState: new { status = story.Status.ToString(), reason = request.Reason },
            cancellationToken: cancellationToken);

        return true;
    }

    #endregion
}
