using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.DTOs;
using StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ClassGroups.Services;

public class ClassGroupService : IClassGroupService
{
    private readonly IUnitOfWork _unitOfWork;

    public ClassGroupService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ClassGroupDto> CreateClassGroupAsync(
        int teacherUserId, CreateClassGroupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150)
        {
            throw new BadRequestException("Tên lớp phải từ 1 đến 150 ký tự.");
        }

        var classGroup = new ClassGroup
        {
            TeacherUserId = teacherUserId,
            Name = request.Name.Trim(),
            Status = ClassGroupStatus.Active,
            OrganizationId = request.OrganizationId
        };

        await _unitOfWork.Repository<ClassGroup>().AddAsync(classGroup, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(classGroup);
    }

    public async Task AddChildToClassGroupAsync(
        int classGroupId, int childProfileId, int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var classGroup = await _unitOfWork.Repository<ClassGroup>()
            .GetByIdAsync(classGroupId, cancellationToken);
        if (classGroup == null)
        {
            throw new NotFoundException("Class Group", classGroupId);
        }

        if (classGroup.TeacherUserId != currentUserId)
        {
            throw new ForbiddenException(
                "Chỉ giáo viên phụ trách lớp mới có quyền thêm học sinh.");
        }

        if (classGroup.Status != ClassGroupStatus.Active)
        {
            throw new BadRequestException("Không thể thêm học sinh vào Class Group đã lưu trữ.");
        }

        var childProfile = await _unitOfWork.Repository<ChildProfile>()
            .GetByIdAsync(childProfileId, cancellationToken);
        if (childProfile == null)
        {
            throw new NotFoundException("Hồ sơ trẻ", childProfileId);
        }

        if (childProfile.Status != ChildProfileStatus.Active)
        {
            throw new BadRequestException(
                "Hồ sơ trẻ phải ở trạng thái Active trước khi thêm vào Class Group.");
        }

        var memberRepo = _unitOfWork.Repository<ClassGroupMember>();
        var alreadyMember = await memberRepo.ExistsAsync(
            value => value.ClassGroupId == classGroupId
                     && value.ChildProfileId == childProfileId,
            cancellationToken);
        if (alreadyMember)
        {
            throw new BadRequestException("Hồ sơ trẻ đã thuộc Class Group này.");
        }

        await memberRepo.AddAsync(new ClassGroupMember
        {
            ClassGroupId = classGroupId,
            ChildProfileId = childProfileId,
            JoinedAt = DateTime.UtcNow
        }, cancellationToken);

        // Backfill các story đã được duyệt cho Owner của hồ sơ mới.
        var approvedShares = await _unitOfWork.Repository<SharedStory>().FindAsync(
            value => value.ClassGroupId == classGroupId
                     && value.TeacherStatus == TeacherShareStatus.Approved,
            cancellationToken: cancellationToken);
        var recipientRepo = _unitOfWork.Repository<SharedStoryRecipient>();
        foreach (var sharedStory in approvedShares)
        {
            var alreadyRecipient = await recipientRepo.ExistsAsync(
                value => value.SharedStoryId == sharedStory.Id
                         && value.RecipientUserId == childProfile.OwnerUserId,
                cancellationToken);
            if (!alreadyRecipient)
            {
                await recipientRepo.AddAsync(new SharedStoryRecipient
                {
                    SharedStoryId = sharedStory.Id,
                    RecipientUserId = childProfile.OwnerUserId,
                    Status = RecipientStatus.Pending
                }, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClassGroupDto> GetClassGroupByIdAsync(
        int classGroupId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var classGroup = await _unitOfWork.Repository<ClassGroup>().GetByIdAsync(classGroupId, cancellationToken);
        if (classGroup == null)
        {
            throw new NotFoundException("Class Group", classGroupId);
        }

        if (classGroup.TeacherUserId != currentUserId)
        {
            throw new ForbiddenException("Chỉ giáo viên phụ trách lớp mới có quyền xem thông tin lớp.");
        }

        return MapToDto(classGroup);
    }

    public async Task<List<ClassGroupDto>> ListMyClassGroupsAsync(
        int teacherUserId, CancellationToken cancellationToken = default)
    {
        var groups = await _unitOfWork.Repository<ClassGroup>().FindAsync(
            value => value.TeacherUserId == teacherUserId, cancellationToken: cancellationToken);

        return groups.Select(MapToDto).ToList();
    }

    public async Task<ClassGroupDto> UpdateClassGroupAsync(
        int classGroupId, int currentUserId, UpdateClassGroupRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var classGroupRepo = _unitOfWork.Repository<ClassGroup>();
        var classGroup = await classGroupRepo.GetByIdAsync(classGroupId, cancellationToken);
        if (classGroup == null)
        {
            throw new NotFoundException("Class Group", classGroupId);
        }

        if (classGroup.TeacherUserId != currentUserId)
        {
            throw new ForbiddenException("Chỉ giáo viên phụ trách lớp mới có quyền sửa thông tin lớp.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 150)
        {
            throw new BadRequestException("Tên lớp phải từ 1 đến 150 ký tự.");
        }

        classGroup.Name = request.Name.Trim();
        classGroup.UpdatedAt = DateTime.UtcNow;
        classGroupRepo.Update(classGroup);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(classGroup);
    }

    public async Task ArchiveClassGroupAsync(
        int classGroupId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var classGroupRepo = _unitOfWork.Repository<ClassGroup>();
        var classGroup = await classGroupRepo.GetByIdAsync(classGroupId, cancellationToken);
        if (classGroup == null)
        {
            throw new NotFoundException("Class Group", classGroupId);
        }

        if (classGroup.TeacherUserId != currentUserId)
        {
            throw new ForbiddenException("Chỉ giáo viên phụ trách lớp mới có quyền lưu trữ lớp.");
        }

        if (classGroup.Status == ClassGroupStatus.Archived)
        {
            return;
        }

        classGroup.Status = ClassGroupStatus.Archived;
        classGroup.UpdatedAt = DateTime.UtcNow;
        classGroupRepo.Update(classGroup);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveChildFromClassGroupAsync(
        int classGroupId, int childProfileId, int currentUserId,
        CancellationToken cancellationToken = default)
    {
        var classGroup = await _unitOfWork.Repository<ClassGroup>().GetByIdAsync(classGroupId, cancellationToken);
        if (classGroup == null)
        {
            throw new NotFoundException("Class Group", classGroupId);
        }

        if (classGroup.TeacherUserId != currentUserId)
        {
            throw new ForbiddenException("Chỉ giáo viên phụ trách lớp mới có quyền gỡ học sinh.");
        }

        var memberRepo = _unitOfWork.Repository<ClassGroupMember>();
        var membership = await memberRepo.FirstOrDefaultAsync(
            value => value.ClassGroupId == classGroupId && value.ChildProfileId == childProfileId,
            cancellationToken: cancellationToken);

        if (membership != null)
        {
            // Xoá cứng — bảng class_group_members chỉ phản ánh thành viên HIỆN TẠI, không mang lịch sử.
            memberRepo.Delete(membership);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<ChildProfileDto>> ListClassGroupMembersAsync(
        int classGroupId, int currentUserId, CancellationToken cancellationToken = default)
    {
        var classGroup = await _unitOfWork.Repository<ClassGroup>().GetByIdAsync(classGroupId, cancellationToken);
        if (classGroup == null)
        {
            throw new NotFoundException("Class Group", classGroupId);
        }

        if (classGroup.TeacherUserId != currentUserId)
        {
            throw new ForbiddenException("Chỉ giáo viên phụ trách lớp mới có quyền xem danh sách thành viên.");
        }

        var memberships = await _unitOfWork.Repository<ClassGroupMember>().FindAsync(
            value => value.ClassGroupId == classGroupId, cancellationToken: cancellationToken);

        var childProfileRepo = _unitOfWork.Repository<ChildProfile>();
        var result = new List<ChildProfileDto>();
        foreach (var membership in memberships)
        {
            var childProfile = await childProfileRepo.GetByIdAsync(membership.ChildProfileId, cancellationToken);
            if (childProfile != null)
            {
                result.Add(new ChildProfileDto
                {
                    Id = childProfile.Id,
                    OwnerUserId = childProfile.OwnerUserId,
                    Nickname = childProfile.Nickname,
                    AgeBand = childProfile.AgeBand.ToString(),
                    Language = childProfile.Language,
                    Status = childProfile.Status.ToString(),
                    Scope = childProfile.Scope.ToString(),
                    OrganizationId = childProfile.OrganizationId,
                    CreatedAt = childProfile.CreatedAt
                });
            }
        }

        return result;
    }

    private static ClassGroupDto MapToDto(ClassGroup classGroup) => new()
    {
        Id = classGroup.Id,
        TeacherUserId = classGroup.TeacherUserId,
        Name = classGroup.Name,
        Status = classGroup.Status.ToString(),
        OrganizationId = classGroup.OrganizationId
    };
}
