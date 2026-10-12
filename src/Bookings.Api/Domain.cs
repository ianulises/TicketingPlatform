namespace Bookings.Api;

public enum BookingStatus { Held, PaymentPending, Confirmed, Expired, PaymentRejected, RefundPending, RefundFailed, Cancelled }

public sealed class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserId { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid EventId { get; set; }
    public DateTimeOffset EventStartsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public BookingStatus Status { get; set; }
    public decimal Total { get; set; }
    public Guid? PaymentId { get; set; }
    public string? PaymentRequestKey { get; set; }
    public List<BookingItem> Items { get; set; } = [];
    public List<Ticket> Tickets { get; set; } = [];
}
public sealed class BookingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid SeatId { get; set; }
    public Guid ZoneId { get; set; }
    public required string SeatLabel { get; set; }
    public decimal Price { get; set; }
    public Booking Booking { get; set; } = null!;
}
public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid SeatId { get; set; }
    public required string Code { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Booking Booking { get; set; } = null!;
}
