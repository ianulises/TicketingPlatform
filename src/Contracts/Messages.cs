namespace Contracts;

public record HoldSeats(Guid BookingId, Guid EventId, Guid[] SeatIds, DateTimeOffset ExpiresAt, Guid CorrelationId);
public record SeatPrice(Guid SeatId, string Label, Guid ZoneId, decimal Price);
public record SeatsHeld(Guid BookingId, DateTimeOffset EventStartsAt, SeatPrice[] Seats, Guid CorrelationId);
public record SeatsUnavailable(Guid BookingId, string Reason, Guid CorrelationId);
public record ReleaseSeats(Guid BookingId, string Reason, Guid CorrelationId);
public record ConfirmSeats(Guid BookingId, Guid CorrelationId);

public record RequestPayment(Guid BookingId, string UserId, decimal Amount, string IdempotencyKey, Guid CorrelationId);
public record PaymentCompleted(Guid BookingId, Guid PaymentId, decimal Amount, Guid CorrelationId);
public record PaymentRejected(Guid BookingId, Guid PaymentId, string Reason, Guid CorrelationId);

public record RequestRefund(Guid BookingId, Guid PaymentId, decimal Amount, string IdempotencyKey, Guid CorrelationId);
public record RefundCompleted(Guid BookingId, Guid PaymentId, Guid RefundId, Guid CorrelationId);
public record RefundFailed(Guid BookingId, Guid PaymentId, string Reason, Guid CorrelationId);
