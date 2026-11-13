using System.Text.Json.Serialization;
using StoryPlatform.Application.Common.Models;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryRatings.DTOs;

public class StoryRatingQueryDto
{
    public const string Newest = "newest";
    public const string Highest = "highest";
    public const string Lowest = "lowest";
    public const int MaxPageSize = 50;

    private int _pageSize = 10;

    public int PageIndex { get; set; } = 1;

    /// <summary>1–50; nhỏ hơn 1 thì dùng 10, lớn hơn 50 thì dùng 50.</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 10 : Math.Min(value, MaxPageSize);
    }

    /// <summary>newest (mặc định), highest hoặc lowest; không phân biệt hoa thường.</summary>
    public string? OrderBy { get; set; }
}

public class UpsertStoryRatingRequestDto
{
    /// <summary>1–5 sao.</summary>
    public int Rating { get; set; }

    /// <summary>Tùy chọn, tối đa 1000 ký tự; số điện thoại / email bị che.</summary>
    public string? Comment { get; set; }
}

/// <summary>Một đánh giá đang hiện công khai. Không có id tài khoản, email hay tên bé của người viết.</summary>
public class StoryRatingDto
{
    public int RatingId { get; set; }

    /// <summary>"Phụ huynh H***".</summary>
    public string ReviewerName { get; set; } = string.Empty;

    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Đánh giá của chính người gọi, ở mọi trạng thái.</summary>
public class MyStoryRatingDto : StoryRatingDto
{
    /// <summary>Published, PendingModeration hoặc Hidden.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Lý do Moderator ẩn; chỉ có khi Status = Hidden.</summary>
    public string? HiddenReason { get; set; }
}

public class StoryRatingSummaryDto
{
    /// <summary>Trung bình các đánh giá đang hiện, làm tròn 1 chữ số; null khi chưa có.</summary>
    public double? AverageRating { get; set; }

    public int RatingCount { get; set; }

    /// <summary>Số đánh giá đang hiện theo từng mức sao, khóa 1..5.</summary>
    public Dictionary<int, int> Distribution { get; set; } = new();
}

public class StoryRatingListDto
{
    public int StoryId { get; set; }
    public StoryRatingSummaryDto Summary { get; set; } = new();

    /// <summary>Chỉ có ở API phụ huynh; null khi chưa đánh giá.</summary>
    public MyStoryRatingDto? MyRating { get; set; }

    /// <summary>True khi người gọi có bé đang hoạt động đã mở đọc truyện.</summary>
    public bool CanRate { get; set; }

    public PagedResult<StoryRatingDto> Ratings { get; set; } = new();
}

public class ReportStoryRatingRequestDto
{
    /// <summary>Offensive, Spam, PersonalInfo, Irrelevant hoặc Other.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StoryRatingReportReason Reason { get; set; }

    /// <summary>Tùy chọn, tối đa 500 ký tự; số điện thoại / email bị che.</summary>
    public string? Note { get; set; }
}

public class StoryRatingReportResultDto
{
    public int RatingId { get; set; }

    /// <summary>Luôn "Received": đánh giá vẫn hiện cho đến khi Moderator quyết định.</summary>
    public string Status { get; set; } = "Received";
}
