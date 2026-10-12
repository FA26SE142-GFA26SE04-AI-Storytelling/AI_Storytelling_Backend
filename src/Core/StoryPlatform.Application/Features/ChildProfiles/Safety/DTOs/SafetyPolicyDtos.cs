using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Safety.DTOs;

public class SafetyPolicyDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public int MaxStoryLength { get; set; }
    public string RequiredApprovalMode { get; set; } = string.Empty;
    public bool ParentalGateEnabled { get; set; }
    public bool ConsentRecorded { get; set; }
    public DateTime? ConsentRecordedAt { get; set; }
    public int ConsentPolicyVersion { get; set; }
    public int? ConsentedByUserId { get; set; }
    public decimal? SafetyScoreThreshold { get; set; }
    public decimal? ReadabilityScoreThreshold { get; set; }
    public decimal ComprehensionThresholdPercent { get; set; }
    public int ComprehensionWindowSize { get; set; }
    public bool StoryWishesEnabled { get; set; }
    public bool AllowInteractiveStories { get; set; }
    public bool ChildCreationEnabled { get; set; }
    public string ChildCreationMode { get; set; } = string.Empty;
    public bool VoiceAnswersEnabled { get; set; }
    public DateTime? VoiceConsentAt { get; set; }
    public bool VoiceConsentRequired { get; set; }
    public bool ConsentOutdated { get; set; }
    public bool EffectiveVoiceAnswersEnabled { get; set; }
    public List<SafetyPolicyCategoryDto> Categories { get; set; } = new();
}

public class SafetyPolicyCategoryDto
{
    public int ContentCategoryId { get; set; }
    public string Rule { get; set; } = string.Empty;
}

public class SetSafetyPolicyRequestDto
{
    [Range(100, 20000, ErrorMessage = "Độ dài truyện tối đa phải từ 100 đến 20000 ký tự.")]
    public int MaxStoryLength { get; set; } = 2000;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApprovalMode RequiredApprovalMode { get; set; } = ApprovalMode.AlwaysManual;

    public bool ParentalGateEnabled { get; set; } = true;

    public bool ConsentRecorded { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Ngưỡng safety phải nằm trong khoảng 0 đến 100.")]
    public decimal? SafetyScoreThreshold { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Ngưỡng readability phải nằm trong khoảng 0 đến 100.")]
    public decimal? ReadabilityScoreThreshold { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Ngưỡng comprehension phải nằm trong khoảng 0 đến 100.")]
    public decimal ComprehensionThresholdPercent { get; set; } = 70m;

    [Range(1, 20, ErrorMessage = "Comprehension window size phải nằm trong khoảng 1 đến 20.")]
    public int ComprehensionWindowSize { get; set; } = 3;

    public bool StoryWishesEnabled { get; set; } = true;
    public bool AllowInteractiveStories { get; set; } = true;
    public bool ChildCreationEnabled { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ChildCreationMode ChildCreationMode { get; set; } = ChildCreationMode.ReviewFirst;

    public bool VoiceAnswersEnabled { get; set; }

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
