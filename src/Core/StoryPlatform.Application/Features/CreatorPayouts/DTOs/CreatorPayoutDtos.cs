using System.ComponentModel.DataAnnotations;

namespace StoryPlatform.Application.Features.CreatorPayouts.DTOs;

/// <summary>Số dư, thông tin nhận tiền và lịch sử của một Creator (Luồng 5, 5h).</summary>
public class CreatorPayoutOverviewDto
{
    public int CreatorUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    /// <summary>Số dư hiện tại (VND, trước thuế) = tổng các bút toán.</summary>
    public long BalanceVnd { get; set; }
    /// <summary>Số dư tối thiểu để được chi; dưới mức này cộng dồn sang kỳ sau.</summary>
    public long MinimumPayoutVnd { get; set; }
    public bool HasPayoutAccount { get; set; }
    /// <summary>Lời nhắc nhập thông tin nhận tiền khi Creator chưa nhập; null khi đã có.</summary>
    public string? Reminder { get; set; }
    public CreatorPayoutAccountDto? PayoutAccount { get; set; }
    /// <summary>Các lần chi, mới nhất trước.</summary>
    public List<CreatorPayoutDto> Payouts { get; set; } = [];
    /// <summary>Thu nhập các kỳ đã chốt, mới nhất trước.</summary>
    public List<CreatorPeriodEarningEntryDto> Earnings { get; set; } = [];
}

public class CreatorPayoutAccountDto
{
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class CreatorPayoutDto
{
    public int PayoutId { get; set; }
    public int CreatorUserId { get; set; }
    /// <summary>Số tiền đã chi (số dương).</summary>
    public long AmountVnd { get; set; }
    public string BankTransactionCode { get; set; } = string.Empty;
    public DateTime TransferredOn { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    /// <summary>Paid hoặc Cancelled.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
}

public class CreatorPeriodEarningEntryDto
{
    public DateTime PeriodMonth { get; set; }
    public int ValidReads { get; set; }
    public long AmountVnd { get; set; }
    public DateTime CreditedAt { get; set; }
}

/// <summary>Một dòng trong danh sách chi của Admin.</summary>
public class CreatorPayoutDueDto
{
    public int CreatorUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public long BalanceVnd { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
}

public class UpdateCreatorPayoutAccountRequestDto
{
    [Required(ErrorMessage = "Cần nhập tên ngân hàng.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên ngân hàng từ 2 đến 100 ký tự.")]
    public string BankName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cần nhập số tài khoản.")]
    [RegularExpression(@"^\s*\d[\d ]{4,28}\d\s*$", ErrorMessage = "Số tài khoản chỉ gồm chữ số (6 đến 20 số).")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Cần nhập tên chủ tài khoản.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên chủ tài khoản từ 2 đến 100 ký tự.")]
    public string AccountHolderName { get; set; } = string.Empty;
}

public class MarkCreatorPayoutPaidRequestDto
{
    /// <summary>Số tiền Admin đã chuyển; phải bằng đúng số dư hiện tại của Creator.</summary>
    [Range(1, long.MaxValue, ErrorMessage = "Số tiền chi phải lớn hơn 0.")]
    public long AmountVnd { get; set; }

    [Required(ErrorMessage = "Cần nhập mã giao dịch ngân hàng.")]
    [StringLength(100, ErrorMessage = "Mã giao dịch tối đa 100 ký tự.")]
    public string BankTransactionCode { get; set; } = string.Empty;

    /// <summary>Ngày chuyển khoản (theo ngày Việt Nam); không được sau hôm nay.</summary>
    [Required(ErrorMessage = "Cần nhập ngày chuyển khoản.")]
    public DateTime? TransferredOn { get; set; }
}

public class CancelCreatorPayoutRequestDto
{
    [Required(ErrorMessage = "Cần ghi lý do huỷ lần chi.")]
    [StringLength(500, ErrorMessage = "Lý do huỷ tối đa 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}
