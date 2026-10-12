using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety;

/// <summary>Quy tắc Allowed/Blocked dùng chung cho Luồng 2, 3 và 6.</summary>
public static class SafetyPolicyEvaluator
{
    /// <summary>
    /// Blocked luôn thắng; Allowed rỗng cho phép mọi danh mục không bị Blocked;
    /// khi có Allowed thì chỉ các danh mục nằm trong Allowed được phép.
    /// </summary>
    public static bool IsCategoryPermitted(
        int contentCategoryId, IReadOnlyCollection<SafetyPolicyCategory> rules)
    {
        if (rules.Any(rule => rule.ContentCategoryId == contentCategoryId
                              && rule.Rule == PolicyRule.Blocked))
        {
            return false;
        }

        var allowed = rules.Where(rule => rule.Rule == PolicyRule.Allowed).ToList();
        return allowed.Count == 0
               || allowed.Any(rule => rule.ContentCategoryId == contentCategoryId);
    }
}
