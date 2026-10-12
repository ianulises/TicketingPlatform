namespace Events.Api;

public enum SeatStatus { Available, Held, Sold }
public sealed class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Venue { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public required string OrganizerId { get; set; }
    public List<Zone> Zones { get; set; } = [];
}
public sealed class Zone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public Event Event { get; set; } = null!;
    public List<Seat> Seats { get; set; } = [];
}
public sealed class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ZoneId { get; set; }
    public required string Label { get; set; }
    public SeatStatus Status { get; set; }
    public Guid? BookingId { get; set; }
    public DateTimeOffset? HeldUntil { get; set; }
    public Zone Zone { get; set; } = null!;
}
