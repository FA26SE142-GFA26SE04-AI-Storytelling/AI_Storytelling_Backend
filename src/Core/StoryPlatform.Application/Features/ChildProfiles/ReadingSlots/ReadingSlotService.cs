using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.ChildProfiles.Sessions.Interfaces;
using StoryPlatform.Application.Features.PlanBenefits.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.ReadingSlots;

/// <summary>
/// Khi số hồ sơ Active vượt số hồ sơ tối đa của gói (thường là lúc về Miễn phí), chỉ một nhóm hồ sơ được đọc:
/// hồ sơ phụ huynh đã chọn, rồi hồ sơ có phiên đọc gần nhất, rồi hồ sơ tạo sớm nhất. Các hồ sơ khác chỉ xem
/// (trạng thái không đổi). Phụ huynh đổi lựa chọn tối đa 1 lần mỗi tuần giờ Việt Nam (Luồng 1, 1e).
/// </summary>
public class ReadingSlotService(
    IUnitOfWork unitOfWork, IPlanBenefitService planBenefits, IChildSessionRevoker sessionRevoker,
    IAuditLogWriter auditLogWriter, TimeProvider? clock = null) : IReadingSlotService
{
    public const string ViewOnlyMessage =
        "Hồ sơ đang ở chế độ chỉ xem. Phụ huynh có thể chọn hồ sơ được đọc hoặc nâng cấp gói.";

    /// <summary>Mã lý do dùng trong danh sách "Cho bé đọc truyện này".</summary>
    public const string ViewOnlySkipCode = "PROFILE_VIEW_ONLY";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<ReadingSlotsDto> GetAsync(int ownerUserId, CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(ownerUserId, cancellationToken);
        return ToDto(state, NextChangeAllowedAt(await LastChangedAtAsync(ownerUserId, cancellationToken)));
    }

    public async Task<ReadingSlotsDto> SelectAsync(
        int ownerUserId, IReadOnlyList<int> childProfileIds, CancellationToken cancellationToken = default)
    {
        _ = await unitOfWork.Repository<UserAccount>().GetByIdAsync(ownerUserId, cancellationToken)
            ?? throw new NotFoundException("Tài khoản", ownerUserId);
        var state = await LoadAsync(ownerUserId, cancellationToken);
        var chosen = (childProfileIds ?? []).Distinct().ToList();
        if (chosen.Count == 0 || chosen.Count > state.Slots)
        {
            throw new BadRequestException($"Chọn từ 1 đến {state.Slots} hồ sơ được đọc.");
        }

        var activeById = state.Active.ToDictionary(profile => profile.Id);
        if (chosen.Any(id => !activeById.ContainsKey(id)))
        {
            throw new BadRequestException("Chỉ chọn được hồ sơ trẻ đang hoạt động của chính tài khoản.");
        }

        var nextChange = NextChangeAllowedAt(await LastChangedAtAsync(ownerUserId, cancellationToken));
        if (nextChange != null)
        {
            throw new ConflictException(
                $"Chỉ được đổi hồ sơ được đọc 1 lần mỗi tuần. Lần đổi tiếp theo: {VietnamTime.LocalDate(nextChange.Value):dd/MM/yyyy}.");
        }

        var now = Now;
        var before = state.Readable.Order().ToList();
        foreach (var profile in state.Active)
        {
            profile.ReadingSlotSelectedAt = chosen.Contains(profile.Id) ? now : null;
            unitOfWork.Repository<ChildProfile>().Update(profile);
        }

        var after = await LoadAsync(ownerUserId, cancellationToken);
        foreach (var profile in after.Active.Where(profile => !after.Readable.Contains(profile.Id)))
        {
            await sessionRevoker.RevokeByChildProfileAsync(profile.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await auditLogWriter.LogAsync(
            ownerUserId, "ReadingSlotsChanged", nameof(UserAccount), ownerUserId,
            new { readableChildProfileIds = before }, new { readableChildProfileIds = after.Readable.Order().ToList(), selected = chosen },
            cancellationToken);

        return ToDto(after, NextChangeAllowedAt(now));
    }

    public async Task<IReadOnlySet<int>> GetReadableProfileIdsAsync(int ownerUserId, CancellationToken cancellationToken = default) =>
        (await LoadAsync(ownerUserId, cancellationToken)).Readable;

    public async Task EnsureCanReadAsync(int childProfileId, CancellationToken cancellationToken = default)
    {
        var profile = await unitOfWork.Repository<ChildProfile>().GetByIdAsync(childProfileId, cancellationToken);
        if (profile == null)
        {
            return;   // Hồ sơ không tồn tại / không Active do các kiểm tra sẵn có xử lý.
        }

        if (!(await GetReadableProfileIdsAsync(profile.OwnerUserId, cancellationToken)).Contains(childProfileId))
        {
            throw new ForbiddenException(ViewOnlyMessage);
        }
    }

    private sealed record SlotState(int Slots, IReadOnlyList<ChildProfile> Active, HashSet<int> Readable);

    private async Task<SlotState> LoadAsync(int ownerUserId, CancellationToken cancellationToken)
    {
        var slots = (await planBenefits.GetForUserAsync(ownerUserId, cancellationToken)).MaxChildProfiles;
        var active = (await unitOfWork.Repository<ChildProfile>().FindAsync(
                profile => profile.OwnerUserId == ownerUserId && !profile.IsDeleted && profile.Status == ChildProfileStatus.Active,
                cancellationToken: cancellationToken))
            .OrderBy(profile => profile.Id)
            .ToList();
        if (active.Count <= slots)
        {
            return new SlotState(slots, active, active.Select(profile => profile.Id).ToHashSet());
        }

        var ids = active.Select(profile => profile.Id).ToList();
        var lastReadAt = (await unitOfWork.Repository<ReadingSession>().FindAsync(
                session => ids.Contains(session.ChildProfileId), cancellationToken: cancellationToken))
            .GroupBy(session => session.ChildProfileId)
            .ToDictionary(group => group.Key, group => group.Max(session => session.StartedAt));

        var readable = active
            .OrderByDescending(profile => profile.ReadingSlotSelectedAt.HasValue)
            .ThenByDescending(profile => profile.ReadingSlotSelectedAt)
            .ThenByDescending(profile => lastReadAt.TryGetValue(profile.Id, out var at) ? at : DateTime.MinValue)
            .ThenBy(profile => profile.CreatedAt)
            .ThenBy(profile => profile.Id)
            .Take(Math.Max(slots, 0))
            .Select(profile => profile.Id)
            .ToHashSet();
        return new SlotState(slots, active, readable);
    }

    /// <summary>
    /// Lần đổi gần nhất = ReadingSlotSelectedAt lớn nhất trên các hồ sơ của tài khoản (mỗi lần chọn đặt mốc này
    /// cho hồ sơ được chọn), nên không cần cột riêng trên tài khoản.
    /// </summary>
    private async Task<DateTime?> LastChangedAtAsync(int ownerUserId, CancellationToken cancellationToken) =>
        (await unitOfWork.Repository<ChildProfile>().FindAsync(
            profile => profile.OwnerUserId == ownerUserId && profile.ReadingSlotSelectedAt != null,
            cancellationToken: cancellationToken))
        .Max(profile => profile.ReadingSlotSelectedAt);

    /// <summary>Đã đổi trong tuần hiện tại → 00:00 thứ Hai tuần sau; chưa → null (đổi được ngay).</summary>
    private DateTime? NextChangeAllowedAt(DateTime? lastChangedAt)
    {
        var weekStart = VietnamTime.StartOfWeekUtc(Now);
        return lastChangedAt >= weekStart ? VietnamTime.StartOfNextWeekUtc(Now) : null;
    }

    private static ReadingSlotsDto ToDto(SlotState state, DateTime? nextChangeAllowedAt) => new()
    {
        Slots = state.Slots,
        IsOverLimit = state.Active.Count > state.Slots,
        NextChangeAllowedAt = nextChangeAllowedAt,
        Profiles = state.Active.Select(profile => new ReadingSlotProfileDto
        {
            ChildProfileId = profile.Id,
            Nickname = profile.Nickname,
            CanRead = state.Readable.Contains(profile.Id),
            IsSelected = profile.ReadingSlotSelectedAt != null
        }).ToList()
    };
}
