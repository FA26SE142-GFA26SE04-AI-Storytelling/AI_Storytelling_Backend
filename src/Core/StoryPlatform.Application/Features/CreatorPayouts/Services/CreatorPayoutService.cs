using System.Text.Json;
using StoryPlatform.Application.Abstractions.Persistence;
using StoryPlatform.Application.Common.Exceptions;
using StoryPlatform.Application.Common.Time;
using StoryPlatform.Application.Features.AuditLogs.Interfaces;
using StoryPlatform.Application.Features.CreatorPayouts.DTOs;
using StoryPlatform.Application.Features.CreatorPayouts.Interfaces;
using StoryPlatform.Application.Features.CreatorStatistics.Interfaces;
using StoryPlatform.Application.Features.Notifications.Interfaces;
using StoryPlatform.Domain.Entities;
using StoryPlatform.Domain.Enums;
using StoryPlatform.Application.Features.AccessControl;
using StoryPlatform.Application.Features.AccessControl.Interfaces;
using StoryPlatform.Application.Features.AccessControl.Services;

namespace StoryPlatform.Application.Features.CreatorPayouts.Services;

/// <summary>
/// Chi trả cho Creator (Luồng 5, 5h). Số dư không lưu thành cột mà là tổng các bút toán trong creator_balance_entries,
/// nên mọi thay đổi số dư đều truy được về kỳ đã chốt hoặc lần chi. Ghi lần chi và huỷ lần chi giữ khoá theo Creator để
/// hai lần bấm Đã chi cùng lúc không chi trùng.
/// </summary>
public class CreatorPayoutService : ICreatorPayoutService
{
    /// <summary>Khoá chốt kỳ: số âm nhỏ nhất, không trùng khoá theo tài khoản (dương) hay theo hồ sơ trẻ (âm, gần 0).</summary>
    private const int PeriodCloseLockId = int.MinValue;

    private const string MissingAccountReminder =
        "Bạn chưa nhập thông tin nhận tiền. Hãy nhập ngân hàng, số tài khoản và tên chủ tài khoản để được chi trả.";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICreatorStatisticsService _statistics;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly INotificationService _notificationService;

    private readonly IAccessPolicy _accessPolicy;

    public CreatorPayoutService(
        IUnitOfWork unitOfWork, ICreatorStatisticsService statistics, IAuditLogWriter auditLogWriter,
        INotificationService notificationService,
        IAccessPolicy? accessPolicy = null)
    {
        _accessPolicy = accessPolicy ?? AccessPolicy.Default(unitOfWork);
        _unitOfWork = unitOfWork;
        _statistics = statistics;
        _auditLogWriter = auditLogWriter;
        _notificationService = notificationService;
    }

