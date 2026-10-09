using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Mapster;
using AppointmentEntity = Hastane.Appointment.Api.Entities.Appointment;

namespace Hastane.Appointment.Api.Mapping;

public class AppointmentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<AppointmentEntity, ResultAppointmentDto>();
        config.NewConfig<AppointmentEntity, GetByIdAppointmentDto>();
        config.NewConfig<AppointmentDetail, AppointmentDetailDto>();

        // Id, Status ve CreatedAt sunucu işi (serviste atanır); Create DTO'sunda zaten yoklar.
        config.NewConfig<CreateAppointmentDto, AppointmentEntity>()
            .Ignore(a => a.Id)
            .Ignore(a => a.PatientId) // token'dan atanır
            .Ignore(a => a.Status)
            .Ignore(a => a.CreatedAt)
            .Ignore(a => a.Detail!);

        config.NewConfig<UpsertAppointmentDetailDto, AppointmentDetail>()
            .Ignore(d => d.Id)
            .Ignore(d => d.AppointmentId)
            .Ignore(d => d.Appointment!)
            .Ignore(d => d.UpdatedAt!);

        // Yeniden planlama (UpdateAppointmentDto) için eşleme yok: serviste yalnız BranchId ve AppointmentDate atanır.
    }
}
