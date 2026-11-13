using System.Text;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.StoryRatings;

/// <summary>Quy tắc dùng chung của đánh giá truyện: giới hạn, truyện được đánh giá, tên hiển thị người viết.</summary>
public static class StoryRatingRules
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 1000;
    public const int MaxReportNoteLength = 500;
    public const int MaxHiddenReasonLength = 1000;
    public const string ReviewerPrefix = "Phụ huynh";

    /// <summary>Truyện Creator đang ở thư viện cộng đồng (cùng điều kiện với GET /api/v1/Story/community).</summary>
    public static bool IsPublicCreatorStory(Story story) =>
        !story.IsDeleted && story.Scope == StoryScope.Creator && story.IsPublished
        && story.Status == StoryStatus.Ready && story.ArchivedAt == null;

    /// <summary>"Phụ huynh H***": chữ cái đầu của từ cuối trong họ tên (hoặc tên đăng nhập). Không lộ họ tên, email hay id.</summary>
    public static string FormatReviewerName(UserAccount? user)
    {
        var source = string.IsNullOrWhiteSpace(user?.FullName) ? user?.Username : user!.FullName;
        var lastWord = source?.Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        return string.IsNullOrEmpty(lastWord)
            ? ReviewerPrefix
            : $"{ReviewerPrefix} {char.ToUpperInvariant(lastWord[0])}***";
    }
}
