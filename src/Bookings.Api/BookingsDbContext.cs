using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Api;

public sealed class BookingsDbContext(DbContextOptions<BookingsDbContext> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Booking>(e => { e.Property(x => x.UserId).HasMaxLength(100); e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.PaymentRequestKey).HasMaxLength(100); e.Property(x => x.Total).HasPrecision(12, 2); e.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique(); e.HasMany(x => x.Items).WithOne(x => x.Booking).HasForeignKey(x => x.BookingId); e.HasMany(x => x.Tickets).WithOne(x => x.Booking).HasForeignKey(x => x.BookingId); });
        model.Entity<BookingItem>(e => { e.Property(x => x.Price).HasPrecision(12, 2); e.Property(x => x.SeatLabel).HasMaxLength(30); e.HasIndex(x => new { x.BookingId, x.SeatId }).IsUnique(); });
        model.Entity<Ticket>(e => { e.Property(x => x.Code).HasMaxLength(50); e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.BookingId, x.SeatId }).IsUnique(); });
        model.AddInboxStateEntity(); model.AddOutboxMessageEntity(); model.AddOutboxStateEntity();
    }
}
