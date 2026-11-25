using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Domain.Entities;

namespace StoryPlatform.Application.Features.AccessControl.Services;

/// <summary>
/// Đồng bộ danh mục role/permission trong code vào DB lúc khởi động. Idempotent; không bao giờ ghi đè quyền
/// Administrator đã chỉnh: bản gán mặc định chỉ được ghi cho role chưa có quyền nào.
/// </summary>
public sealed class PermissionCatalogSeeder(IUnitOfWork uow)
{
    private static readonly IReadOnlyDictionary<string, string> RoleNames = new Dictionary<string, string>
    {
        [RoleCodes.User] = "Phụ huynh",
        [RoleCodes.ContentCreator] = "Nhà sáng tạo",
        [RoleCodes.CreatorApplicant] = "Người đăng ký sáng tạo",
        [RoleCodes.Moderator] = "Kiểm duyệt viên",
        [RoleCodes.Administrator] = "Quản trị viên",
    };

    /// <summary>Trả về số dòng đã thêm hoặc sửa.</summary>
    public async Task<int> SeedAsync(CancellationToken ct)
    {
        var changes = 0;
        var roleRepo = uow.Repository<Role>();
        var permissionRepo = uow.Repository<Permission>();

        var roles = (await roleRepo.GetAllAsync(ct)).ToList();
        foreach (var code in RoleCodes.All.Where(code => roles.All(r => r.Code != code)))
        {
            var role = new Role { Code = code, Name = RoleNames[code], IsStaff = RoleCodes.Staff.Contains(code), IsSystem = true };
            await roleRepo.AddAsync(role, ct);
            roles.Add(role);
            changes++;
        }

        var permissions = (await permissionRepo.GetAllAsync(ct)).ToList();
        foreach (var definition in PermissionCatalog.All)
        {
            var existing = permissions.FirstOrDefault(p => p.Code == definition.Code);
            if (existing is null)
            {
                var permission = new Permission
                {
                    Code = definition.Code, Group = definition.Group,
                    Description = definition.Description, IsStaffGrantable = definition.IsStaffGrantable
                };
                await permissionRepo.AddAsync(permission, ct);
                permissions.Add(permission);
                changes++;
            }
            else if (existing.Group != definition.Group || existing.Description != definition.Description
                     || existing.IsStaffGrantable != definition.IsStaffGrantable || existing.IsDeleted)
            {
                existing.Group = definition.Group;
                existing.Description = definition.Description;
                existing.IsStaffGrantable = definition.IsStaffGrantable;
                existing.IsDeleted = false;
                existing.UpdatedAt = DateTime.UtcNow;
                permissionRepo.Update(existing);
                changes++;
            }
        }

        foreach (var stale in permissions.Where(p => !p.IsDeleted && PermissionCatalog.Find(p.Code) is null))
        {
            stale.IsDeleted = true;
            stale.UpdatedAt = DateTime.UtcNow;
            permissionRepo.Update(stale);
            changes++;
        }

        await uow.SaveChangesAsync(ct);

        var linkRepo = uow.Repository<RolePermission>();
        var rolesWithLinks = (await linkRepo.GetAllAsync(ct)).Select(x => x.RoleId).ToHashSet();
        foreach (var role in roles.Where(r => !rolesWithLinks.Contains(r.Id) && DefaultRolePermissions.Map.ContainsKey(r.Code)))
        {
            var links = DefaultRolePermissions.Map[role.Code]
                .Select(code => new RolePermission { RoleId = role.Id, PermissionId = permissions.Single(p => p.Code == code).Id });
            await linkRepo.AddRangeAsync(links, ct);
            changes += DefaultRolePermissions.Map[role.Code].Count;
        }

        await uow.SaveChangesAsync(ct);
        return changes;
    }
}
