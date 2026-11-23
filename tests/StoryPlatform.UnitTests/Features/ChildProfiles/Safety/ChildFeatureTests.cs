using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Moq;
using StoryPlatform.Api.Authorization;
using StoryPlatform.Api.Controllers;
using StoryPlatform.Api.Extensions;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Features.ChildProfiles.Safety;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Safety.Services;
using StoryPlatform.Domain.Entities;
using Xunit;

namespace StoryPlatform.UnitTests.Features.ChildProfiles.Safety;

public class ChildFeatureTests
{
    private static readonly SafetyPolicy AllOff = new()
    {
        QuizEnabled = false, NotebookEnabled = false, LibraryBrowseEnabled = false, StoryWishesEnabled = false,
        VoiceAnswersEnabled = false, ReflectionEnabled = false, RewardsEnabled = false
    };

    [Fact]
    public void Catalog_has_the_seven_codes_in_order()
    {
        Assert.Equal(
            ["child.quiz", "child.notebook", "child.library.browse", "child.wishes", "child.voice", "child.reflection", "child.rewards"],
            ChildFeatures.All.Select(f => f.Code));
    }

    [Fact]
    public void Missing_policy_enables_everything()
    {
        Assert.All(ChildFeatures.All, f => Assert.True(ChildFeatures.IsEnabled(null, f.Code)));
    }

    [Fact]
    public void Each_code_reads_its_own_column()
    {
        Assert.All(ChildFeatures.All, f => Assert.False(ChildFeatures.IsEnabled(AllOff, f.Code)));
        Assert.True(ChildFeatures.IsEnabled(new SafetyPolicy { QuizEnabled = true, VoiceAnswersEnabled = true }, ChildFeatures.Voice));
    }

