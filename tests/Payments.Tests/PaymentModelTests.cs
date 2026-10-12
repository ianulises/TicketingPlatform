using Microsoft.EntityFrameworkCore;
using Payments.Api;

namespace Payments.Tests;

public sealed class PaymentModelTests
{
    [Fact]
    public void Payment_and_refund_have_unique_idempotency_indexes()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=x;Password=x").Options;
        using var db = new PaymentsDbContext(options);
        var paymentIndexes = db.Model.FindEntityType(typeof(Payment))!.GetIndexes();
        var refundIndexes = db.Model.FindEntityType(typeof(Refund))!.GetIndexes();
        Assert.Contains(paymentIndexes, x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(Payment.BookingId), nameof(Payment.IdempotencyKey)]));
        Assert.Contains(refundIndexes, x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual([nameof(Refund.PaymentId), nameof(Refund.IdempotencyKey)]));
    }
}
