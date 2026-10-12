using System.Data;
using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Events.Api;

public sealed record AllocationResult(bool Succeeded, string? Error, DateTimeOffset EventStartsAt, SeatPrice[] Seats);

public sealed class SeatAllocator(EventsDbContext db, TimeProvider clock)
{
    public async Task<AllocationResult> HoldAsync(Guid bookingId, Guid eventId, Guid[] seatIds, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        if (seatIds is not { Length: > 0 and <= 6 } || seatIds.Distinct().Count() != seatIds.Length) return new(false, "Debe seleccionar entre uno y seis asientos distintos.", default, []);

        // El bloqueo vive en PostgreSQL para que la garantía se mantenga aunque haya varias instancias de la API.
        // Ordenar las filas también evita que dos reservas de varios asientos las bloqueen en distinto orden.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var seats = await db.Seats.FromSqlInterpolated($"SELECT * FROM \"Seats\" WHERE \"Id\" = ANY ({seatIds}) ORDER BY \"Id\" FOR UPDATE").Include(x => x.Zone).ThenInclude(x => x.Event).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        // La validación se hace después de tomar los bloqueos. Si un asiento falla, no se modifica ninguno.
        var invalid = seats.Count != seatIds.Length || seats.Any(s => s.Zone.EventId != eventId || s.Status == SeatStatus.Sold || s.Status == SeatStatus.Held && s.HeldUntil > now && s.BookingId != bookingId);
        if (invalid) { await transaction.RollbackAsync(cancellationToken); return new(false, "Uno o más asientos no están disponibles.", default, []); }
        foreach (var seat in seats) { seat.Status = SeatStatus.Held; seat.BookingId = bookingId; seat.HeldUntil = expiresAt; }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(true, null, seats[0].Zone.Event.StartsAt, seats.Select(s => new SeatPrice(s.Id, s.Label, s.ZoneId, s.Zone.Price)).ToArray());
    }
}
