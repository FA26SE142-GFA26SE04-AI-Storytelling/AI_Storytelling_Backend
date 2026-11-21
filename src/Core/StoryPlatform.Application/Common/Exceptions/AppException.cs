using System;

namespace StoryPlatform.Application.Common.Exceptions;

public abstract class AppException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }

    protected AppException(string message, int statusCode = 400, string? errorCode = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}

public class NotFoundException : AppException
{
    /// <summary>Tên đối tượng (tiếng Anh, dùng trong code) đổi sang tiếng Việt cho câu báo lỗi.</summary>
    private static readonly Dictionary<string, string> EntityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Story"] = "truyện", ["Story draft"] = "bản nháp truyện", ["StoryVersion"] = "phiên bản truyện",
        ["Current story version"] = "phiên bản hiện tại của truyện", ["StoryWish"] = "điều ước",
        ["StoryGenerationRequest"] = "yêu cầu tạo truyện", ["StorySegment"] = "đoạn truyện",
        ["IllustrationBeat"] = "khung tranh", ["MediaAsset"] = "hình ảnh hoặc âm thanh", ["Media asset"] = "hình ảnh hoặc âm thanh",
        ["Media job"] = "lượt tạo hình ảnh và giọng đọc", ["Media generation job"] = "lượt tạo hình ảnh và giọng đọc",
        ["ChildProfile"] = "hồ sơ của bé", ["Audit log"] = "nhật ký thao tác", ["Business Report"] = "báo cáo kinh doanh",
        ["Content Category"] = "danh mục nội dung", ["Report"] = "báo cáo nội dung", ["Guide"] = "hướng dẫn viết",
        ["AssistanceTask"] = "phiên trợ lý", ["AssistanceStep"] = "bước trợ lý", ["Import preview"] = "bản xem trước khi nhập truyện",
        ["ModerationRubricVersion"] = "phiên bản bộ tiêu chí kiểm duyệt", ["ModerationItem"] = "mục kiểm duyệt",
        ["Family confirmation"] = "xác nhận của gia đình", ["ParentStoryPackage"] = "gói truyện",
        ["Interaction"] = "tương tác", ["ListeningInteraction"] = "tương tác khi nghe", ["Vocabulary"] = "từ vựng",
        ["Proposal"] = "đề xuất",
        ["Safety Policy của hồ sơ trẻ"] = "chính sách an toàn của bé", ["Learning Profile của hồ sơ trẻ"] = "hồ sơ học tập của bé",
        ["Gói Subscription"] = "gói đăng ký", ["Đơn đăng ký Content Creator"] = "đơn đăng ký nhà sáng tạo nội dung",
        ["Tài khoản Content Creator"] = "tài khoản nhà sáng tạo nội dung",
    };

    public NotFoundException(string message) : base(Describe(message), 404)
    {
    }

    public NotFoundException(string entityName, object key)
        : base(EntityNames.TryGetValue(entityName, out var name)
            ? $"Không tìm thấy {name}. Có thể dữ liệu đã bị xoá hoặc bạn không có quyền truy cập."
            : $"Không tìm thấy {entityName} với mã định danh [{key}].", 404)
    {
    }

    private static string Describe(string message) => EntityNames.TryGetValue(message.Trim(), out var name)
        ? $"Không tìm thấy {name}. Có thể dữ liệu đã bị xoá hoặc bạn không có quyền truy cập."
        : message;
}

public class BadRequestException : AppException
{
    public BadRequestException(string message) : base(message, 400)
    {
    }
}

public class UnauthorizedException : AppException
{
    public UnauthorizedException(string message = "Bạn cần đăng nhập để thực hiện chức năng này.") : base(message, 401)
    {
    }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message = "Bạn không có quyền truy cập tài nguyên này.") : base(message, 403)
    {
    }
}

public class ConflictException : AppException
{
    public ConflictException(string message, string? errorCode = null) : base(message, 409, errorCode)
    {
    }
}
