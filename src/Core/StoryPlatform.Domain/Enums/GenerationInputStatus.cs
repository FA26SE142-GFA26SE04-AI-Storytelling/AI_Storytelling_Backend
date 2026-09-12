namespace StoryPlatform.Domain.Enums;

public enum GenerationInputStatus
{
    PendingInput = 1,
    CheckingInput = 2,
    InputAccepted = 3,
    InputBlocked = 4,
    InputCheckFailed = 5
}
