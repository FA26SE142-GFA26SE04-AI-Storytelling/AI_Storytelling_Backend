namespace StoryPlatform.Application.Features.AuditLogs.DTOs;

public class AuditLogDto
{
    public int Id { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>Một dòng audit log cho Administrator (Luồng 5): thêm người làm và trạng thái trước/sau.</summary>
public class AdminAuditLogDto : AuditLogDto
{
    /// <summary>null khi hệ thống tự làm (worker, job).</summary>
    public string? ActorUsername { get; set; }
    public string? ActorFullName { get; set; }
    public string? ActorRole { get; set; }
    /// <summary>JSON trạng thái trước/sau như lúc ghi; null khi không có.</summary>
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
}

/// <summary>Bộ lọc audit log của Administrator; mọi điều kiện đều tuỳ chọn và kết hợp bằng AND.</summary>
public class AdminAuditLogQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int? ActorUserId { get; set; }
    /// <summary>Chỉ log do hệ thống tự ghi (không có người làm).</summary>
    public bool SystemOnly { get; set; }
    /// <summary>Khớp đúng tên action, ví dụ PlanSettingsUpdated.</summary>
    public string? Action { get; set; }
    /// <summary>Khớp đúng loại đối tượng, ví dụ UserAccount.</summary>
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    /// <summary>Từ thời điểm (UTC, gồm cả mốc này).</summary>
    public DateTime? From { get; set; }
    /// <summary>Đến thời điểm (UTC, gồm cả mốc này).</summary>
    public DateTime? To { get; set; }
}

/// <summary>Giá trị cho các ô chọn của màn audit log.</summary>
public class AuditLogFiltersDto
{
    public List<string> Actions { get; set; } = [];
    public List<string> EntityTypes { get; set; } = [];
}
