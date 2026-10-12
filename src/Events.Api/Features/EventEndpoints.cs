using Microsoft.EntityFrameworkCore;

namespace Events.Api.Features;

public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/events").WithTags("Events");
        group.MapPost("/", Create); group.MapGet("/", List); group.MapGet("/{id:guid}", Get);
        group.MapGet("/{id:guid}/availability", Availability);
        group.MapPut("/{eventId:guid}/zones/{zoneId:guid}/price", ChangePrice);
        return app;
    }
    private static async Task<IResult> Create(CreateEventRequest request, HttpContext http, EventsDbContext db, TimeProvider clock)
    {
        if (!http.Request.Headers.TryGetValue("X-User-Id", out var userId) || !string.Equals(http.Request.Headers["X-User-Role"], "organizer", StringComparison.OrdinalIgnoreCase))
            return Results.Problem("Se requiere identidad y rol organizer.", statusCode: 403);
        var errors = Validate(request, clock.GetUtcNow()); if (errors.Count > 0) return Results.ValidationProblem(errors);
        var entity = new Event { Name = request.Name.Trim(), Venue = request.Venue.Trim(), StartsAt = request.StartsAt, OrganizerId = userId.ToString(), Zones = request.Zones.Select(z => new Zone { Name = z.Name.Trim(), Price = z.Price, Seats = z.SeatLabels.Distinct(StringComparer.OrdinalIgnoreCase).Select(label => new Seat { Label = label.Trim() }).ToList() }).ToList() };
        db.Events.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/events/{entity.Id}", ToResponse(entity));
    }
    private static async Task<IResult> List(EventsDbContext db) => Results.Ok(await db.Events.AsNoTracking().OrderBy(x => x.StartsAt).Select(x => new { x.Id, x.Name, x.Venue, x.StartsAt }).ToListAsync());
    private static async Task<IResult> Get(Guid id, EventsDbContext db)
    {
        var entity = await db.Events.AsNoTracking().Include(x => x.Zones).ThenInclude(x => x.Seats).SingleOrDefaultAsync(x => x.Id == id);
        return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
    }
    private static async Task<IResult> Availability(Guid id, EventsDbContext db, TimeProvider clock)
    {
        var now = clock.GetUtcNow(); if (!await db.Events.AnyAsync(x => x.Id == id)) return Results.NotFound();
        var zones = await db.Zones.AsNoTracking().Where(x => x.EventId == id).Select(z => new { z.Id, z.Name, z.Price, Seats = z.Seats.OrderBy(s => s.Label).Select(s => new { s.Id, s.Label, Available = s.Status == SeatStatus.Available || s.Status == SeatStatus.Held && s.HeldUntil <= now }) }).ToListAsync();
        return Results.Ok(zones);
    }
    private static async Task<IResult> ChangePrice(Guid eventId, Guid zoneId, ChangePriceRequest request, HttpContext http, EventsDbContext db)
    {
        var userId = http.Request.Headers["X-User-Id"].ToString();
        if (request.Price < 0 || string.IsNullOrWhiteSpace(userId) || !string.Equals(http.Request.Headers["X-User-Role"], "organizer", StringComparison.OrdinalIgnoreCase)) return Results.Problem("Solicitud o permisos inválidos.", statusCode: 403);
        var zone = await db.Zones.Include(x => x.Event).SingleOrDefaultAsync(x => x.Id == zoneId && x.EventId == eventId);
        if (zone is null) return Results.NotFound(); if (zone.Event.OrganizerId != userId) return Results.Forbid();
        zone.Price = request.Price; await db.SaveChangesAsync(); return Results.NoContent();
    }
    public static Dictionary<string, string[]> Validate(CreateEventRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["El nombre es obligatorio."];
        if (string.IsNullOrWhiteSpace(request.Venue)) errors["venue"] = ["El recinto es obligatorio."];
        if (request.StartsAt <= now) errors["startsAt"] = ["La fecha debe estar en el futuro."];
        if (request.Zones.Count == 0 || request.Zones.Any(z => z.Price < 0 || z.SeatLabels.Count == 0)) errors["zones"] = ["Se requiere al menos una zona con precio válido y asientos."];
        return errors;
    }
    private static object ToResponse(Event x) => new { x.Id, x.Name, x.Venue, x.StartsAt, Zones = x.Zones.Select(z => new { z.Id, z.Name, z.Price, Seats = z.Seats.Select(s => new { s.Id, s.Label }) }) };
}
public sealed record CreateEventRequest(string Name, string Venue, DateTimeOffset StartsAt, List<CreateZoneRequest> Zones);
public sealed record CreateZoneRequest(string Name, decimal Price, List<string> SeatLabels);
public sealed record ChangePriceRequest(decimal Price);
