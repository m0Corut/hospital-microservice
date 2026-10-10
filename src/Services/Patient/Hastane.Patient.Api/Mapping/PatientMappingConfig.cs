using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Entities;
using Mapster;

namespace Hastane.Patient.Api.Mapping;

public class PatientMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Yalnız çıkış yönü: UpdatePatientProfileDto -> PatientProfile eşlemesi bilerek yok; servis alanları tek tek atar.
        // Age burada değil serviste (şimdiki zaman gerekir).
        config.NewConfig<PatientProfile, PatientProfileDto>()
            .Ignore(d => d.Age)
            .Map(d => d.BloodTypeDisplay, s => BloodTypes.Display(s.BloodType));

        config.NewConfig<FavoriteDoctor, FavoriteDoctorDto>();
    }
}
