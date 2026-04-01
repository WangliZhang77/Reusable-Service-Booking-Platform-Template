namespace BookingTemplate.Application.DTOs.Chat;

/// <summary>
/// 与 Gemini 侧确认 token 的 JSON 字段一致，用于序列化/反序列化。
/// </summary>
public sealed record BookingConfirmationPayload(
    string ServiceName,
    string Date,
    string? StartTime,
    string? CustomerName,
    string Phone,
    string PetName,
    string? PetType);
