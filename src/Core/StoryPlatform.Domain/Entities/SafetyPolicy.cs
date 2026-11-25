using System;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Domain.Entities;

public class SafetyPolicy : BaseEntity
{
    public int ChildProfileId { get; set; }
    public virtual ChildProfile? ChildProfile { get; set; }

    public int MaxStoryLength { get; set; }

    /// <summary>
    /// Độ dài truyện (số từ) phụ huynh tự chỉnh; NULL = theo mức đọc của bé (900 hoặc 1500). AI viết khoảng ±10% quanh số này.
    /// </summary>
    public int? StoryLengthTarget { get; set; }
    public ApprovalMode RequiredApprovalMode { get; set; } = ApprovalMode.AlwaysManual;
    public bool ParentalGateEnabled { get; set; } = true;
    public bool ConsentRecorded { get; set; } = false;
    public DateTime? ConsentRecordedAt { get; set; }

    public int ConsentPolicyVersion { get; set; } = 1;
    public int? ConsentedByUserId { get; set; }
    public virtual UserAccount? ConsentedByUser { get; set; }
    public decimal? SafetyScoreThreshold { get; set; }
    public decimal? ReadabilityScoreThreshold { get; set; }
    public decimal ComprehensionThresholdPercent { get; set; } = 70m;
    public int ComprehensionWindowSize { get; set; } = 3;
    public bool StoryWishesEnabled { get; set; } = true;
    public bool AllowInteractiveStories { get; set; } = true;
    public bool ChildCreationEnabled { get; set; } = true;
    public ChildCreationMode ChildCreationMode { get; set; } = ChildCreationMode.ReviewFirst;
    public bool VoiceAnswersEnabled { get; set; }

    // Quyền của bé (phụ huynh bật/tắt từng chức năng trong phiên của bé); mặc định bật.
    public bool QuizEnabled { get; set; } = true;
    public bool NotebookEnabled { get; set; } = true;
    public bool LibraryBrowseEnabled { get; set; } = true;
    public bool ReflectionEnabled { get; set; } = true;
    public bool RewardsEnabled { get; set; } = true;

    /// <summary>Thời điểm Owner đồng ý mục (e) về giọng nói; NULL = chưa đồng ý.</summary>
    public DateTime? VoiceConsentAt { get; set; }

    /// <summary>
    /// Additional Supervisor muốn bật giọng nói khi Owner chưa đồng ý. NULL = không có yêu cầu chờ.
    /// </summary>
    public DateTime? VoiceEnableRequestedAt { get; set; }
}
