using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using StoryPlatform.Api.Controllers;
using Xunit;

namespace StoryPlatform.UnitTests;

/// <summary>Giữ tài liệu bàn giao không lệch khỏi controller và danh sách mã lỗi.</summary>
public sealed class AuthoringGuideManagementDocsTests
{
    private const string ApiGuide = "docs/AUTHORING_GUIDE_MANAGEMENT_API_GUIDE.md";

    private static readonly string[] ErrorCodes =
    [
        "GUIDE_REVISION_TOKEN_REQUIRED", "GUIDE_REVISION_CONFLICT", "GUIDE_DRAFT_EXISTS", "GUIDE_DRAFT_NOT_FOUND",
        "GUIDE_ITEM_NOT_FOUND", "GUIDE_SOURCE_VERSION_INVALID", "GUIDE_CATALOG_EMPTY", "GUIDE_CATALOG_UNAVAILABLE",
        "GUIDE_CATALOG_CHANGED", "PAGINATION_INVALID", "WRITING_PURPOSE_INVALID", "GUIDE_ID_DUPLICATED", "GUIDE_ID_INVALID",
        "GUIDE_FIELD_REQUIRED", "GUIDE_AUDIENCE_INVALID", "GUIDE_PURPOSE_INVALID", "GUIDE_GROUP_INVALID",
        "GUIDE_ITEM_TYPE_INVALID", "GUIDE_QUESTION_HAS_TEMPLATE", "GUIDE_TEMPLATE_REQUIRED", "GUIDE_PLACEHOLDERS_MISMATCH",
        "PLACEHOLDER_VALUES_INVALID", "GUIDE_HAS_NO_TEMPLATE", "GUIDE_PLACEHOLDER_LABEL_INVALID"
    ];

    private static readonly string[] Terms =
    [
        "revisionToken", "catalogVersion", "sourceVersionId", "discardedCatalogVersion", "itemCount", "parent-vi-1",
        "Moderator", "Administrator", "/api/v1/authoring-guides"
    ];

    internal static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StoryPlatform.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    internal static IReadOnlyList<string> ControllerRoutes()
    {
        var type = typeof(AuthoringGuideManagementController);
        // The controller declares its own route, overriding BaseApiController's route.
        var baseRoute = type.GetCustomAttribute<RouteAttribute>(inherit: false)!.Template;
        var routes = new List<string>();
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            foreach (var attribute in method.GetCustomAttributes().OfType<HttpMethodAttribute>())
            {
                var template = string.IsNullOrEmpty(attribute.Template) ? "" : "/" + Regex.Replace(attribute.Template, @"\{(\w+):\w+\}", "{$1}");
                routes.Add($"{attribute.HttpMethods.Single()} /{baseRoute}{template}");
            }
        return routes;
    }

    [Fact]
    public void Controller_exposes_the_nine_documented_endpoints()
    {
        var routes = ControllerRoutes();
        Assert.Equal(9, routes.Count);
        Assert.Contains("DELETE /api/v1/moderation/authoring-guides/draft", routes);
    }

    [Fact]
    public void Api_guide_documents_every_controller_route()
    {
        var text = File.ReadAllText(RepoFile(ApiGuide));
        foreach (var route in ControllerRoutes()) Assert.Contains($"`{route}`", text);
    }

    [Fact]
    public void Api_guide_documents_every_error_code_and_key_term()
    {
        var text = File.ReadAllText(RepoFile(ApiGuide));
        foreach (var code in ErrorCodes) Assert.True(text.Contains(code), $"API guide is missing error code: {code}");
        foreach (var term in Terms) Assert.True(text.Contains(term), $"API guide is missing term: {term}");
    }

    private const string SwaggerGuide = "docs/AUTHORING_GUIDE_MANAGEMENT_SWAGGER_TEST.md";

    [Fact]
    public void Swagger_test_guide_covers_every_management_route_and_the_parent_side()
    {
        var text = File.ReadAllText(RepoFile(SwaggerGuide));
        foreach (var route in ControllerRoutes()) Assert.Contains($"`{route}`", text);
        Assert.Contains("GET /api/v1/authoring-guides", text);
        Assert.Contains("POST /api/v1/authoring-guides/{guideId}/render", text);
        Assert.Contains("## Kết quả chạy thật", text);
    }

    [Fact]
    public void Existing_parent_docs_describe_the_database_backed_catalog()
    {
        var parentGuide = File.ReadAllText(RepoFile("docs/PARENT_MANUAL_API_GUIDE.md"));
        Assert.Contains("catalogVersion", parentGuide);
        Assert.Contains("GUIDE_CATALOG_CHANGED", parentGuide);
        Assert.DoesNotContain("catalog `parent-vi-1` gồm 12 câu hỏi và 18 mẫu diễn đạt. Cần rà soát", parentGuide);

        var actorFlows = File.ReadAllText(RepoFile("docs/API_FRONTEND_ACTOR_FLOWS.md"));
        Assert.Contains("/api/v1/moderation/authoring-guides", actorFlows);
        Assert.Contains("catalogVersion", actorFlows);
    }
}
