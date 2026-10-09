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
        config.NewConfig<ClinicLocation, ClinicLocationDto>();
        config.NewConfig<ClinicLocationDto, ClinicLocation>();

        config.NewConfig<DoctorEntity, ResultDoctorDto>();
        config.NewConfig<DoctorEntity, GetByIdDoctorDto>();
        // Create'te Id gelmez; Mongo üretir.
        config.NewConfig<CreateDoctorDto, DoctorEntity>().Ignore(d => d.Id!);
        config.NewConfig<UpdateDoctorDto, DoctorEntity>();
    }
}
