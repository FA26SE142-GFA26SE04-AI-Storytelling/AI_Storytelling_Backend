namespace StoryPlatform.Application.Features.ContentGeneration;

public sealed class LearningBatchOptions
{
    public const string SectionName = "AI:LearningGeneration";
    public int MaxConcurrentPerStory { get; set; } = 2;
    public int MaxConcurrentRequests { get; set; } = 4;
    public int RequestsPerMinute { get; set; } = 30;
    public int BurstSize { get; set; } = 4;
    public int MaxAttempts { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 120;
    public int BaseRetryDelaySeconds { get; set; } = 2;
    /// <summary>
    /// Minimum wait before retrying a provider rate limit that arrived without a Retry-After hint. A short
    /// exponential delay lands inside the same quota window and burns the remaining attempts for nothing.
    /// </summary>
    public int QuotaRetryFloorSeconds { get; set; } = 60;
    public int LeaseMinutes { get; set; } = 10;
    public int YoungerTargetWords { get; set; } = 900;
    public int OlderTargetWords { get; set; } = 1500;
    public int YoungerVocabularyTarget { get; set; } = 12;
    public int OlderVocabularyTarget { get; set; } = 18;
    public int YoungerQuizTarget { get; set; } = 24;
    public int OlderQuizTarget { get; set; } = 36;
    public StoryPlatform.Contracts.AI.Models.LearningGenerationProfile Profile(string age, int requested = 0)
    {
        var older = age is "9-10" or "Age_9_10";
        var value = StoryPlatform.Contracts.AI.Models.LearningGenerationProfile.ForAge(age,
            requested > 0 ? requested : older ? OlderTargetWords : YoungerTargetWords) with
        { VocabularyTarget = older ? OlderVocabularyTarget : YoungerVocabularyTarget, QuizTarget = older ? OlderQuizTarget : YoungerQuizTarget };
        if (value.TargetWords is < 100 or > 9000 || value.VocabularyTarget is < 5 or > 20 || value.QuizTarget is < 3 or > 40)
            throw new InvalidOperationException("LEARNING_PROFILE_CONFIGURATION_INVALID");
        return value;
    }
}
