using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Payments.Api;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>(); public DbSet<Refund> Refunds => Set<Refund>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Payment>(e => { e.Property(x => x.UserId).HasMaxLength(100); e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.Amount).HasPrecision(12, 2); e.HasIndex(x => new { x.BookingId, x.IdempotencyKey }).IsUnique(); e.HasMany(x => x.Refunds).WithOne(x => x.Payment).HasForeignKey(x => x.PaymentId); });
        model.Entity<Refund>(e => { e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.Amount).HasPrecision(12, 2); e.HasIndex(x => new { x.PaymentId, x.IdempotencyKey }).IsUnique(); });
        model.AddInboxStateEntity(); model.AddOutboxMessageEntity(); model.AddOutboxStateEntity();
    }
}
