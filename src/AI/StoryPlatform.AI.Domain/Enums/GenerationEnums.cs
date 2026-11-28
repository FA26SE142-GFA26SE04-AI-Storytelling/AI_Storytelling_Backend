namespace StoryPlatform.AI.Domain.Enums;

public enum GenerationOperation
{
    OutlineGeneration = 1,
    StoryExpansion = 2,
    Refinement = 3,
    Evaluation = 4
}

public enum GenerationStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}

public enum PromptType
{
    Outline = 1,
    Story = 2,
    Refinement = 3,
    StoryContent = 4,
    StoryContentRefinement = 5,
    Vocabulary = 6,
    Quiz = 7,
    ContentSafety = 9,
    ListeningCheckpoint = 10,
    ListeningRolePlay = 11,
    ListeningFillBlank = 12,
    ListeningPicturePrediction = 13,
    ListeningEventOrdering = 14,
    ListeningReadAloud = 15,
    ListeningCharacterLetter = 16,
    ListeningWhoSaidIt = 17,
    ListeningCheckpointLiteral = 18,
    ListeningPicturePredictionAge6 = 19,
    ListeningPictureSearch = 20,
    ListeningTrueFalse = 21,
    ListeningFollowAction = 22,
    ListeningSentenceBuilder = 23,
    ListeningEventOrderingAge7 = 24,
    ListeningAlternateReading = 25,
    ListeningInferenceQuestion = 26,
    ListeningCauseEffect = 27,
    ListeningSceneTitle = 28,
    ListeningThoughtBubble = 29,
    ListeningCharacterTrial = 30,
    ListeningFindWrongDetail = 31,
    ListeningRelationshipMap = 32,
    ListeningStoryEditor = 33,
    ListeningWhatIf = 34
}
