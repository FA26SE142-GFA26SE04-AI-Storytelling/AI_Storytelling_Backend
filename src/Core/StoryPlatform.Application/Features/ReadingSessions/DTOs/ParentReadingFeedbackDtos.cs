namespace StoryPlatform.Application.Features.ReadingSessions;

public class ParentReadingFeedbackQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    /// <summary>"Completed" (mặc định) hoặc "Unfinished" (phiên mở ra nhưng chưa đọc xong).</summary>
    public string? Status { get; set; }
    /// <summary>Mốc đầu (UTC, tính cả mốc). Completed lọc theo CompletedAt; Unfinished lọc theo StartedAt.</summary>
    public DateTime? From { get; set; }
    /// <summary>Mốc cuối (UTC, không tính mốc).</summary>
    public DateTime? To { get; set; }
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
    /// <summary>Câu con trả lời ở Điểm dừng cảm xúc/cảm nhận trong lần đọc này, theo thời gian.</summary>
    public List<ConversationAnswerDto> ConversationAnswers { get; set; } = new();
    /// <summary>Tên SessionStatus: "Completed", "Started", "Reading", "Activity", "Abandoned", "ForceExited".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    /// <summary>Tổng giây bé đã đọc trong phiên này (FE cộng dồn gửi lên khi lưu tiến độ).</summary>
    public int TimeSpentSeconds { get; set; }
    /// <summary>Số trang đã đọc qua (trang hiện tại - 1, lấy lớn nhất).</summary>
    public int PagesCompleted { get; set; }
    /// <summary>Tổng số trang (scene) của bản truyện; 0 nếu không tìm thấy.</summary>
    public int TotalPages { get; set; }
    /// <summary>Số lần bé đã đọc xong truyện này (mọi thời điểm, không theo bộ lọc ngày).</summary>
    public int ReadCount { get; set; }
    /// <summary>Dòng này là lần đọc xong thứ mấy (1 = lần đầu); null với dòng chưa đọc xong.</summary>
    public int? ReadOrdinal { get; set; }
    /// <summary>Chỉ dùng cho dòng chưa xong: true nếu sau lần mở này bé đã đọc xong truyện.</summary>
    public bool LaterCompleted { get; set; }
    /// <summary>Số lần bé đã làm quiz của truyện này (mọi lần đọc); 0 nếu chưa làm hoặc không có quiz.</summary>
    public int QuizRunCount { get; set; }
    /// <summary>Phần trăm của lần làm gần nhất; null nếu chưa làm hoặc không có quiz.</summary>
    public decimal? QuizLatestPercent { get; set; }
    /// <summary>Kết quả luyện đọc thành tiếng trong phiên này; null nếu bé chưa đọc to.</summary>
    public ParentReadAloudSummaryDto? ReadAloud { get; set; }
    /// <summary>Kết quả luyện đọc to sau truyện trong phiên này; null nếu bé chưa luyện.</summary>
    public ParentReadAloudPracticeDto? ReadAloudPractice { get; set; }
    /// <summary>Tổng EXP bé nhận trong phiên này (đọc xong, câu đố, lưu từ).</summary>
    public int ExpEarned { get; set; }
    /// <summary>Từ bé lưu vào sổ lần đầu trong phiên này, theo thứ tự lưu.</summary>
    public List<ParentCollectedWordDto> WordsCollected { get; set; } = new();
    /// <summary>"ListenFirst" (nghe là chính) hoặc "ReadFirst" (tự đọc); null nếu phiên cũ chưa ghi.</summary>
    public string? ReadingMode { get; set; }
    /// <summary>Bản "Bé kể lại" của phiên này; null nếu bé không gửi hoặc phụ huynh đã xóa.</summary>
    public ParentRetellBriefDto? Retell { get; set; }
}

public class ConversationAnswerDto
{
    public string Question { get; set; } = string.Empty;
    /// <summary>"Emotion" hoặc "Opinion".</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Chữ của thẻ con đã chọn.</summary>
    public string AnswerText { get; set; } = string.Empty;
    /// <summary>Scene có câu hỏi; null nếu không còn tìm thấy.</summary>
    public int? SceneIndex { get; set; }
    public DateTime AnsweredAt { get; set; }
}

public class ParentQuizDetailDto
{
    public int SessionId { get; set; }
    public int StoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    /// <summary>Các lần làm quiz của truyện này (mọi lần đọc), theo thứ tự thời gian.</summary>
    public List<ParentQuizRunDto> Runs { get; set; } = new();
    public List<ParentQuizQuestionDto> Questions { get; set; } = new();
}

public class ParentQuizRunDto
{
    /// <summary>1 = lần đầu.</summary>
    public int RunNo { get; set; }
    public int Answered { get; set; }
    public int Correct { get; set; }
    /// <summary>Correct / TotalQuestions; câu bỏ qua tính là chưa đúng.</summary>
    public decimal Percent { get; set; }
}

