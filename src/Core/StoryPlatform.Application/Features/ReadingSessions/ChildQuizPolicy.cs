namespace StoryPlatform.Application.Features.ReadingSessions;

public static class ChildQuizPolicy
{
    public const string ReadFirstMessage = "Em đọc xong truyện rồi mình làm câu hỏi nhé!";
    public const string BlankAnswerMessage = "Em hãy chọn hoặc nhập câu trả lời nhé.";
    public const string CorrectFeedback = "Giỏi quá! Câu trả lời của em khớp rồi.";
    public const string NotMatchedFeedback = "Chưa khớp rồi. Em thử nghe lại đoạn truyện rồi trả lời lại nhé!";
    public const int MaxAnswerLength = 200;

    /// <summary>Hai lần nộp giống hệt nhau cho cùng một câu trong khoảng này coi là bấm đúp.</summary>
    public static readonly TimeSpan DoubleTapWindow = TimeSpan.FromSeconds(3);
}
