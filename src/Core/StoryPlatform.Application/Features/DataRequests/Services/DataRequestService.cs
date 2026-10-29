using System.Text.Json;
using StoryPlatform.Application.Abstractions.Communication;
using StoryPlatform.Application.Abstractions.Export;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Abstractions.Security;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.Interfaces;
using StoryPlatform.Application.Features.DataRequests.DTOs;
using StoryPlatform.Application.Features.DataRequests.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.DataRequests.Services;

public class DataRequestService : IDataRequestService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IWorkbookExportBuilder _exportBuilder;
    private readonly IArchiveExportBuilder _archiveExportBuilder;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IChildProfileEraser _childProfileEraser;
    private readonly INotificationService _notificationService;
    private readonly IEmailSender _emailSender;

    public DataRequestService(
        IUnitOfWork unitOfWork,
        IAuditLogWriter auditLogWriter,
        IWorkbookExportBuilder exportBuilder,
        IArchiveExportBuilder archiveExportBuilder,
        IPasswordHasher passwordHasher,
        IChildProfileEraser childProfileEraser,
        INotificationService notificationService,
        IEmailSender emailSender)
    {
        _unitOfWork = unitOfWork;
        _auditLogWriter = auditLogWriter;
        _exportBuilder = exportBuilder;
        _archiveExportBuilder = archiveExportBuilder;
        _passwordHasher = passwordHasher;
        _childProfileEraser = childProfileEraser;
        _notificationService = notificationService;
        _emailSender = emailSender;
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

        if (requestType == DataRequestType.Delete)
        {
            await EnsureDeleteConfirmedAsync(
                ownerUserId, child, request, cancellationToken);
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

    public async Task<List<DataRequestDto>> ListMineAsync(
        int ownerUserId, int? childProfileId,
        CancellationToken cancellationToken = default)
    {
        var requests = await _unitOfWork.Repository<DataRequest>().FindAsync(
            item => item.RequestedByUserId == ownerUserId
                    && (childProfileId == null || item.ChildProfileId == childProfileId),
            cancellationToken: cancellationToken);
        return requests
            .OrderByDescending(item => item.CreatedAt)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<DataRequestDto> CancelAsync(
        int ownerUserId, int dataRequestId,
        CancellationToken cancellationToken = default)
    {
        var requestRepo = _unitOfWork.Repository<DataRequest>();
        var request = await requestRepo.GetByIdAsync(dataRequestId, cancellationToken)
                      ?? throw new NotFoundException("Data Request", dataRequestId);
        if (request.RequestedByUserId != ownerUserId)
        {
            throw new ForbiddenException(
                "Bạn chỉ hủy được yêu cầu do chính mình gửi.");
        }

        var child = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(request.ChildProfileId ?? 0, cancellationToken);
        if (child == null || child.OwnerUserId != ownerUserId)
        {
            throw new ForbiddenException(
                "Chỉ Owner của hồ sơ trẻ mới hủy được Data Request.");
        }

        if (request.Status != DataRequestStatus.Pending)
        {
            throw new ConflictException(
                "Chỉ hủy được Data Request đang chờ xử lý.");
        }

        request.Status = DataRequestStatus.Cancelled;
        request.UpdatedAt = DateTime.UtcNow;
        requestRepo.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            ownerUserId, "DataRequestCancelled", nameof(DataRequest), request.Id,
            new { status = DataRequestStatus.Pending.ToString() },
            new { status = DataRequestStatus.Cancelled.ToString() }, cancellationToken);

        return MapToDto(request);
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

        await NotifyCompletedAsync(request, "resolved", null, cancellationToken);

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

        await _childProfileEraser.EraseAsync(child, adminUserId, DateTime.UtcNow, cancellationToken);

        var beforeState = new { status = request.Status.ToString() };
        request.Status = DataRequestStatus.Resolved;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = adminUserId;
        _unitOfWork.Repository<DataRequest>().Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "DataRequestDeleted", nameof(DataRequest), request.Id,
            beforeState, new { status = request.Status.ToString() }, cancellationToken);

        await NotifyCompletedAsync(request, "resolved", null, cancellationToken);

        return MapToDto(request);
    }

    public async Task<DataRequestDto> RejectAsync(
        int adminUserId, int dataRequestId, string reason,
        CancellationToken cancellationToken = default)
    {
        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
        {
            throw new BadRequestException("Lý do từ chối không được để trống.");
        }

        var requestRepo = _unitOfWork.Repository<DataRequest>();
        var request = await requestRepo.GetByIdAsync(dataRequestId, cancellationToken)
                      ?? throw new NotFoundException("Data Request", dataRequestId);
        if (request.Status != DataRequestStatus.Pending)
        {
            throw new ConflictException(
                "Data Request đã được xử lý trước đó.");
        }

        request.Status = DataRequestStatus.Rejected;
        request.RejectionReason = trimmedReason;
        request.ResolvedAt = DateTime.UtcNow;
        request.ResolvedByUserId = adminUserId;
        requestRepo.Update(request);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogWriter.LogAsync(
            adminUserId, "DataRequestRejected", nameof(DataRequest), request.Id,
            new { status = DataRequestStatus.Pending.ToString() },
            new { status = DataRequestStatus.Rejected.ToString() }, cancellationToken);
        await NotifyCompletedAsync(
            request, "rejected", trimmedReason, cancellationToken);

        return MapToDto(request);
    }

    private async Task EnsureDeleteConfirmedAsync(
        int ownerUserId, ChildProfile child, CreateDataRequestDto request,
        CancellationToken cancellationToken)
    {
        var owner = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(ownerUserId, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", ownerUserId);
        if (string.IsNullOrEmpty(request.CurrentPassword)
            || !_passwordHasher.VerifyPassword(
                request.CurrentPassword, owner.PasswordHash))
        {
            throw new BadRequestException("Mật khẩu hiện tại không chính xác.");
        }

        if (!string.Equals(
                request.ConfirmNickname?.Trim(), child.Nickname,
                StringComparison.Ordinal))
        {
            throw new BadRequestException(
                "Biệt danh nhập vào không khớp với biệt danh của trẻ.");
        }
    }

    private async Task NotifyCompletedAsync(
        DataRequest request, string result, string? reason,
        CancellationToken cancellationToken)
    {
        var requester = await _unitOfWork.Repository<UserAccount>()
            .GetByIdAsync(request.RequestedByUserId, cancellationToken);
        if (requester == null)
        {
            return;
        }

        var requestType = request.RequestType.ToString().ToLowerInvariant();
        await _notificationService.CreateAsync(
            requester.Id, NotificationType.DataRequestCompleted,
            JsonSerializer.Serialize(new
            {
                dataRequestId = request.Id,
                childProfileId = request.ChildProfileId,
                request_type = requestType,
                result,
                reason
            }), cancellationToken);
        await _emailSender.SendDataRequestCompletedEmailAsync(
            requester.Email, requester.FullName, requestType, result, reason,
            cancellationToken);
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
        RejectionReason = request.RejectionReason,
        CreatedAt = request.CreatedAt,
        ResolvedAt = request.ResolvedAt,
        ResolvedByUserId = request.ResolvedByUserId
    };
}
