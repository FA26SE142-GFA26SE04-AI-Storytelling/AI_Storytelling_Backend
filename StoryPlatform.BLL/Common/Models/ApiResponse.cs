using System.Collections.Generic;

namespace StoryPlatform.BLL.Common.Models;

/// <summary>
/// Định dạng phản hồi chuẩn cho toàn bộ API trong hệ thống.
/// </summary>
/// <typeparam name="T">Kiểu dữ liệu của payload</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Thao tác thành công.")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            Errors = null
        };
    }

    public static ApiResponse<T> Fail(string message, List<string>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Data = default,
            Errors = errors
        };
    }
}
