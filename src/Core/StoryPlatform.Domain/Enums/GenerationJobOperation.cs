namespace StoryPlatform.Domain.Enums;

public enum GenerationJobOperation
{
    GenerateOutline = 1,
    RegenerateOutline = 2,
    GenerateContent = 3,
    GenerateVocabulary = 4,
    GenerateQuiz = 5,
    // Reserved for persisted legacy jobs; never enqueue, execute or retry this operation.
    GenerateDiscussion = 6,
    GenerateMediaPackage = 7,
    BuildMediaContext = 8,
    SegmentStory = 9,
    GenerateIllustration = 10,
    GenerateTts = 11,
    ValidateIllustration = 12,
    FinalizeMedia = 13,
    GenerateListeningInteraction = 14,
    CheckParentPackage = 15,
    BuildParentPackage = 16
}
