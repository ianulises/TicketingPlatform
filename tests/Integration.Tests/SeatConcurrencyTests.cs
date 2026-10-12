using Events.Api;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Integration.Tests;

public sealed class SeatConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DbContextOptions<EventsDbContext> options = null!;
    private Guid eventId;
    private Guid seatId;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        options = new DbContextOptionsBuilder<EventsDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new EventsDbContext(options); await db.Database.MigrateAsync();
        var entity = new Event { Name = "Concierto", Venue = "Foro", OrganizerId = "org", StartsAt = DateTimeOffset.UtcNow.AddDays(10), Zones = [new Zone { Name = "A", Price = 100, Seats = [new Seat { Label = "A-1" }] }] };
        db.Events.Add(entity); await db.SaveChangesAsync(); eventId = entity.Id; seatId = entity.Zones[0].Seats[0].Id;
    }

    [Fact]
    public async Task Only_one_of_100_concurrent_requests_can_hold_the_same_seat()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var attempts = Enumerable.Range(0, 100).Select(async _ =>
        {
            await using var db = new EventsDbContext(options);
            return await new SeatAllocator(db, TimeProvider.System).HoldAsync(Guid.NewGuid(), eventId, [seatId], expiresAt);
        });
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, x => x.Succeeded);
    }

    public async Task DisposeAsync() => await database.DisposeAsync();
}
