using Hastane.Appointment.Api.Entities;
using Hastane.Appointment.Api.Services;

namespace Hastane.Appointment.Tests;

public class AppointmentStatusTransitionsTests
{
    public static TheoryData<AppointmentStatus, AppointmentStatus, bool> AllPairs()
    {
        var valid = new HashSet<(AppointmentStatus, AppointmentStatus)>
        {
            (AppointmentStatus.Pending, AppointmentStatus.Confirmed),
            (AppointmentStatus.Pending, AppointmentStatus.Cancelled),
            (AppointmentStatus.Confirmed, AppointmentStatus.Completed),
            (AppointmentStatus.Confirmed, AppointmentStatus.Cancelled),
        };
        var data = new TheoryData<AppointmentStatus, AppointmentStatus, bool>();
        foreach (var from in Enum.GetValues<AppointmentStatus>())
            foreach (var to in Enum.GetValues<AppointmentStatus>())
                data.Add(from, to, valid.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_MatchesTable(AppointmentStatus from, AppointmentStatus to, bool expected)
        => Assert.Equal(expected, AppointmentStatusTransitions.CanTransition(from, to));

    [Theory]
    [InlineData(AppointmentStatus.Pending, false)]
    [InlineData(AppointmentStatus.Confirmed, false)]
    [InlineData(AppointmentStatus.Completed, true)]
    [InlineData(AppointmentStatus.Cancelled, true)]
    public void IsFinal_OnlyCompletedAndCancelled(AppointmentStatus status, bool expected)
        => Assert.Equal(expected, AppointmentStatusTransitions.IsFinal(status));
}
