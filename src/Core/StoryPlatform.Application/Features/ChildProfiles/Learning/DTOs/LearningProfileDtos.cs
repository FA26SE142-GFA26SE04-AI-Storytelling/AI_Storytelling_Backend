using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using StoryPlatform.Domain.Enums;

namespace StoryPlatform.Application.Features.ChildProfiles.Learning.DTOs;

public class LearningProfileDto
{
    public int Id { get; set; }
    public int ChildProfileId { get; set; }
    public int ReadingLevel { get; set; }
    public string? ComprehensionGoal { get; set; }
    public List<LearningProfileTopicDto> Topics { get; set; } = new();
}

public class LearningProfileTopicDto
{
    public string Topic { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
}

public class SetLearningProfileRequestDto
{
    [Range(1, 5, ErrorMessage = "Reading level phải từ 1 đến 5.")]
    public int ReadingLevel { get; set; }

    [StringLength(500, ErrorMessage = "Mục tiêu đọc hiểu tối đa 500 ký tự.")]
    public string? ComprehensionGoal { get; set; }

    public List<SetLearningProfileTopicRequestDto> Topics { get; set; } = new();
}

public class SetLearningProfileTopicRequestDto
{
    [Required(ErrorMessage = "Tên chủ đề không được để trống.")]
    [StringLength(150, ErrorMessage = "Tên chủ đề tối đa 150 ký tự.")]
    public string Topic { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TopicRelation Relation { get; set; } = TopicRelation.FavoriteTopic;
}
