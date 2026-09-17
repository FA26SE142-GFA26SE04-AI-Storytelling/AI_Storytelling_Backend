namespace StoryPlatform.Domain.Enums;

public enum GenerationJobOperation
{
    GenerateOutline = 1,
    RegenerateOutline = 2,
    GenerateContent = 3,
    GenerateVocabulary = 4,
    GenerateQuiz = 5,
    GenerateDiscussion = 6,
    GenerateMediaPackage = 7,
    BuildMediaContext = 8,
    SegmentStory = 9,
    GenerateIllustration = 10,
    GenerateTts = 11,
    ValidateIllustration = 12,
    FinalizeMedia = 13
}
