namespace BookingTemplate.Application.DTOs.Chat;

/// <summary>
/// Gemini 结构化输出：意图与预约相关字段（用于 C# 侧校验与是否调用工具）。
/// </summary>
public sealed class BookingIntentExtractionDto
{
    public string Intent { get; set; } = "general";

    public string? ServiceName { get; set; }
    public string? Date { get; set; }
    public string? StartTime { get; set; }
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? PetName { get; set; }
    public string? PetType { get; set; }

    /// <summary>FAQ / 政策类检索用的查询句（可与用户原句不同）。</summary>
    public string? FaqQuery { get; set; }

    /// <summary>模型给出的缺失字段提示（会与后端计算结果合并）。</summary>
    public List<string>? MissingFields { get; set; }
}
