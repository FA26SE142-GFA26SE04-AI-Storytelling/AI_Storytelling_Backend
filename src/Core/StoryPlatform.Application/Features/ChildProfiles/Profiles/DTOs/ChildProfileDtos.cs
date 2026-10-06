using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Profiles.DTOs;

public class ChildProfileDto
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string AgeBand { get; set; } = string.Empty;
    public string Language { get; set; } = "vi";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateChildProfileRequestDto
{
    [Required(ErrorMessage = "Biệt danh của trẻ không được để trống.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Biệt danh từ 1 đến 150 ký tự.")]
    public string Nickname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhóm tuổi nhận thức không được để trống.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgeBand AgeBand { get; set; }

    [StringLength(10, ErrorMessage = "Mã ngôn ngữ tối đa 10 ký tự.")]
    public string Language { get; set; } = "vi";
}

public class UpdateChildProfileRequestDto
{
    [Required(ErrorMessage = "Biệt danh của trẻ không được để trống.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Biệt danh từ 1 đến 150 ký tự.")]
    public string Nickname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nhóm tuổi nhận thức không được để trống.")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgeBand AgeBand { get; set; }

    [StringLength(10, ErrorMessage = "Mã ngôn ngữ tối đa 10 ký tự.")]
    public string Language { get; set; } = "vi";
}