    public async Task<CreatorPayoutOverviewDto> GetOverviewAsync(int creatorUserId, CancellationToken cancellationToken = default)
    {
        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        var account = await FindAccountAsync(creator.Id, cancellationToken);
        var entries = await _unitOfWork.Repository<CreatorBalanceEntry>().FindAsync(
            entry => entry.CreatorUserId == creator.Id, cancellationToken: cancellationToken);
        var periodIds = entries.Where(entry => entry.EarningPeriodId != null)
            .Select(entry => entry.EarningPeriodId!.Value)
            .Distinct()
            .ToList();
        var periods = (await _unitOfWork.Repository<CreatorEarningPeriod>().FindAsync(
                period => periodIds.Contains(period.Id), cancellationToken: cancellationToken))
            .ToDictionary(period => period.Id, period => period.PeriodMonth);
        var reversals = entries
            .Where(entry => entry.Type == CreatorBalanceEntryType.PayoutReversal && entry.ReversedEntryId != null)
            .ToDictionary(entry => entry.ReversedEntryId!.Value);

        return new CreatorPayoutOverviewDto
        {
            CreatorUserId = creator.Id,
            Username = creator.Username,
            FullName = creator.FullName,
            BalanceVnd = entries.Sum(entry => entry.AmountVnd),
            MinimumPayoutVnd = ICreatorPayoutService.MinimumPayoutVnd,
            HasPayoutAccount = account != null,
            Reminder = account == null ? MissingAccountReminder : null,
            PayoutAccount = account == null ? null : ToDto(account),
            Payouts = entries
                .Where(entry => entry.Type == CreatorBalanceEntryType.Payout)
                .OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id)
                .Select(entry => ToDto(entry, reversals.GetValueOrDefault(entry.Id)))
                .ToList(),
            Earnings = entries
                .Where(entry => entry.Type == CreatorBalanceEntryType.PeriodEarning && entry.EarningPeriodId != null)
                .Select(entry => new CreatorPeriodEarningEntryDto
                {
                    PeriodMonth = periods.GetValueOrDefault(entry.EarningPeriodId!.Value),
                    ValidReads = entry.ValidReads ?? 0,
                    AmountVnd = entry.AmountVnd,
                    CreditedAt = entry.CreatedAt
                })
                .OrderByDescending(entry => entry.PeriodMonth)
                .ToList()
        };
    }

    public async Task<CreatorPayoutAccountDto> UpdatePayoutAccountAsync(
        int creatorUserId, UpdateCreatorPayoutAccountRequestDto request, CancellationToken cancellationToken = default)
    {
        var bankName = request.BankName?.Trim() ?? string.Empty;
        var accountNumber = new string((request.AccountNumber ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        var holder = request.AccountHolderName?.Trim() ?? string.Empty;
        if (bankName.Length is < 2 or > 100)
        {
            throw new BadRequestException("Tên ngân hàng từ 2 đến 100 ký tự.");
        }

        if (accountNumber.Length is < 6 or > 20 || !accountNumber.All(char.IsAsciiDigit))
        {
            throw new BadRequestException("Số tài khoản chỉ gồm chữ số (6 đến 20 số).");
        }

        if (holder.Length is < 2 or > 100)
        {
            throw new BadRequestException("Tên chủ tài khoản từ 2 đến 100 ký tự.");
        }

        var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
        var accounts = _unitOfWork.Repository<CreatorPayoutAccount>();
        var account = await FindAccountAsync(creator.Id, cancellationToken);
        var before = account == null ? null : AuditState(account);
        var now = DateTime.UtcNow;
        if (account == null)
        {
            account = new CreatorPayoutAccount { CreatorUserId = creator.Id, CreatedAt = now };
            await accounts.AddAsync(account, cancellationToken);
        }
        else
        {
            accounts.Update(account);
        }

        account.BankName = bankName;
        account.AccountNumber = accountNumber;
        account.AccountHolderName = holder;
        account.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Audit log ghi số tài khoản đã che, không ghi đủ số.
        await _auditLogWriter.LogAsync(
            creator.Id, "CreatorPayoutAccountUpdated", nameof(CreatorPayoutAccount), account.Id,
            before, AuditState(account), cancellationToken);
        return ToDto(account);
    }

    public async Task<IReadOnlyList<CreatorPayoutDueDto>> GetDueAsync(CancellationToken cancellationToken = default)
    {
        var balances = (await _unitOfWork.Repository<CreatorBalanceEntry>().GetAllAsync(cancellationToken))
            .GroupBy(entry => entry.CreatorUserId)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.AmountVnd));
        var dueIds = balances.Where(pair => pair.Value >= ICreatorPayoutService.MinimumPayoutVnd)
            .Select(pair => pair.Key)
            .ToList();
        if (dueIds.Count == 0)
        {
            return [];
        }

        var creators = (await _unitOfWork.Repository<UserAccount>().FindAsync(
                user => dueIds.Contains(user.Id) && user.Role == UserRole.ContentCreator
                        && user.Status == AccountStatus.Active && !user.IsDeleted,
                cancellationToken: cancellationToken))
            .ToDictionary(user => user.Id);
        var accounts = await _unitOfWork.Repository<CreatorPayoutAccount>().FindAsync(
            account => dueIds.Contains(account.CreatorUserId), cancellationToken: cancellationToken);

        return accounts
            .Where(account => creators.ContainsKey(account.CreatorUserId))
            .Select(account =>
            {
                var creator = creators[account.CreatorUserId];
                return new CreatorPayoutDueDto
                {
                    CreatorUserId = creator.Id,
                    Username = creator.Username,
                    FullName = creator.FullName,
                    BalanceVnd = balances[creator.Id],
                    BankName = account.BankName,
                    AccountNumber = account.AccountNumber,
                    AccountHolderName = account.AccountHolderName
                };
            })
            .OrderByDescending(row => row.BalanceVnd).ThenBy(row => row.CreatorUserId)
            .ToList();
    }

    public async Task<CreatorPayoutDto> MarkPaidAsync(
        int adminUserId, int creatorUserId, MarkCreatorPayoutPaidRequestDto request, CancellationToken cancellationToken = default)
    {
        var code = request.BankTransactionCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            throw new BadRequestException("Cần nhập mã giao dịch ngân hàng.");
        }

        if (code.Length > 100)
        {
            throw new BadRequestException("Mã giao dịch tối đa 100 ký tự.");
        }

        if (request.TransferredOn is not { } transferredOn)
        {
            throw new BadRequestException("Cần nhập ngày chuyển khoản.");
        }

        var transferDate = DateTime.SpecifyKind(transferredOn.Date, DateTimeKind.Utc);
        if (transferDate > VietnamTime.LocalDate(DateTime.UtcNow))
        {
            throw new BadRequestException("Ngày chuyển khoản không được sau hôm nay.");
        }

        await RequireActiveAdminAsync(adminUserId, cancellationToken);
        await RequireCreatorAsync(creatorUserId, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(creatorUserId, cancellationToken);
            // Đọc lại sau khi có khoá: lần bấm trước có thể vừa ghi lần chi.
            _unitOfWork.DiscardTrackedChanges();
            var creator = await RequireCreatorAsync(creatorUserId, cancellationToken);
            if (creator.Status != AccountStatus.Active)
            {
                throw new ConflictException("Nhà sáng tạo đang bị vô hiệu hoá; số dư được giữ đến khi tài khoản được kích hoạt lại.");
            }

            var account = await FindAccountAsync(creator.Id, cancellationToken)
                          ?? throw new ConflictException("Nhà sáng tạo chưa nhập thông tin nhận tiền.");
            var balance = await GetBalanceAsync(creator.Id, cancellationToken);
            if (balance <= 0)
            {
                throw new ConflictException("Số dư đã về 0; không ghi thêm lần chi.");
            }

            if (balance < ICreatorPayoutService.MinimumPayoutVnd)
            {
                throw new ConflictException(
                    $"Số dư {balance:N0} đ chưa đạt mức chi tối thiểu {ICreatorPayoutService.MinimumPayoutVnd:N0} đ.");
            }

            if (request.AmountVnd != balance)
            {
                throw new ConflictException($"Số tiền chi phải bằng đúng số dư hiện tại ({balance:N0} đ).");
            }

            var payout = new CreatorBalanceEntry
            {
                CreatorUserId = creator.Id,
                Type = CreatorBalanceEntryType.Payout,
                AmountVnd = -balance,
                BankTransactionCode = code,
                TransferredOn = transferDate,
                BankName = account.BankName,
                AccountNumber = account.AccountNumber,
                AccountHolderName = account.AccountHolderName,
                RecordedByUserId = adminUserId
            };
            await _unitOfWork.Repository<CreatorBalanceEntry>().AddAsync(payout, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                adminUserId, "CreatorPayoutMarkedPaid", nameof(CreatorBalanceEntry), payout.Id,
                new { creatorUserId = creator.Id, balanceVnd = balance },
                new
                {
                    creatorUserId = creator.Id, balanceVnd = 0L, amountVnd = balance, bankTransactionCode = code,
                    transferredOn = transferDate, bankName = account.BankName,
                    accountNumber = Mask(account.AccountNumber)
                },
                cancellationToken);
            await _notificationService.CreateAsync(
                creator.Id, NotificationType.CreatorPayoutPaid,
                JsonSerializer.Serialize(new
                {
                    payoutId = payout.Id, amountVnd = balance, bankTransactionCode = code, transferredOn = transferDate
                }),
                cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return ToDto(payout, null);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task<CreatorPayoutDto> CancelPayoutAsync(
        int adminUserId, int payoutId, CancelCreatorPayoutRequestDto request, CancellationToken cancellationToken = default)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0)
        {
            throw new BadRequestException("Cần ghi lý do huỷ lần chi.");
        }

        if (reason.Length > 500)
        {
            throw new BadRequestException("Lý do huỷ tối đa 500 ký tự.");
        }

        await RequireActiveAdminAsync(adminUserId, cancellationToken);
        var creatorUserId = (await FindPayoutAsync(payoutId, cancellationToken)).CreatorUserId;
        var entries = _unitOfWork.Repository<CreatorBalanceEntry>();

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(creatorUserId, cancellationToken);
            _unitOfWork.DiscardTrackedChanges();
            var payout = await FindPayoutAsync(payoutId, cancellationToken);
            if (payout.CancelledAt != null)
            {
                throw new ConflictException("Lần chi này đã được huỷ.");
            }

            var now = DateTime.UtcNow;
            var balanceBefore = await GetBalanceAsync(payout.CreatorUserId, cancellationToken);
            var reversal = new CreatorBalanceEntry
            {
                CreatorUserId = payout.CreatorUserId,
                Type = CreatorBalanceEntryType.PayoutReversal,
                AmountVnd = -payout.AmountVnd,
                ReversedEntryId = payout.Id,
                RecordedByUserId = adminUserId,
                Reason = reason,
                CreatedAt = now
            };
            payout.CancelledAt = now;
            payout.UpdatedAt = now;
            entries.Update(payout);
            await entries.AddAsync(reversal, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLogWriter.LogAsync(
                adminUserId, "CreatorPayoutCancelled", nameof(CreatorBalanceEntry), payout.Id,
                new { creatorUserId = payout.CreatorUserId, status = "Paid", balanceVnd = balanceBefore },
                new
                {
                    creatorUserId = payout.CreatorUserId, status = "Cancelled", reason,
                    balanceVnd = balanceBefore + reversal.AmountVnd, reversalEntryId = reversal.Id
                },
                cancellationToken);
            await _notificationService.CreateAsync(
                payout.CreatorUserId, NotificationType.CreatorPayoutCancelled,
                JsonSerializer.Serialize(new { payoutId = payout.Id, amountVnd = reversal.AmountVnd, reason }),
                cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return ToDto(payout, reversal);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }

    public async Task<bool> CloseDuePeriodAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var today = VietnamTime.LocalDate(now);
        if (today.Day < ICreatorPayoutService.PeriodCloseDay)
        {
            return false;
        }

        // Kỳ là tháng dương lịch trước đó, cùng cách tính kỳ (theo ngày) của thống kê thu nhập dự kiến (5f).
        var periodStart = DateTime.SpecifyKind(new DateTime(today.Year, today.Month, 1).AddMonths(-1), DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var periods = _unitOfWork.Repository<CreatorEarningPeriod>();
        if (await periods.ExistsAsync(period => period.PeriodMonth == periodStart, cancellationToken))
        {
            return false;
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _unitOfWork.AcquireTransactionLockAsync(PeriodCloseLockId, cancellationToken);
            if (await periods.ExistsAsync(period => period.PeriodMonth == periodStart, cancellationToken))
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            var earnings = await _statistics.GetPeriodEarningsAsync(periodStart, periodEnd, cancellationToken);
            var credited = earnings.Creators.Where(row => row.EarningsVnd > 0).ToList();
            var period = new CreatorEarningPeriod
            {
                PeriodMonth = periodStart,
                CreatorFundVnd = earnings.CreatorFundVnd,
                TotalValidReads = earnings.TotalValidReads,
                DistributedVnd = credited.Sum(row => row.EarningsVnd),
                ClosedAt = now,
                CreatedAt = now
            };
            await periods.AddAsync(period, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var entries = _unitOfWork.Repository<CreatorBalanceEntry>();
            foreach (var row in credited)
            {
                // Creator bị vô hiệu hoá vẫn được cộng: số dư giữ đến khi tài khoản được kích hoạt lại.
                await entries.AddAsync(new CreatorBalanceEntry
                {
                    CreatorUserId = row.CreatorUserId,
                    Type = CreatorBalanceEntryType.PeriodEarning,
                    AmountVnd = row.EarningsVnd,
                    EarningPeriodId = period.Id,
                    ValidReads = row.ValidReads,
                    CreatedAt = now
                }, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return true;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            _unitOfWork.DiscardTrackedChanges();
            throw;
        }
    }

    private async Task<long> GetBalanceAsync(int creatorUserId, CancellationToken cancellationToken) =>
        (await _unitOfWork.Repository<CreatorBalanceEntry>().FindAsync(
            entry => entry.CreatorUserId == creatorUserId, cancellationToken: cancellationToken))
        .Sum(entry => entry.AmountVnd);

    private Task<CreatorPayoutAccount?> FindAccountAsync(int creatorUserId, CancellationToken cancellationToken) =>
        _unitOfWork.Repository<CreatorPayoutAccount>().FirstOrDefaultAsync(
            account => account.CreatorUserId == creatorUserId, cancellationToken: cancellationToken);

    private async Task<CreatorBalanceEntry> FindPayoutAsync(int payoutId, CancellationToken cancellationToken)
    {
        var payout = await _unitOfWork.Repository<CreatorBalanceEntry>().GetByIdAsync(payoutId, cancellationToken);
        if (payout == null || payout.IsDeleted || payout.Type != CreatorBalanceEntryType.Payout)
        {
            throw new NotFoundException("Lần chi", payoutId);
        }

        return payout;
    }

    private Task RequireActiveAdminAsync(int adminUserId, CancellationToken cancellationToken) =>
        _accessPolicy.EnsureAsync(
            adminUserId, Permissions.Admin.CreatorPayoutManage,
            deniedMessage: "Chỉ quản trị viên đang hoạt động mới ghi nhận được chi trả cho nhà sáng tạo.",
            cancellationToken: cancellationToken);

    private async Task<UserAccount> RequireCreatorAsync(int creatorUserId, CancellationToken cancellationToken)
    {
        var creator = await _unitOfWork.Repository<UserAccount>().GetByIdAsync(creatorUserId, cancellationToken);
        if (creator == null || creator.IsDeleted || creator.Role != UserRole.ContentCreator)
        {
            throw new NotFoundException("Tài khoản nhà sáng tạo nội dung", creatorUserId);
        }

        return creator;
    }

    /// <summary>Chỉ để 4 số cuối, dùng cho audit log.</summary>
    public static string Mask(string accountNumber) =>
        accountNumber.Length <= 4 ? accountNumber : new string('*', accountNumber.Length - 4) + accountNumber[^4..];

    private static object AuditState(CreatorPayoutAccount account) => new
    {
        bankName = account.BankName,
        accountNumber = Mask(account.AccountNumber),
        accountHolderName = account.AccountHolderName
    };

    private static CreatorPayoutAccountDto ToDto(CreatorPayoutAccount account) => new()
    {
        BankName = account.BankName,
        AccountNumber = account.AccountNumber,
        AccountHolderName = account.AccountHolderName,
        UpdatedAt = account.UpdatedAt ?? account.CreatedAt
    };

    private static CreatorPayoutDto ToDto(CreatorBalanceEntry payout, CreatorBalanceEntry? reversal) => new()
    {
        PayoutId = payout.Id,
        CreatorUserId = payout.CreatorUserId,
        AmountVnd = -payout.AmountVnd,
        BankTransactionCode = payout.BankTransactionCode ?? string.Empty,
        TransferredOn = payout.TransferredOn ?? payout.CreatedAt.Date,
        BankName = payout.BankName ?? string.Empty,
        AccountNumber = payout.AccountNumber ?? string.Empty,
        AccountHolderName = payout.AccountHolderName ?? string.Empty,
        RecordedAt = payout.CreatedAt,
        Status = payout.CancelledAt == null ? "Paid" : "Cancelled",
        CancelledAt = payout.CancelledAt,
        CancelReason = reversal?.Reason
    };
}
