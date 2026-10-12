using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Api.Messaging;

public sealed class PaymentCompletedConsumer(BookingsDbContext db, TimeProvider clock, IPublishEndpoint publish) : IConsumer<PaymentCompleted>
{
    public async Task Consume(ConsumeContext<PaymentCompleted> context)
    {
        var booking = await db.Bookings.Include(x => x.Items).Include(x => x.Tickets).SingleOrDefaultAsync(x => x.Id == context.Message.BookingId); if (booking is null || booking.Status is BookingStatus.Confirmed or BookingStatus.Cancelled) return;
        booking.PaymentId = context.Message.PaymentId;
        // Un pago tardío no revive la reserva: se compensa con un reembolso y no se emiten boletos.
        if (booking.ExpiresAt <= clock.GetUtcNow()) { booking.Status = BookingStatus.RefundPending; await publish.Publish(new RequestRefund(booking.Id, context.Message.PaymentId, booking.Total, $"expired-{booking.Id}", context.Message.CorrelationId)); await db.SaveChangesAsync(); return; }
        booking.Status = BookingStatus.Confirmed;
        foreach (var item in booking.Items.Where(i => booking.Tickets.All(t => t.SeatId != i.SeatId)))
            db.Tickets.Add(new Ticket { BookingId = booking.Id, SeatId = item.SeatId, Code = $"TKT-{Guid.NewGuid():N}".ToUpperInvariant() });
        await publish.Publish(new ConfirmSeats(booking.Id, context.Message.CorrelationId)); await db.SaveChangesAsync();
    }
}
public sealed class PaymentRejectedConsumer(BookingsDbContext db) : IConsumer<PaymentRejected>
{
    public async Task Consume(ConsumeContext<PaymentRejected> context) { var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == context.Message.BookingId); if (booking?.Status == BookingStatus.PaymentPending) { booking.Status = BookingStatus.PaymentRejected; booking.PaymentId = context.Message.PaymentId; await db.SaveChangesAsync(); } }
}
public sealed class RefundCompletedConsumer(BookingsDbContext db, IPublishEndpoint publish, TimeProvider clock) : IConsumer<RefundCompleted>
{
    public async Task Consume(ConsumeContext<RefundCompleted> context) { var booking = await db.Bookings.Include(x => x.Tickets).SingleOrDefaultAsync(x => x.Id == context.Message.BookingId); if (booking is null || booking.Status == BookingStatus.Cancelled) return; booking.Status = BookingStatus.Cancelled; foreach (var ticket in booking.Tickets) ticket.CancelledAt = clock.GetUtcNow(); await publish.Publish(new ReleaseSeats(booking.Id, "Reserva cancelada y reembolsada.", context.Message.CorrelationId)); await db.SaveChangesAsync(); }
}
public sealed class RefundFailedConsumer(BookingsDbContext db) : IConsumer<RefundFailed>
{
    public async Task Consume(ConsumeContext<RefundFailed> context) { var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == context.Message.BookingId); if (booking?.Status == BookingStatus.RefundPending) { booking.Status = BookingStatus.RefundFailed; await db.SaveChangesAsync(); } }
}
