namespace StoryPlatform.Domain.Enums;

public enum AccountStatus
{
    Registered = 1,
    EmailVerified = 2,
    LoggedIn = 3,
    PasswordResetPending = 4,
    LoggedOut = 5,
    Suspended = 6
}
