using System.Reflection;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Services;
using Xunit;

namespace StoryPlatform.UnitTests.Features.AccessControl;

public class PermissionCatalogTests
{
    [Fact]
    public void Codes_are_unique_and_well_formed()
    {
        var codes = PermissionCatalog.All.Select(p => p.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches(@"^[a-z_]+(\.[a-z_]+){1,2}$", code));
    }

    [Fact]
    public void Constants_match_catalog()
    {
        var constants = typeof(Permissions).GetNestedTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(constants, PermissionCatalog.All.Select(p => p.Code).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Staff_grantable_only_in_staff_groups()
    {
        Assert.All(PermissionCatalog.All, p =>
            Assert.Equal(p.Group is PermissionCatalog.ModerationGroup or PermissionCatalog.AdminGroup, p.IsStaffGrantable));
    }

    [Fact]
    public void Every_permission_has_vietnamese_description()
    {
        Assert.All(PermissionCatalog.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
    }

    [Fact]
    public void Default_map_uses_known_roles_and_codes()
    {
        var codes = PermissionCatalog.All.Select(p => p.Code).ToHashSet();
        Assert.All(DefaultRolePermissions.Map, pair =>
        {
            Assert.Contains(pair.Key, RoleCodes.All);
            Assert.All(pair.Value, code => Assert.Contains(code, codes));
        });
    }

    [Fact]
    public void Every_permission_is_granted_to_some_role()
    {
        var granted = DefaultRolePermissions.Map.Values.SelectMany(x => x).ToHashSet();
        Assert.All(PermissionCatalog.All, p => Assert.Contains(p.Code, granted));
    }

    [Fact]
    public void Administrator_has_permission_manage()
    {
        Assert.Contains(Permissions.Admin.PermissionManage, DefaultRolePermissions.Map[RoleCodes.Administrator]);
    }

    [Fact]
    public void Staff_roles_only_hold_staff_or_shared_defaults_they_had_before()
    {
        // Không role nào ngoài nhân viên nhận permission có thể gán cho nhân viên.
        var grantable = PermissionCatalog.All.Where(p => p.IsStaffGrantable).Select(p => p.Code).ToHashSet();
        foreach (var role in new[] { RoleCodes.User, RoleCodes.ContentCreator, RoleCodes.CreatorApplicant })
            Assert.DoesNotContain(DefaultRolePermissions.Map[role], grantable.Contains);
    }

    [Fact]
    public async Task Static_service_answers_from_default_map()
    {
        var service = new StaticPermissionService();
        Assert.True(await service.RoleHasAsync(RoleCodes.Moderator, Permissions.Review.GrayZone));
        Assert.False(await service.RoleHasAsync(RoleCodes.User, Permissions.Review.GrayZone));
        Assert.False(await service.RoleHasAsync("Ghost", Permissions.Review.GrayZone));
        Assert.Empty(await service.GetForRoleAsync("Ghost"));
    }
}
