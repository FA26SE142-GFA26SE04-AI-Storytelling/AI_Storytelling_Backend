namespace StoryPlatform.Application.Features.ReadingSessions;

public sealed record BadgeInfo(string Code, string Name, string Description);

public static class BadgeCatalog
{
    public const string FirstStory = "FIRST_STORY_COMPLETED";
    public const string Bookworm = "BOOKWORM";
    public const string Streak3 = "STREAK_3_DAYS";
    public const string Streak7 = "STREAK_7_DAYS";
    public const string QuizChampion = "QUIZ_CHAMPION";

    public static readonly IReadOnlyList<BadgeInfo> All = new List<BadgeInfo>
    {
        new(FirstStory, "Truyện đầu tiên", "Em đã đọc xong truyện đầu tiên!"),
        new(Bookworm, "Mọt sách", "Em đã đọc xong 5 truyện khác nhau!"),
        new(Streak3, "Đọc 3 ngày liền", "Em đã đọc truyện 3 ngày liên tiếp!"),
        new(Streak7, "Đọc 7 ngày liền", "Em đã đọc truyện 7 ngày liên tiếp!"),
        new(QuizChampion, "Nhà vô địch câu hỏi", "Em trả lời khớp hết các câu hỏi của một truyện!")
    };
}
