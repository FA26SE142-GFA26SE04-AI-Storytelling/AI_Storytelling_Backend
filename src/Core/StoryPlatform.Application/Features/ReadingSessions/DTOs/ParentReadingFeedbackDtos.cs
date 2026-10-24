namespace StoryPlatform.Application.Features.ReadingSessions;

public class ParentReadingFeedbackQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ParentReadingFeedbackItemDto
{
    public int SessionId { get; set; }
    public int StoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    /// <summary>"VeryLike", "Like", "Neutral", "Dislike"; null nếu con chưa chọn.</summary>
    public string? Feeling { get; set; }
    public string? FeelingLabel { get; set; }
    /// <summary>Số câu quiz của truyện (0 nếu truyện không có quiz).</summary>
    public int QuizTotalQuestions { get; set; }
    /// <summary>Phần trăm của lần làm quiz tốt nhất; null nếu con chưa làm hoặc truyện không có quiz.</summary>
    public decimal? QuizBestPercent { get; set; }
}
