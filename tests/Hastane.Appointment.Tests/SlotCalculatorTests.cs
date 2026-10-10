using Hastane.Appointment.Api.Integration;
using Hastane.Appointment.Api.Services;

namespace Hastane.Appointment.Tests;

// Saf hesap testleri: veritabanı yok, ağ yok, Docker yok.
public class SlotCalculatorTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static DoctorSchedule Schedule(params ScheduleLocation[] locations) => new([.. locations]);

    private static ScheduleLocation Location(string name, string city, params ScheduleEntry[] entries)
        => new(name, city, [.. entries]);

    // 2030-01-07 bir Pazartesi.
    private static readonly DateOnly Monday = new(2030, 1, 7);

    [Fact]
    public void Istanbul_0900_IsConvertedTo_0600Utc()
    {
        var schedule = Schedule(Location("A", "Ankara", new ScheduleEntry(DayOfWeek.Monday, "09:00", "10:00", 30)));

        var slots = SlotCalculator.SlotsForLocalDate(schedule, Monday, Istanbul);

        Assert.Equal(
            [new DateTime(2030, 1, 7, 6, 0, 0, DateTimeKind.Utc), new DateTime(2030, 1, 7, 6, 30, 0, DateTimeKind.Utc)],
            slots.Select(s => s.Start));
        Assert.Equal(slots.Select(s => s.Start.AddMinutes(30)), slots.Select(s => s.End));
    }

    [Fact]
    public void LastSlot_MustFitInsideTheRange()
    {
        // 09:00-10:10, 30 dk: 09:00 ve 09:30 sığar, 10:00 (bitişi 10:30) sığmaz.
        var schedule = Schedule(Location("A", "Ankara", new ScheduleEntry(DayOfWeek.Monday, "09:00", "10:10", 30)));

        Assert.Equal(2, SlotCalculator.SlotsForLocalDate(schedule, Monday, Istanbul).Count);
    }

    [Fact]
    public void OtherWeekday_ReturnsNothing()
    {
        var schedule = Schedule(Location("A", "Ankara", new ScheduleEntry(DayOfWeek.Monday, "09:00", "12:00", 30)));

        Assert.Empty(SlotCalculator.SlotsForLocalDate(schedule, Monday.AddDays(1), Istanbul));
    }

    [Fact]
    public void MultipleClinics_AreMergedAndSortedByStart()
    {
        var schedule = Schedule(
            Location("Öğleden sonra", "İzmir", new ScheduleEntry(DayOfWeek.Monday, "14:00", "15:00", 60)),
            Location("Sabah", "Ankara", new ScheduleEntry(DayOfWeek.Monday, "09:00", "10:00", 60)));

        var slots = SlotCalculator.SlotsForLocalDate(schedule, Monday, Istanbul);

        Assert.Equal(["Sabah", "Öğleden sonra"], slots.Select(s => s.ClinicName));
        Assert.Equal(["Ankara", "İzmir"], slots.Select(s => s.City));
    }

    [Fact]
    public void SameStartInTwoClinics_FirstClinicWins()
    {
        var schedule = Schedule(
            Location("Birinci", "Ankara", new ScheduleEntry(DayOfWeek.Monday, "09:00", "10:00", 60)),
            Location("İkinci", "İzmir", new ScheduleEntry(DayOfWeek.Monday, "09:00", "10:00", 60)));

        var slot = Assert.Single(SlotCalculator.SlotsForLocalDate(schedule, Monday, Istanbul));
        Assert.Equal("Birinci", slot.ClinicName);
    }

    [Fact]
    public void GarbageEntries_AreSkippedNotThrown()
    {
        var schedule = Schedule(Location("A", "Ankara",
            new ScheduleEntry(DayOfWeek.Monday, "9", "10:00", 30),
            new ScheduleEntry(DayOfWeek.Monday, "09:00", "10:00", 0),
            new ScheduleEntry(DayOfWeek.Monday, "11:00", "12:00", 60)));

        var slot = Assert.Single(SlotCalculator.SlotsForLocalDate(schedule, Monday, Istanbul));
        Assert.Equal(new DateTime(2030, 1, 7, 8, 0, 0, DateTimeKind.Utc), slot.Start);
    }

    [Fact]
    public void DaylightSavingGap_LocalTimeThatDoesNotExistIsSkipped()
    {
        // New York 2030-03-10 02:30 yerel saat yok (02:00 -> 03:00 atlar).
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var sunday = new DateOnly(2030, 3, 10);
        var schedule = Schedule(Location("A", "NY", new ScheduleEntry(DayOfWeek.Sunday, "02:00", "04:00", 30)));

        var slots = SlotCalculator.SlotsForLocalDate(schedule, sunday, newYork);

        // 02:00, 02:30 yok; 03:00 ve 03:30 var.
        Assert.Equal(2, slots.Count);
    }
}
