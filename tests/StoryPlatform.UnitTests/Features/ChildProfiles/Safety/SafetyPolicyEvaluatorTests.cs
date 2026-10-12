using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Safety;

public class SafetyPolicyEvaluatorTests
{
    private static SafetyPolicyCategory Rule(int categoryId, PolicyRule rule) =>
        new() { ContentCategoryId = categoryId, Rule = rule };

    [Fact]
    public void EmptyRules_PermitEverything() =>
        Assert.True(SafetyPolicyEvaluator.IsCategoryPermitted(3, new List<SafetyPolicyCategory>()));

    [Fact]
    public void BlockedCategory_IsNotPermitted() =>
        Assert.False(SafetyPolicyEvaluator.IsCategoryPermitted(
            3, new[] { Rule(3, PolicyRule.Blocked) }));

    [Fact]
    public void OnlyBlockedRules_OtherCategoriesPermitted() =>
        Assert.True(SafetyPolicyEvaluator.IsCategoryPermitted(
            4, new[] { Rule(3, PolicyRule.Blocked) }));

    [Fact]
    public void WhenAllowedListExists_OnlyAllowedCategoriesPermitted()
    {
        var rules = new[] { Rule(1, PolicyRule.Allowed), Rule(2, PolicyRule.Allowed) };

        Assert.True(SafetyPolicyEvaluator.IsCategoryPermitted(1, rules));
        Assert.False(SafetyPolicyEvaluator.IsCategoryPermitted(5, rules));
    }

    [Fact]
    public void BlockedAlwaysWinsOverAllowed() =>
        Assert.False(SafetyPolicyEvaluator.IsCategoryPermitted(
            1, new[] { Rule(1, PolicyRule.Allowed), Rule(1, PolicyRule.Blocked) }));
}