    [Fact]
    public void Unknown_code_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => ChildFeatures.IsEnabled(null, "child.unknown"));
    }

    [Fact]
    public async Task Service_lists_disabled_features_in_catalog_order()
    {
        var repo = new Mock<IGenericRepository<SafetyPolicy>>();
        repo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<SafetyPolicy, bool>>>(), null, It.IsAny<CancellationToken>()))
            // VoiceAnswersEnabled mặc định false (cần phụ huynh đồng ý), nên bật hẳn ở đây để chỉ quiz/rewards tắt.
            .ReturnsAsync(new SafetyPolicy { ChildProfileId = 7, RewardsEnabled = false, QuizEnabled = false, VoiceAnswersEnabled = true });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.Repository<SafetyPolicy>()).Returns(repo.Object);
        var service = new ChildFeatureService(uow.Object);

        Assert.Equal(["child.quiz", "child.rewards"], await service.GetDisabledAsync(7));
        Assert.False(await service.IsEnabledAsync(7, ChildFeatures.Quiz));
        Assert.True(await service.IsEnabledAsync(7, ChildFeatures.Notebook));
    }

    [Fact]
    public async Task Filter_blocks_a_disabled_feature_with_403_and_code()
    {
        var (context, nextCalled) = ActionContext(childId: 7);
        var filter = new RequireChildFeatureFilter(new FakeFeatures(enabled: false), ChildFeatures.Quiz);

        var ex = await Assert.ThrowsAsync<ChildFeatureDisabledException>(() =>
            filter.OnActionExecutionAsync(context, () => { nextCalled[0] = true; return Task.FromResult<ActionExecutedContext>(null!); }));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("CHILD_FEATURE_DISABLED", ex.ErrorCode);
        Assert.Equal("Bố mẹ chưa bật chức năng này.", ex.Message);
        Assert.False(nextCalled[0]);
    }

    [Fact]
    public async Task Filter_lets_an_enabled_feature_through()
    {
        var (context, nextCalled) = ActionContext(childId: 7);
        var filter = new RequireChildFeatureFilter(new FakeFeatures(enabled: true), ChildFeatures.Quiz);

        await filter.OnActionExecutionAsync(context, () => { nextCalled[0] = true; return Task.FromResult<ActionExecutedContext>(null!); });

        Assert.True(nextCalled[0]);
    }

    // Cốt lõi (spec mục 5): luôn bật, không gắn [RequireChildFeature].
    private static readonly HashSet<string> Core =
    [
        "ChildAccessCredentialController.GetMySession",
        "ChildStorySummaryController.Ask",
    ];

    private static readonly HashSet<string> CoreControllers =
        ["MyRoadmapController", "ChildManualStoriesController", "ChildPackageAssetsController"];

    private static readonly Dictionary<string, string> Gated = new()
    {
        ["ChildReadingController.SearchStories"] = ChildFeatures.LibraryBrowse,
        ["ChildReadingController.ReadAloud"] = ChildFeatures.Voice,
        ["ChildReadingController.ReadAloudPractice"] = ChildFeatures.Voice,
        ["ChildStorySummaryController.SendRecording"] = ChildFeatures.Voice,
    };

    private static readonly Dictionary<string, string> GatedControllers = new()
    {
        ["ChildQuizController"] = ChildFeatures.Quiz,
        ["ChildNotebookController"] = ChildFeatures.Notebook,
        ["ChildWishesController"] = ChildFeatures.Wishes,
        ["ChildStoryReflectionController"] = ChildFeatures.Reflection,
        ["ChildRewardsController"] = ChildFeatures.Rewards,
    };

    [Fact]
    public void Every_child_action_is_core_or_gated_with_the_right_feature()
    {
        var problems = new List<string>();
        foreach (var (controller, action) in ChildActions())
        {
            var key = $"{controller.Name}.{action.Name}";
            var feature = action.GetCustomAttribute<RequireChildFeatureAttribute>()?.Feature
                          ?? controller.GetCustomAttribute<RequireChildFeatureAttribute>()?.Feature;
            var expected = Gated.TryGetValue(key, out var a) ? a
                : GatedControllers.TryGetValue(controller.Name, out var c) ? c : null;
            var isCore = Core.Contains(key) || CoreControllers.Contains(controller.Name)
                         || (controller.Name == "ChildReadingController" && expected is null);
            if (expected is not null && feature != expected) problems.Add($"{key}: expected {expected}, got {feature ?? "none"}");
            if (expected is null && !isCore) problems.Add($"{key}: not classified as core or gated");
            if (expected is null && isCore && feature is not null) problems.Add($"{key}: core action must not be gated");
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Turning_off_library_browse_never_blocks_reading_a_story()
    {
        var reading = typeof(ChildReadingController);
        foreach (var name in new[] { "Start", "GetContent", "Complete" })
        {
            var method = reading.GetMethod(name)!;
            Assert.Null(method.GetCustomAttribute<RequireChildFeatureAttribute>());
        }
        Assert.Null(reading.GetCustomAttribute<RequireChildFeatureAttribute>());
    }

    private static IEnumerable<(Type Controller, MethodInfo Action)> ChildActions() =>
        typeof(BaseApiController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .Where(m => (t.GetCustomAttributes<AuthorizeAttribute>(true).Concat(m.GetCustomAttributes<AuthorizeAttribute>(true)))
                    .Any(a => a.Policy == ServiceExtensions.ChildSessionPolicy))
                .Select(m => (t, m)));

    private static (ActionExecutingContext, bool[]) ActionContext(int childId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, childId.ToString()), new Claim("token_type", "child")], "TestAuth"))
        };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return (new ActionExecutingContext(action, [], new Dictionary<string, object?>(), controller: new object()), [false]);
    }

    private sealed class FakeFeatures(bool enabled) : IChildFeatureService
    {
        public Task<bool> IsEnabledAsync(int childProfileId, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(enabled);
        public Task<IReadOnlyList<string>> GetDisabledAsync(int childProfileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
