using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Events.Api;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Seat> Seats => Set<Seat>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Event>(e => { e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Venue).HasMaxLength(200); e.Property(x => x.OrganizerId).HasMaxLength(100); e.HasMany(x => x.Zones).WithOne(x => x.Event).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade); });
        model.Entity<Zone>(e => { e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Price).HasPrecision(12, 2); e.HasMany(x => x.Seats).WithOne(x => x.Zone).HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Cascade); e.HasIndex(x => new { x.EventId, x.Name }).IsUnique(); });
        model.Entity<Seat>(e => { e.Property(x => x.Label).HasMaxLength(30); e.HasIndex(x => new { x.ZoneId, x.Label }).IsUnique(); e.HasIndex(x => x.BookingId); });
        model.AddInboxStateEntity(); model.AddOutboxMessageEntity(); model.AddOutboxStateEntity();
    }
}
