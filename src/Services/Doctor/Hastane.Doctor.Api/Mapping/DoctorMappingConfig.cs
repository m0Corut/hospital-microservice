using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using Mapster;
using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Api.Mapping;

public class DoctorMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // İç içe sınıflar: sıkı modda her çift açıkça yazılmalı.
        config.NewConfig<Education, EducationDto>();
        config.NewConfig<EducationDto, Education>();
        config.NewConfig<Experience, ExperienceDto>();
        config.NewConfig<ExperienceDto, Experience>();
        config.NewConfig<Award, AwardDto>();
        config.NewConfig<AwardDto, Award>();
        config.NewConfig<ScheduleSlot, ScheduleSlotDto>();
        config.NewConfig<ScheduleSlotDto, ScheduleSlot>();
        config.NewConfig<SocialLinks, SocialLinksDto>();
        config.NewConfig<SocialLinksDto, SocialLinks>();
        config.NewConfig<ClinicLocation, ClinicLocationDto>();
        config.NewConfig<ClinicLocationDto, ClinicLocation>();

        // Türetilmiş alanlar (MinFee...) kaynakta yok: hesaplama açıkça yazılır, saklanmaz.
        config.NewConfig<DoctorEntity, ResultDoctorDto>()
            .Map(d => d.PrimaryCity, s => DoctorDerived.PrimaryCity(s.ClinicLocations))
            .Map(d => d.MinFee, s => DoctorDerived.MinFee(s.ClinicLocations))
            .Map(d => d.MaxFee, s => DoctorDerived.MaxFee(s.ClinicLocations))
            .Map(d => d.Currency, s => DoctorDerived.Currency(s.ClinicLocations));
        config.NewConfig<DoctorEntity, GetByIdDoctorDto>()
            .Map(d => d.PrimaryCity, s => DoctorDerived.PrimaryCity(s.ClinicLocations))
            .Map(d => d.MinFee, s => DoctorDerived.MinFee(s.ClinicLocations))
            .Map(d => d.MaxFee, s => DoctorDerived.MaxFee(s.ClinicLocations))
            .Map(d => d.Currency, s => DoctorDerived.Currency(s.ClinicLocations));
        config.NewConfig<DoctorEntity, DoctorSearchItemDto>()
            .Map(d => d.Gallery, s => s.Gallery.Take(4).ToList())
            .Map(d => d.PrimaryCity, s => DoctorDerived.PrimaryCity(s.ClinicLocations))
            .Map(d => d.MinFee, s => DoctorDerived.MinFee(s.ClinicLocations))
            .Map(d => d.MaxFee, s => DoctorDerived.MaxFee(s.ClinicLocations))
            .Map(d => d.Currency, s => DoctorDerived.Currency(s.ClinicLocations));
        config.NewConfig<DoctorEntity, DoctorSummaryDto>();

        // Create'te Id gelmez; Mongo üretir. Gender DTO'da nullable ama doğrulama (DTO + servis) null'ı zaten reddetti.
        config.NewConfig<CreateDoctorDto, DoctorEntity>()
            .Ignore(d => d.Id!)
            .Map(d => d.Gender, s => s.Gender!.Value);
        config.NewConfig<UpdateDoctorDto, DoctorEntity>()
            .Map(d => d.Gender, s => s.Gender!.Value);
    }
}
