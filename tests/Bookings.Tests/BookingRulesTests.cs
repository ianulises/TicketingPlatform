using Bookings.Api;

namespace Bookings.Tests;

public sealed class BookingRulesTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reservation_expires_exactly_at_deadline()
    {
        var booking = Booking(BookingStatus.Held, Now);
        Assert.True(BookingRules.IsExpired(booking, Now));
    }

    [Theory]
    [InlineData(48, true)]
    [InlineData(47, false)]
    public void Cancellation_observes_48_hour_boundary(int hours, bool expected)
    {
        var booking = Booking(BookingStatus.Confirmed, Now.AddHours(hours)); booking.PaymentId = Guid.NewGuid();
        Assert.Equal(expected, BookingRules.CanCancel(booking, Now));
    }

    private static Booking Booking(BookingStatus status, DateTimeOffset eventDate) => new() { UserId = "user", IdempotencyKey = "key", EventStartsAt = eventDate, CreatedAt = Now, ExpiresAt = Now, Status = status };
}
