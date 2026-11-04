using EventHub.Web.Data.Entities;
using EventHub.Web.Services;

namespace EventHub.Tests;

/// <summary>報名狀態判斷規則（純函式，不需要啟動網站）。</summary>
public class EventAvailabilityTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 12, 0, 0);
    private static readonly DateTime Start = Now.AddDays(7);
    private static readonly DateTime End = Start.AddHours(3);

    private static RegistrationAvailability Evaluate(
        EventStatus status = EventStatus.Published,
        DateTime? start = null,
        DateTime? end = null,
        DateTime? deadline = null,
        int capacity = 10,
        int reserved = 0,
        DateTime? now = null) =>
        EventAvailability.Evaluate(status, start ?? Start, end ?? End, deadline, capacity, reserved, now ?? Now);

    [Fact]
    public void Published_with_seats_before_deadline_is_open() =>
        Assert.Equal(RegistrationAvailability.Open, Evaluate());

    [Fact]
    public void Draft_is_not_published() =>
        Assert.Equal(RegistrationAvailability.NotPublished, Evaluate(EventStatus.Draft));

    [Fact]
    public void Cancelled_takes_priority_over_everything() =>
        Assert.Equal(RegistrationAvailability.Cancelled,
            Evaluate(EventStatus.Cancelled, reserved: 10, now: End.AddDays(1)));

    [Theory]
    [InlineData(9, RegistrationAvailability.Open)]
    [InlineData(10, RegistrationAvailability.Full)]
    public void Full_when_reserved_reaches_capacity(int reserved, RegistrationAvailability expected) =>
        Assert.Equal(expected, Evaluate(capacity: 10, reserved: reserved));

    [Fact]
    public void Closed_after_registration_deadline() =>
        Assert.Equal(RegistrationAvailability.Closed, Evaluate(deadline: Now.AddMinutes(-1)));

    [Fact]
    public void Closed_exactly_at_deadline() =>
        Assert.Equal(RegistrationAvailability.Closed, Evaluate(deadline: Now));

    [Fact]
    public void Without_deadline_registration_closes_when_event_starts() =>
        Assert.Equal(RegistrationAvailability.Closed, Evaluate(now: Start.AddMinutes(1)));

    [Fact]
    public void Ended_after_end_time() =>
        Assert.Equal(RegistrationAvailability.Ended, Evaluate(now: End));

    [Fact]
    public void Closed_takes_priority_over_full() =>
        Assert.Equal(RegistrationAvailability.Closed,
            Evaluate(capacity: 10, reserved: 10, deadline: Now.AddMinutes(-1)));
}
