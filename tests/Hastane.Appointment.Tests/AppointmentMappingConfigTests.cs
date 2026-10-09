using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Appointment.Api.Mapping;
using Mapster;
using AppointmentEntity = Hastane.Appointment.Api.Entities.Appointment;

namespace Hastane.Appointment.Tests;

public class AppointmentMappingConfigTests
{
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(AppointmentMappingConfig).Assembly);
        config.RequireExplicitMapping = true;
        config.RequireDestinationMemberSource = true;
        return config;
    }

    [Fact]
    public void Configuration_Compiles()
    {
        StrictConfig().Compile();
    }

    [Fact]
    public void CreateDto_To_Entity_LeavesServerFieldsUnset()
    {
        var entity = new CreateAppointmentDto
        {
            DoctorId = "d1",
            PatientId = "p1",
            BranchId = "b1",
            AppointmentDate = new DateTime(2035, 1, 1, 10, 0, 0, DateTimeKind.Utc),
        }.Adapt<AppointmentEntity>(StrictConfig());

        Assert.Equal(Guid.Empty, entity.Id);
        Assert.Equal(default, entity.CreatedAt);
        Assert.Equal(AppointmentStatus.Pending, entity.Status);
        Assert.Equal("d1", entity.DoctorId);
        Assert.Null(entity.Detail);
    }

    [Fact]
    public void Entity_To_GetByIdDto_MapsDetail_AndNullWhenMissing()
    {
        var withDetail = new AppointmentEntity { DoctorId = "d", PatientId = "p", BranchId = "b" };
        withDetail.Detail = new AppointmentDetail { Complaint = "Baş ağrısı", AppointmentId = withDetail.Id };
        var without = new AppointmentEntity { DoctorId = "d", PatientId = "p", BranchId = "b" };

        var config = StrictConfig();
        Assert.Equal("Baş ağrısı", withDetail.Adapt<GetByIdAppointmentDto>(config).Detail!.Complaint);
        Assert.Null(without.Adapt<GetByIdAppointmentDto>(config).Detail);
    }
}
