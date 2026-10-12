namespace Payments.Api;

public enum PaymentStatus { Pending, Approved, Rejected, RefundPending, Refunded, RefundFailed }
public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public required string UserId { get; set; }
    public required string IdempotencyKey { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? FailureReason { get; set; }
    public List<Refund> Refunds { get; set; } = [];
}
public sealed class Refund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public required string IdempotencyKey { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public Payment Payment { get; set; } = null!;
}
public sealed class GatewayOptions
{
    public int LatencySeconds { get; set; } = 1;
    public double FailureRate { get; set; } = 0.1;
}
