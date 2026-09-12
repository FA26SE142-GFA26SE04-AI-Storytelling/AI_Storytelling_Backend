namespace StoryPlatform.Domain.Enums;

public enum TelemetryEventType
{
    SessionStarted = 1,
    SessionCompleted = 2,
    PageViewed = 3,
    PageCompleted = 4,
    TtsStarted = 5,
    TtsCompleted = 6,
    AsrWordResult = 7,
    PronunciationError = 8,
    VocabularyOpened = 9,
    VocabularyCollected = 10,
    QuizAnswered = 11,
    QuizCompleted = 12,
    RewardGranted = 13,
    BadgeUnlocked = 14,
    StoryBookmarked = 15,
    StoryFavorited = 16,
    LibrarySearched = 17
}
