namespace BookingTemplate.Domain.Entities;

public sealed class BusinessHour
{
    public Guid Id { get; set; }
    public short Weekday { get; set; }
    public bool IsOpen { get; set; } = true;
    public TimeOnly? OpenTime { get; set; }
    public TimeOnly? CloseTime { get; set; }
    public int SlotIntervalMinutes { get; set; } = 30;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
