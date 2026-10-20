namespace StoryPlatform.Domain.Enums;

public enum SessionStatus
{
    Started = 1,
    Reading = 2,
    Activity = 3,
    Completed = 4,
    Abandoned = 5,

    /// <summary>Phiên bị hệ thống ngắt; không được tính là trẻ tự bỏ ngang.</summary>
    ForceExited = 6
}
