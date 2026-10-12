using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Events.Api.Messaging;

public sealed class HoldSeatsConsumer(SeatAllocator allocator) : IConsumer<HoldSeats>
{
    public async Task Consume(ConsumeContext<HoldSeats> context)
    {
        var command = context.Message;
        var result = await allocator.HoldAsync(command.BookingId, command.EventId, command.SeatIds, command.ExpiresAt, context.CancellationToken);
        if (!result.Succeeded) { await context.RespondAsync(new SeatsUnavailable(command.BookingId, result.Error!, command.CorrelationId)); return; }
        await context.RespondAsync(new SeatsHeld(command.BookingId, result.EventStartsAt, result.Seats, command.CorrelationId));
    }
}
public sealed class ReleaseSeatsConsumer(EventsDbContext db) : IConsumer<ReleaseSeats>
{
    public async Task Consume(ConsumeContext<ReleaseSeats> context) { var seats = await db.Seats.Where(x => x.BookingId == context.Message.BookingId).ToListAsync(); foreach (var seat in seats) { seat.Status = SeatStatus.Available; seat.BookingId = null; seat.HeldUntil = null; } await db.SaveChangesAsync(); }
}
public sealed class ConfirmSeatsConsumer(EventsDbContext db) : IConsumer<ConfirmSeats>
{
    public async Task Consume(ConsumeContext<ConfirmSeats> context) { var seats = await db.Seats.Where(x => x.BookingId == context.Message.BookingId).ToListAsync(); foreach (var seat in seats.Where(x => x.Status == SeatStatus.Held)) { seat.Status = SeatStatus.Sold; seat.HeldUntil = null; } await db.SaveChangesAsync(); }
}
