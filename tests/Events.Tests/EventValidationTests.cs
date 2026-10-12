using Events.Api.Features;

namespace Events.Tests;

public sealed class EventValidationTests
{
    [Fact]
    public void Rejects_event_without_zones()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var errors = EventEndpoints.Validate(new CreateEventRequest("Concierto", "Foro", now.AddDays(1), []), now);
        Assert.Contains("zones", errors.Keys);
    }

    [Fact]
    public void Accepts_future_event_with_numbered_seats()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var request = new CreateEventRequest("Concierto", "Foro", now.AddDays(1), [new("General", 250, ["A-1", "A-2"])]);
        Assert.Empty(EventEndpoints.Validate(request, now));
    }
}
