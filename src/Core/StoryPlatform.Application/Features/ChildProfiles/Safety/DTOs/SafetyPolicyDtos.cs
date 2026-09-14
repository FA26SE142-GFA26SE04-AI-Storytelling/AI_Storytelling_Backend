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

    public List<SetSafetyPolicyCategoryRequestDto> Categories { get; set; } = new();
}

public class SetSafetyPolicyCategoryRequestDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Content Category không hợp lệ.")]
    public int ContentCategoryId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolicyRule Rule { get; set; } = PolicyRule.Allowed;
}
