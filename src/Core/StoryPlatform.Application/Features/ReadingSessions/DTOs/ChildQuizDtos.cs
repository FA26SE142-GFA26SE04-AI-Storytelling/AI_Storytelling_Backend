namespace StoryPlatform.Application.Features.ReadingSessions;

public class QuizDto
{
    public int SessionId { get; set; }
    public int TotalQuestions { get; set; }
    /// <summary>Phần trăm của lần làm tốt nhất; null nếu chưa làm lần nào.</summary>
    public decimal? BestPercent { get; set; }
    public List<QuizQuestionDto> Questions { get; set; } = new();
}

public class QuizQuestionDto
{
    public int QuizItemId { get; set; }
    /// <summary>"MultipleChoice", "TrueFalse" hoặc "ShortAnswer".</summary>
    public string Type { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    /// <summary>Rỗng với tự luận ngắn.</summary>
    public List<string> Choices { get; set; } = new();
}

public class SubmitQuizAnswerRequestDto
{
    public int QuizItemId { get; set; }
    public string? Answer { get; set; }
}

public class QuizAnswerResultDto
{
    /// <summary>EXP vừa nhận cho câu này (0 nếu câu đã từng đúng hoặc chưa khớp).</summary>
    public int ExpGained { get; set; }
    public List<string> NewBadges { get; set; } = new();

    public int QuizItemId { get; set; }
    public bool IsCorrect { get; set; }
    /// <summary>Lời khen hoặc gợi ý nhẹ nhàng; không bao giờ nêu đáp án.</summary>
    public string Feedback { get; set; } = string.Empty;
    /// <summary>Lần làm hiện tại đã trả lời hết các câu.</summary>
    public bool RunCompleted { get; set; }
    public decimal CurrentPercent { get; set; }
    public decimal? BestPercent { get; set; }
}

public class QuizResultDto
{
    public int SessionId { get; set; }
    public int TotalQuestions { get; set; }
    /// <summary>Số câu đã trả lời / số câu đúng trong lần làm hiện tại (lần gần nhất).</summary>
    public int Answered { get; set; }
    public int Correct { get; set; }
    public decimal CurrentPercent { get; set; }
    public decimal? BestPercent { get; set; }
    public bool RunCompleted { get; set; }
}
