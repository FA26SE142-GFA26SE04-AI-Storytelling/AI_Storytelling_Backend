using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;

public class SafetyPolicyDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public int MaxStoryLength { get; set; }
    public int? StoryLengthTarget { get; set; }
    public string RequiredApprovalMode { get; set; } = string.Empty;
    public bool ParentalGateEnabled { get; set; }
    public bool ConsentRecorded { get; set; }
    public DateTime? ConsentRecordedAt { get; set; }
    public int ConsentPolicyVersion { get; set; }
    public int? ConsentedByUserId { get; set; }
    public decimal? SafetyScoreThreshold { get; set; }
    public decimal? ReadabilityScoreThreshold { get; set; }
    public bool StoryWishesEnabled { get; set; }
    public bool AllowInteractiveStories { get; set; }
    public bool ChildCreationEnabled { get; set; }
    public string ChildCreationMode { get; set; } = string.Empty;
    public bool VoiceAnswersEnabled { get; set; }
    public DateTime? VoiceConsentAt { get; set; }
    public bool VoiceConsentRequired { get; set; }
    public bool ConsentOutdated { get; set; }
    public bool EffectiveVoiceAnswersEnabled { get; set; }
    public bool QuizEnabled { get; set; }
    public bool NotebookEnabled { get; set; }
    public bool LibraryBrowseEnabled { get; set; }
    public bool ReflectionEnabled { get; set; }
    public bool RewardsEnabled { get; set; }
    public List<SafetyPolicyCategoryDto> Categories { get; set; } = new();
}

public class SafetyPolicyCategoryDto
{
    public int ContentCategoryId { get; set; }
    public string Rule { get; set; } = string.Empty;
}

public class SetSafetyPolicyRequestDto
{
    [Range(100, 20000, ErrorMessage = "Độ dài truyện tối đa phải từ 100 đến 20000 đơn vị từ.")]
    public int MaxStoryLength { get; set; } = 2000;

    /// <summary>Độ dài truyện phụ huynh tự chỉnh từ 300 đến mặc định theo tuổi và mức đọc; null = theo mức đọc.</summary>
    [Range(StoryLengthRules.MinimumTarget, int.MaxValue, ErrorMessage = "Độ dài truyện phải từ 300 từ trở lên.")]
    public int? StoryLengthTarget { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApprovalMode RequiredApprovalMode { get; set; } = ApprovalMode.AlwaysManual;

    public bool ParentalGateEnabled { get; set; } = true;

    public bool ConsentRecorded { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Ngưỡng safety phải nằm trong khoảng 0 đến 100.")]
    public decimal? SafetyScoreThreshold { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Ngưỡng readability phải nằm trong khoảng 0 đến 100.")]
    public decimal? ReadabilityScoreThreshold { get; set; }

    public bool StoryWishesEnabled { get; set; } = true;
    public bool AllowInteractiveStories { get; set; } = true;
    public bool ChildCreationEnabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChildCreationMode ChildCreationMode { get; set; } = ChildCreationMode.ReviewFirst;

    public bool VoiceAnswersEnabled { get; set; }

    /// <summary>Quyền của bé; null (client cũ không gửi) = giữ giá trị đang lưu.</summary>
    public bool? QuizEnabled { get; set; }
    public bool? NotebookEnabled { get; set; }
    public bool? LibraryBrowseEnabled { get; set; }
    public bool? ReflectionEnabled { get; set; }
    public bool? RewardsEnabled { get; set; }

    /// <summary>Owner đồng ý mục (e) giọng nói khi bật lần đầu.</summary>
    public bool VoiceConsentAccepted { get; set; }

    /// <summary>null = tự áp khi tạo mới và chưa khai danh mục; false = không áp.</summary>
    public bool? ApplyAgePreset { get; set; }

    public List<SetSafetyPolicyCategoryRequestDto> Categories { get; set; } = new();
}

public class AgePresetDto
{
    public string AgeBand { get; set; } = string.Empty;
    public List<int> BlockedCategoryIds { get; set; } = new();
}

public class SetSafetyPolicyCategoryRequestDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Content Category không hợp lệ.")]
    public int ContentCategoryId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolicyRule Rule { get; set; } = PolicyRule.Allowed;
}
