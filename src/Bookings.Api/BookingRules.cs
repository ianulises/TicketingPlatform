namespace Bookings.Api;

public static class BookingRules
{
    public static bool IsExpired(Booking booking, DateTimeOffset now) => booking.ExpiresAt <= now;
    public static bool CanCancel(Booking booking, DateTimeOffset now) => booking.Status == BookingStatus.Confirmed && booking.PaymentId.HasValue && booking.EventStartsAt - now >= TimeSpan.FromHours(48);
}