public class ParentQuizQuestionDto
{
    public int QuizItemId { get; set; }
    /// <summary>"MultipleChoice", "TrueFalse" hoặc "ShortAnswer".</summary>
    public string Type { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    /// <summary>Rỗng với tự luận ngắn.</summary>
    public List<string> Choices { get; set; } = new();
    public string? CorrectAnswer { get; set; }
    public string? Explanation { get; set; }
    /// <summary>Câu trả lời gần nhất của bé; null nếu chưa trả lời câu này.</summary>
    public string? LatestAnswer { get; set; }
    public bool? LatestIsCorrect { get; set; }
    public int TimesAnswered { get; set; }
    public int TimesCorrect { get; set; }
}

public class ParentReadAloudSummaryDto
{
    /// <summary>Số lần bé bấm đọc to (mọi lần, kể cả đọc lại cùng đoạn).</summary>
    public int Attempts { get; set; }
    /// <summary>Số đoạn khác nhau bé đã đọc to.</summary>
    public int SegmentsPracticed { get; set; }
    /// <summary>Tổng chữ đọc tốt, mỗi đoạn lấy lần đọc cuối.</summary>
    public int GoodWords { get; set; }
    public int RetryWords { get; set; }
    public int TotalWords { get; set; }
    /// <summary>GoodWords / TotalWords (%).</summary>
    public decimal GoodPercent { get; set; }
}

public class ParentReadAloudPracticeDto
{
    /// <summary>Số lần bé đọc theo đoạn luyện trong phiên.</summary>
    public int Attempts { get; set; }
    /// <summary>Số chữ của đoạn luyện.</summary>
    public int TotalWords { get; set; }
    /// <summary>Số chữ đọc tốt ở lần gần nhất.</summary>
    public int LatestGoodWords { get; set; }
    public decimal LatestGoodPercent { get; set; }
    /// <summary>Phần trăm chữ đọc tốt của lần tốt nhất.</summary>
    public decimal BestGoodPercent { get; set; }
}

public class ParentCollectedWordDto
{
    public int VocabularyId { get; set; }
    public string Term { get; set; } = string.Empty;
}

public class ParentRetellBriefDto
{
    /// <summary>Id trong API ParentStorySummary (dùng để mở/nghe/xóa bản kể lại).</summary>
    public int SummaryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool HasRecording { get; set; }
    /// <summary>Số câu bé hỏi bố mẹ về tranh hoặc cả cảnh.</summary>
    public int FlagCount { get; set; }
    /// <summary>Số câu bố mẹ chưa đánh dấu đã trả lời.</summary>
    public int UnansweredCount { get; set; }
}

public class ParentReadingOverviewQueryDto
{
    /// <summary>Mốc đầu (UTC, tính cả mốc); null = từ trước tới nay.</summary>
    public DateTime? From { get; set; }
    /// <summary>Mốc cuối (UTC, không tính mốc); null = tới hiện tại.</summary>
    public DateTime? To { get; set; }
}

public class ParentReadingOverviewDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>Số lần đọc xong (CompletedAt trong khoảng).</summary>
    public int CompletedSessions { get; set; }
    public int DistinctStoriesCompleted { get; set; }
    /// <summary>Số truyện có lần mở trong khoảng mà chưa đọc xong và sau đó cũng chưa đọc xong.</summary>
    public int UnfinishedStories { get; set; }
    /// <summary>Tổng giây đọc của các phiên trong khoảng (đã xong + chưa xong).</summary>
    public int TotalTimeSpentSeconds { get; set; }
    /// <summary>Số truyện đọc xong trong khoảng mà bé đã làm quiz.</summary>
    public int QuizStoriesTaken { get; set; }
    /// <summary>Trung bình điểm tốt nhất của các truyện đó; null nếu chưa làm quiz nào.</summary>
    public decimal? AverageQuizBestPercent { get; set; }
    /// <summary>Khóa: "VeryLike", "Like", "Neutral", "Dislike", "None" (chưa chọn); tính trên các lần đọc xong.</summary>
    public Dictionary<string, int> FeelingCounts { get; set; } = new();
    public int ExpEarned { get; set; }
    /// <summary>Số từ khác nhau bé lưu lần đầu trong khoảng.</summary>
    public int WordsCollected { get; set; }
    /// <summary>Tỉ lệ chữ đọc tốt khi đọc to; null nếu chưa đọc to.</summary>
    public decimal? ReadAloudGoodPercent { get; set; }
    /// <summary>Số lần đọc trong khoảng có bản "Bé kể lại" chưa xóa.</summary>
    public int RetellCount { get; set; }
}
