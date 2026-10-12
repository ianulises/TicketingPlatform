using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Api.Features;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/bookings").WithTags("Bookings");
        group.MapPost("/", Create); group.MapGet("/", Mine); group.MapGet("/{id:guid}", Get);
        group.MapPost("/{id:guid}/payments", Pay); group.MapPost("/{id:guid}/cancel", Cancel);
        app.MapGet("/tickets", Tickets).WithTags("Tickets");
        return app;
    }
    private static async Task<IResult> Create(CreateBookingRequest request, HttpContext http, BookingsDbContext db, IRequestClient<HoldSeats> client, TimeProvider clock, IPublishEndpoint publish)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(key)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["headers"] = ["X-User-Id e Idempotency-Key son obligatorios."] });
        if (request.SeatIds is not { Count: > 0 and <= 6 } || request.SeatIds.Distinct().Count() != request.SeatIds.Count) return Results.ValidationProblem(new Dictionary<string, string[]> { ["seatIds"] = ["Seleccione entre uno y seis asientos distintos."] });
        var existing = await db.Bookings.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key);
        if (existing is not null) return Results.Ok(ToResponse(existing));
        var now = clock.GetUtcNow(); var bookingId = Guid.NewGuid(); var correlationId = Guid.NewGuid(); var expiresAt = now.AddMinutes(10);
        var response = await client.GetResponse<SeatsHeld, SeatsUnavailable>(new HoldSeats(bookingId, request.EventId, request.SeatIds.ToArray(), expiresAt, correlationId));
        if (response.Is(out Response<SeatsUnavailable>? unavailable)) return Results.Conflict(new ProblemDetails { Title = "Asientos no disponibles", Detail = unavailable.Message.Reason, Status = 409 });
        if (!response.Is(out Response<SeatsHeld>? held)) return Results.Problem("No se obtuvo respuesta al apartar los asientos.", statusCode: 503);
        // El precio se copia a la reserva. Los cambios posteriores en la zona no alteran una compra en curso.
        var booking = new Booking { Id = bookingId, UserId = userId, IdempotencyKey = key, EventId = request.EventId, EventStartsAt = held.Message.EventStartsAt, CreatedAt = now, ExpiresAt = expiresAt, Status = BookingStatus.Held, Total = held.Message.Seats.Sum(x => x.Price), Items = held.Message.Seats.Select(x => new BookingItem { SeatId = x.SeatId, ZoneId = x.ZoneId, SeatLabel = x.Label, Price = x.Price }).ToList() };
        try { db.Bookings.Add(booking); await db.SaveChangesAsync(); }
        catch { await publish.Publish(new ReleaseSeats(bookingId, "No se pudo persistir la reserva.", correlationId)); throw; }
        return Results.Created($"/bookings/{booking.Id}", ToResponse(booking));
    }
    private static async Task<IResult> Mine(HttpContext http, BookingsDbContext db)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
        return Results.Ok((await db.Bookings.AsNoTracking().Include(x => x.Items).Include(x => x.Tickets).Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync()).Select(ToResponse));
    }
    private static async Task<IResult> Get(Guid id, HttpContext http, BookingsDbContext db)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); var booking = await db.Bookings.AsNoTracking().Include(x => x.Items).Include(x => x.Tickets).SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        return booking is null ? Results.NotFound() : Results.Ok(ToResponse(booking));
    }
    private static async Task<IResult> Pay(Guid id, HttpContext http, BookingsDbContext db, TimeProvider clock, IPublishEndpoint publish)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); var key = http.Request.Headers["Idempotency-Key"].ToString(); if (string.IsNullOrWhiteSpace(key)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Es obligatorio."] });
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId); if (booking is null) return Results.NotFound();
        if (booking.Status == BookingStatus.Confirmed) return Results.Ok(ToResponse(booking));
        if (BookingRules.IsExpired(booking, clock.GetUtcNow())) return Results.Conflict(new ProblemDetails { Title = "La reserva venció", Status = 409 });
        if (booking.Status is not (BookingStatus.Held or BookingStatus.PaymentRejected or BookingStatus.PaymentPending)) return Results.Conflict();
        if (booking.Status == BookingStatus.PaymentPending && booking.PaymentRequestKey != key) return Results.Conflict(new ProblemDetails { Title = "Ya existe un pago en proceso", Status = 409 });
        booking.Status = BookingStatus.PaymentPending; booking.PaymentRequestKey = key;
        // Publicar antes de SaveChanges permite que el bus outbox guarde el mensaje en la misma transacción.
        await publish.Publish(new RequestPayment(booking.Id, booking.UserId, booking.Total, key, Guid.NewGuid())); await db.SaveChangesAsync(); return Results.Accepted($"/bookings/{id}", ToResponse(booking));
    }
    private static async Task<IResult> Cancel(Guid id, HttpContext http, BookingsDbContext db, TimeProvider clock, IPublishEndpoint publish)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); var key = http.Request.Headers["Idempotency-Key"].ToString(); var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (booking is null) return Results.NotFound(); if (booking.Status == BookingStatus.Cancelled) return Results.Ok(ToResponse(booking));
        var canRetryRefund = booking.Status == BookingStatus.RefundFailed && booking.PaymentId.HasValue && booking.EventStartsAt - clock.GetUtcNow() >= TimeSpan.FromHours(48);
        if (!BookingRules.CanCancel(booking, clock.GetUtcNow()) && !canRetryRefund) return Results.Conflict(new ProblemDetails { Title = "La reserva debe estar confirmada y faltar al menos 48 horas.", Status = 409 });
        if (string.IsNullOrWhiteSpace(key)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Es obligatorio."] });
        // El asiento sigue vendido hasta confirmar el reembolso; liberarlo antes podría dejar dinero sin devolver.
        booking.Status = BookingStatus.RefundPending; await publish.Publish(new RequestRefund(booking.Id, booking.PaymentId.GetValueOrDefault(), booking.Total, key, Guid.NewGuid())); await db.SaveChangesAsync(); return Results.Accepted($"/bookings/{id}", ToResponse(booking));
    }
    private static async Task<IResult> Tickets(HttpContext http, BookingsDbContext db)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString(); if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
        return Results.Ok(await db.Tickets.AsNoTracking().Where(x => x.Booking.UserId == userId).Select(x => new { x.Id, x.BookingId, x.SeatId, x.Code, x.CancelledAt }).ToListAsync());
    }
    public static object ToResponse(Booking x) => new { x.Id, x.EventId, x.EventStartsAt, x.CreatedAt, x.ExpiresAt, Status = x.Status.ToString(), x.Total, Items = x.Items.Select(i => new { i.SeatId, i.ZoneId, i.SeatLabel, i.Price }), Tickets = x.Tickets.Select(t => new { t.Id, t.SeatId, t.Code, t.CancelledAt }) };
}
public sealed record CreateBookingRequest(Guid EventId, List<Guid> SeatIds);
