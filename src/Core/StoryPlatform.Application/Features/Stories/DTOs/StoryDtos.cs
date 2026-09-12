using System;
using System.ComponentModel.DataAnnotations;
using StoryPlatform.Application.Common.Models;

namespace StoryPlatform.Application.Features.Stories.DTOs;

public class StoryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Content { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Genre { get; set; }
    public string? MoralLesson { get; set; }
    public string AgeBand { get; set; } = string.Empty;
    public string Language { get; set; } = "vi";
    public string Source { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public int AuthorUserId { get; set; }
    public string? AuthorName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateStoryRequestDto
{
    [Required(ErrorMessage = "Tiêu đề truyện không được để trống.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Tiêu đề truyện từ 3 đến 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? Content { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Genre { get; set; }
    public string? MoralLesson { get; set; }
    public string AgeBand { get; set; } = "6-8";
    public string Language { get; set; } = "vi";
    public bool IsAiGenerated { get; set; } = true;
}

public class UpdateStoryRequestDto
{
    [Required(ErrorMessage = "Tiêu đề truyện không được để trống.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Tiêu đề truyện từ 3 đến 200 ký tự.")]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? Content { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? Genre { get; set; }
    public string? MoralLesson { get; set; }
    public string AgeBand { get; set; } = "6-8";
    public string Language { get; set; } = "vi";
}

public class StoryFilterRequestDto : PageRequest
{
    public string? Genre { get; set; }
    public string? AgeBand { get; set; }
    public string? Status { get; set; }
    public bool? IsPublished { get; set; }
    public int? AuthorUserId { get; set; }
}
