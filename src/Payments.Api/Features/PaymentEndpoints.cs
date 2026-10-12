using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Payments.Api.Features;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payments").WithTags("Payments"); group.MapPost("/", Request); group.MapGet("/{bookingId:guid}", Get); return app;
    }
    private static async Task<IResult> Request(PaymentRequest request, HttpContext http, IPublishEndpoint publish)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); var key = http.Request.Headers["Idempotency-Key"].ToString(); if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(key) || request.Amount <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Identidad, idempotencia e importe positivo son obligatorios."] });
        await publish.Publish(new RequestPayment(request.BookingId, userId, request.Amount, key, Guid.NewGuid())); return Results.Accepted($"/payments/{request.BookingId}");
    }
    private static async Task<IResult> Get(Guid bookingId, HttpContext http, PaymentsDbContext db)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); var payment = await db.Payments.AsNoTracking().Include(x => x.Refunds).SingleOrDefaultAsync(x => x.BookingId == bookingId && x.UserId == userId);
        return payment is null ? Results.NotFound() : Results.Ok(new { payment.Id, payment.BookingId, payment.Amount, Status = payment.Status.ToString(), payment.CreatedAt, payment.FailureReason, Refunds = payment.Refunds.Select(x => new { x.Id, x.Amount, Status = x.Status.ToString(), x.FailureReason }) });
    }
}
public sealed record PaymentRequest(Guid BookingId, decimal Amount);
