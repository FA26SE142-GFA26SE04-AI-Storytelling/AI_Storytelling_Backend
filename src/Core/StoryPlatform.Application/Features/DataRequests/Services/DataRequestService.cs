using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.DataRequests.DTOs;
using StoryPlatform.Application.Features.DataRequests.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.DataRequests.Services;

public class DataRequestService : IDataRequestService
{
    private const string AnonymizedNicknamePlaceholder = "[Đã ẩn danh]";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IWorkbookExportBuilder _exportBuilder;
    private readonly IArchiveExportBuilder _archiveExportBuilder;

    public DataRequestService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        IWorkbookExportBuilder exportBuilder,
        IArchiveExportBuilder archiveExportBuilder)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _exportBuilder = exportBuilder;
        _archiveExportBuilder = archiveExportBuilder;
    }

    public async Task<DataRequestDto> CreateAsync(
        int ownerUserId, CreateDataRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<DataRequestType>(request.RequestType, ignoreCase: true, out var requestType))
        {
            throw new BadRequestException("RequestType phải là 'export' hoặc 'delete'.");
        }

        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(request.ChildProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", request.ChildProfileId);

        if (child.OwnerUserId != ownerUserId)
        {
            throw new ForbiddenException("Chỉ Owner của Child Profile mới được tạo Data Request.");
        }

        var duplicatePending = await _unitOfWork.Repository<DataRequest>().ExistsAsync(
            item => item.ChildProfileId == request.ChildProfileId
                    && item.RequestType == requestType
                    && item.Status == DataRequestStatus.Pending,
            cancellationToken);
        if (duplicatePending)
        {
            throw new ConflictException("Đã có một Data Request cùng loại đang chờ xử lý cho Child Profile này.");
        }

        var dataRequest = new DataRequest
        {
            RequestedByUserId = ownerUserId,
            ChildProfileId = request.ChildProfileId,
            RequestType = requestType,
            Status = DataRequestStatus.Pending
        };

        await _unitOfWork.Repository<DataRequest>().AddAsync(dataRequest, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(dataRequest);
    }

    public async Task<List<DataRequestDto>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _unitOfWork.Repository<DataRequest>().FindAsync(
            item => item.Status == DataRequestStatus.Pending, cancellationToken: cancellationToken);
        return pending.OrderBy(item => item.CreatedAt).Select(MapToDto).ToList();
    }

    private static readonly HashSet<string> SupportedExportFormats = new(StringComparer.OrdinalIgnoreCase) { "xlsx", "csv" };

    public async Task<ExportedFileDto> ResolveExportAsync(
        int adminUserId, int dataRequestId, string format = "xlsx", CancellationToken cancellationToken = default)
    {
        if (!SupportedExportFormats.Contains(format))
        {
            throw new BadRequestException("Format phải là 'xlsx' hoặc 'csv'.");
        }

        var request = await _unitOfWork.Repository<DataRequest>().GetByIdAsync(dataRequestId, cancellationToken)
                      ?? throw new NotFoundException("Data Request", dataRequestId);

        if (request.RequestType != DataRequestType.Export)
        {
            throw new BadRequestException("Data Request này không phải loại Export.");
        }

        if (request.Status != DataRequestStatus.Pending)
        {
            throw new ConflictException("Data Request đã được xử lý trước đó.");
        }

        var childProfileId = request.ChildProfileId
                              ?? throw new BadRequestException("Data Request chưa gắn Child Profile.");
        var sheets = await GatherExportSheetsAsync(childProfileId, cancellationToken);
        var isCsv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);
        var content = isCsv ? _archiveExportBuilder.BuildCsvArchive(sheets) : _exportBuilder.BuildWorkbook(sheets);
        var extension = isCsv ? "zip" : "xlsx";

        var beforeState = new { status = request.Status.ToString() };
        request.Status = DataRequestStatus.Resolved;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = adminUserId;
        _unitOfWork.Repository<DataRequest>().Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "DataRequestExported", nameof(DataRequest), request.Id,
            beforeState, new { status = request.Status.ToString() }, cancellationToken);

        return new ExportedFileDto
        {
            FileName = $"child-{childProfileId}-data-export-{DateTime.UtcNow:yyyyMMdd}.{extension}",
            Content = content
        };
    }

    public async Task<DataRequestDto> ResolveDeleteAsync(
        int adminUserId, int dataRequestId, CancellationToken cancellationToken = default)
    {
        var request = await _unitOfWork.Repository<DataRequest>().GetByIdAsync(dataRequestId, cancellationToken)
                      ?? throw new NotFoundException("Data Request", dataRequestId);

        if (request.RequestType != DataRequestType.Delete)
        {
            throw new BadRequestException("Data Request này không phải loại Delete.");
        }

        if (request.Status != DataRequestStatus.Pending)
        {
            throw new ConflictException("Data Request đã được xử lý trước đó.");
        }

        if (request.DeletionMethod == DataRequestDeletionMethod.HardDelete)
        {
            throw new BadRequestException(
                "Hard Delete cần xác nhận pháp lý bằng văn bản và xử lý thủ công, chưa hỗ trợ tự động.");
        }

        var childProfileId = request.ChildProfileId
                              ?? throw new BadRequestException("Data Request chưa gắn Child Profile.");
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);

        await RevokeSupervisionRelationshipsAsync(childProfileId, adminUserId, cancellationToken);
        await CancelAssignmentsAsync(childProfileId, adminUserId, cancellationToken);
        await RevokeSharedStoriesAsync(childProfileId, cancellationToken);

        child.Nickname = AnonymizedNicknamePlaceholder;
        child.DateOfBirth = null;
        child.Status = ChildProfileStatus.Archived;
        _unitOfWork.Repository<ChildProfile>().Update(child);

        var beforeState = new { status = request.Status.ToString() };
        request.Status = DataRequestStatus.Resolved;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = adminUserId;
        _unitOfWork.Repository<DataRequest>().Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "DataRequestDeleted", nameof(DataRequest), request.Id,
            beforeState, new { status = request.Status.ToString() }, cancellationToken);

        return MapToDto(request);
    }

    private async Task RevokeSupervisionRelationshipsAsync(
        int childProfileId, int adminUserId, CancellationToken cancellationToken)
    {
        var relationships = await _unitOfWork.Repository<SupervisionRelationship>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        foreach (var relationship in relationships.Where(item => item.RevokedAt == null))
        {
            relationship.RevokedAt = DateTime.UtcNow;
            relationship.RevokedByUserId = adminUserId;
            _unitOfWork.Repository<SupervisionRelationship>().Update(relationship);
        }
    }

    private async Task CancelAssignmentsAsync(int childProfileId, int adminUserId, CancellationToken cancellationToken)
    {
        var recipients = await _unitOfWork.Repository<AssignmentRecipient>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        foreach (var recipient in recipients.Where(item => item.Status != AssignmentStatus.Cancelled))
        {
            recipient.Status = AssignmentStatus.Cancelled;
            recipient.CancelledAt = DateTime.UtcNow;
            recipient.CancelledByUserId = adminUserId;
            _unitOfWork.Repository<AssignmentRecipient>().Update(recipient);
        }

        var directAssignments = await _unitOfWork.Repository<Assignment>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        foreach (var assignment in directAssignments.Where(item => item.Status != AssignmentStatus.Cancelled))
        {
            assignment.Status = AssignmentStatus.Cancelled;
            _unitOfWork.Repository<Assignment>().Update(assignment);
        }
    }

    private async Task RevokeSharedStoriesAsync(int childProfileId, CancellationToken cancellationToken)
    {
        var stories = await _unitOfWork.Repository<Story>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var storyIds = stories.Select(item => item.Id).ToList();
        if (storyIds.Count == 0)
        {
            return;
        }

        var sharedStories = await _unitOfWork.Repository<SharedStory>().FindAsync(
            item => storyIds.Contains(item.StoryId), cancellationToken: cancellationToken);
        var sharedStoryIds = sharedStories.Select(item => item.Id).ToList();
        if (sharedStoryIds.Count == 0)
        {
            return;
        }

        var sharedRecipients = await _unitOfWork.Repository<SharedStoryRecipient>().FindAsync(
            item => sharedStoryIds.Contains(item.SharedStoryId), cancellationToken: cancellationToken);
        foreach (var recipient in sharedRecipients.Where(item => item.Status != RecipientStatus.Revoked))
        {
            recipient.Status = RecipientStatus.Revoked;
            _unitOfWork.Repository<SharedStoryRecipient>().Update(recipient);
        }
    }

    private async Task<IReadOnlyList<ExportSheet>> GatherExportSheetsAsync(
        int childProfileId, CancellationToken cancellationToken)
    {
        var child = await _unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken)
                    ?? throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        var learningProfiles = await _unitOfWork.Repository<LearningProfile>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var safetyPolicies = await _unitOfWork.Repository<SafetyPolicy>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var readingSessions = await _unitOfWork.Repository<ReadingSession>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var readingSessionIds = readingSessions.Select(item => item.Id).ToList();
        var telemetryLogs = await _unitOfWork.Repository<TelemetryLog>().FindAsync(
            item => readingSessionIds.Contains(item.ReadingSessionId), cancellationToken: cancellationToken);
        var quizAttempts = await _unitOfWork.Repository<QuizAttempt>().FindAsync(
            item => readingSessionIds.Contains(item.ReadingSessionId), cancellationToken: cancellationToken);
        var achievements = await _unitOfWork.Repository<Achievement>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var badges = await _unitOfWork.Repository<Badge>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var vocabulary = await _unitOfWork.Repository<VocabularyNotebookEntry>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var assignmentRecipients = await _unitOfWork.Repository<AssignmentRecipient>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var assignmentRecipientIds = assignmentRecipients.Select(item => item.Id).ToList();
        var o2oAssessments = await _unitOfWork.Repository<O2OAssessment>().FindAsync(
            item => assignmentRecipientIds.Contains(item.AssignmentRecipientId), cancellationToken: cancellationToken);
        var interventionCases = await _unitOfWork.Repository<InterventionCase>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);
        var recommendations = await _unitOfWork.Repository<Recommendation>().FindAsync(
            item => item.ChildProfileId == childProfileId, cancellationToken: cancellationToken);

        return new List<ExportSheet>
        {
            ToSheet("child_profiles", new[] { child }),
            ToSheet("learning_profiles", learningProfiles),
            ToSheet("safety_policies", safetyPolicies),
            ToSheet("reading_sessions", readingSessions),
            ToSheet("telemetry_logs", telemetryLogs),
            ToSheet("quiz_attempts", quizAttempts),
            ToSheet("achievements", achievements),
            ToSheet("badges", badges),
            ToSheet("vocabulary_notebook_entries", vocabulary),
            ToSheet("o2o_assessments", o2oAssessments),
            ToSheet("intervention_cases", interventionCases),
            ToSheet("recommendations", recommendations)
        };
    }

    private static readonly HashSet<Type> ScalarTypes =
    [
        typeof(string), typeof(int), typeof(int?), typeof(bool), typeof(bool?),
        typeof(DateTime), typeof(DateTime?), typeof(DateOnly), typeof(DateOnly?),
        typeof(decimal), typeof(decimal?), typeof(double), typeof(double?)
    ];

    private static ExportSheet ToSheet<T>(string name, IEnumerable<T> items)
    {
        var properties = typeof(T).GetProperties()
            .Where(property => property.CanRead
                                && (ScalarTypes.Contains(property.PropertyType) || IsEnumOrNullableEnum(property.PropertyType)))
            .OrderBy(property => property.Name == "Id" ? 0 : 1)
            .ToList();
        var columns = properties.Select(property => property.Name).ToList();
        var rows = items.Select(item => (IReadOnlyList<string?>)properties
            .Select(property => FormatValue(property.GetValue(item)))
            .ToList()).ToList();
        return new ExportSheet(name, columns, rows);
    }

    private static bool IsEnumOrNullableEnum(Type type) =>
        type.IsEnum || (Nullable.GetUnderlyingType(type)?.IsEnum ?? false);

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        DateTime dateTime => dateTime.ToString("O"),
        DateOnly dateOnly => dateOnly.ToString("O"),
        _ => value.ToString()
    };

    private static DataRequestDto MapToDto(DataRequest request) => new()
    {
        Id = request.Id,
        ChildProfileId = request.ChildProfileId ?? 0,
        RequestType = request.RequestType.ToString().ToLowerInvariant(),
        Status = request.Status.ToString(),
        CreatedAt = request.CreatedAt,
        ResolvedAt = request.ResolvedAt,
        ResolvedByUserId = request.ResolvedByUserId
    };
}
