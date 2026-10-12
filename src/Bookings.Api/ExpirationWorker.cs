using Bookings.Api;
using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

public sealed class ExpirationWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No se agenda un timer por reserva: la fecha persistida permite retomar el trabajo después de un reinicio.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<BookingsDbContext>(); var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(); var now = clock.GetUtcNow();
                var expired = await db.Bookings.Where(x => (x.Status == BookingStatus.Held || x.Status == BookingStatus.PaymentRejected) && x.ExpiresAt <= now).ToListAsync(stoppingToken);
                foreach (var booking in expired) { booking.Status = BookingStatus.Expired; await publish.Publish(new ReleaseSeats(booking.Id, "Reserva vencida.", Guid.NewGuid()), stoppingToken); }
                if (expired.Count > 0) await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) { logger.LogError(ex, "No fue posible procesar reservas vencidas"); }
        }
    }
}
