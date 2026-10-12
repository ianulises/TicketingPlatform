using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Payments.Api.Messaging;

public sealed class RequestPaymentConsumer(PaymentsDbContext db, IOptions<GatewayOptions> options, TimeProvider clock, IPublishEndpoint publish) : IConsumer<RequestPayment>
{
    public async Task Consume(ConsumeContext<RequestPayment> context)
    {
        var command = context.Message; var existing = await db.Payments.SingleOrDefaultAsync(x => x.BookingId == command.BookingId && x.IdempotencyKey == command.IdempotencyKey);
        // En un reintento se publica el resultado almacenado; nunca se vuelve a ejecutar el cargo simulado.
        if (existing is not null) { await PublishResult(existing, command.CorrelationId); await db.SaveChangesAsync(); return; }
        var payment = new Payment { BookingId = command.BookingId, UserId = command.UserId, IdempotencyKey = command.IdempotencyKey, Amount = command.Amount, Status = PaymentStatus.Pending, CreatedAt = clock.GetUtcNow() };
        db.Payments.Add(payment); await db.SaveChangesAsync();
        // La espera ocurre después de persistir Pending para que la operación sea observable y recuperable.
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.LatencySeconds, 0, 10)), context.CancellationToken);
        if (Random.Shared.NextDouble() < Math.Clamp(options.Value.FailureRate, 0, 1)) { payment.Status = PaymentStatus.Rejected; payment.FailureReason = "Pago rechazado por el simulador."; }
        else payment.Status = PaymentStatus.Approved;
        await PublishResult(payment, command.CorrelationId); await db.SaveChangesAsync();
    }
    private Task PublishResult(Payment payment, Guid correlationId) => payment.Status switch
    {
        PaymentStatus.Approved => publish.Publish(new PaymentCompleted(payment.BookingId, payment.Id, payment.Amount, correlationId)),
        _ => publish.Publish(new PaymentRejected(payment.BookingId, payment.Id, payment.FailureReason ?? "Pago rechazado.", correlationId))
    };
}
public sealed class RequestRefundConsumer(PaymentsDbContext db, IOptions<GatewayOptions> options, IPublishEndpoint publish) : IConsumer<RequestRefund>
{
    public async Task Consume(ConsumeContext<RequestRefund> context)
    {
        var command = context.Message; var payment = await db.Payments.Include(x => x.Refunds).SingleOrDefaultAsync(x => x.Id == command.PaymentId && x.BookingId == command.BookingId);
        if (payment is null || payment.Status is not (PaymentStatus.Approved or PaymentStatus.RefundPending or PaymentStatus.RefundFailed)) { await publish.Publish(new RefundFailed(command.BookingId, command.PaymentId, "El pago no admite reembolso.", command.CorrelationId)); return; }
        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == command.IdempotencyKey);
        if (existing is not null) { await PublishResult(payment, existing, command.CorrelationId); await db.SaveChangesAsync(); return; }
        var refund = new Refund { PaymentId = payment.Id, IdempotencyKey = command.IdempotencyKey, Amount = command.Amount, Status = PaymentStatus.RefundPending }; db.Refunds.Add(refund); payment.Status = PaymentStatus.RefundPending; await db.SaveChangesAsync();
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.LatencySeconds, 0, 10)), context.CancellationToken);
        if (Random.Shared.NextDouble() < Math.Clamp(options.Value.FailureRate, 0, 1)) { refund.Status = PaymentStatus.RefundFailed; refund.FailureReason = "El simulador rechazó el reembolso."; payment.Status = PaymentStatus.RefundFailed; }
        else { refund.Status = PaymentStatus.Refunded; payment.Status = PaymentStatus.Refunded; }
        await PublishResult(payment, refund, command.CorrelationId); await db.SaveChangesAsync();
    }
    private Task PublishResult(Payment payment, Refund refund, Guid correlationId) => refund.Status == PaymentStatus.Refunded
        ? publish.Publish(new RefundCompleted(payment.BookingId, payment.Id, refund.Id, correlationId))
        : publish.Publish(new RefundFailed(payment.BookingId, payment.Id, refund.FailureReason ?? "Reembolso fallido.", correlationId));
}
