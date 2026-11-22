using StoryPlatform.Application.Features.CreatorApplications.Interfaces;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.AccessControl;

/// <summary>Giá trị claim role trong JWT; <c>CreatorApplicant</c> là role tạm của Creator đang chờ duyệt.</summary>
public static class RoleCodes
{
    public const string User = nameof(UserRole.User);
    public const string ContentCreator = nameof(UserRole.ContentCreator);
    public const string CreatorApplicant = CreatorApplicantAccess.ApplicantRole;
    public const string Moderator = nameof(UserRole.Moderator);
    public const string Administrator = nameof(UserRole.Administrator);

    public static readonly IReadOnlyList<string> All = [User, ContentCreator, CreatorApplicant, Moderator, Administrator];

    /// <summary>Role nhân viên: Administrator chỉnh được quyền của các role này.</summary>
    public static readonly IReadOnlyList<string> Staff = [Moderator, Administrator];
}
