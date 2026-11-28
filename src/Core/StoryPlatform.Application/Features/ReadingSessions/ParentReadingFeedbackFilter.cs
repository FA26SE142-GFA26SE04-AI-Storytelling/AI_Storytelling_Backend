using System.Linq.Expressions;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ReadingSessions;

/// <summary>Bộ lọc dùng chung cho danh sách kết quả đọc và trang tổng quan phía phụ huynh.</summary>
public static class ParentReadingFeedbackFilter
{
    public const string Completed = "Completed";
    public const string Unfinished = "Unfinished";
    public const string InvalidStatusMessage = "Trạng thái lọc không hợp lệ. Chỉ nhận Completed hoặc Unfinished.";
    public const string InvalidRangeMessage = "Mốc bắt đầu phải nhỏ hơn mốc kết thúc.";

    /// <summary>null/rỗng/"Completed" → false; "Unfinished" → true (không phân biệt hoa thường); giá trị khác → 400.</summary>
    public static bool IsUnfinished(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)
            || string.Equals(status.Trim(), Completed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(status.Trim(), Unfinished, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new BadRequestException(InvalidStatusMessage);
    }

    public static void EnsureRange(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
        {
            throw new BadRequestException(InvalidRangeMessage);
        }
    }

    /// <summary>
    /// Phiên của trẻ theo trạng thái và khoảng thời gian [from, to). Phiên đã xong lọc theo CompletedAt; phiên chưa xong
    /// (Started/Reading/Activity/Abandoned/ForceExited) lọc theo StartedAt. Viết OR tường minh để EF dịch được với
    /// cột enum lưu dạng chuỗi. storyId khác null thì chỉ lấy phiên của truyện đó.
    /// </summary>
    public static Expression<Func<ReadingSession, bool>> Sessions(
        int childProfileId, bool unfinished, DateTime? from, DateTime? to, int? storyId = null)
    {
        if (!unfinished)
        {
            return session => session.ChildProfileId == childProfileId
                              && session.Status == SessionStatus.Completed
                              && !session.IsDeleted
                              && (storyId == null || session.StoryId == storyId)
                              && (from == null || session.CompletedAt >= from)
                              && (to == null || session.CompletedAt < to);
        }

        return session => session.ChildProfileId == childProfileId
                          && (session.Status == SessionStatus.Started
                              || session.Status == SessionStatus.Reading
                              || session.Status == SessionStatus.Activity
                              || session.Status == SessionStatus.Abandoned
                              || session.Status == SessionStatus.ForceExited)
                          && !session.IsDeleted
                          && (storyId == null || session.StoryId == storyId)
                          && (from == null || session.StartedAt >= from)
                          && (to == null || session.StartedAt < to);
    }
}
