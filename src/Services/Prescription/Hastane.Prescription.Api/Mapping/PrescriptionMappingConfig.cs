using Hastane.Prescription.Api.Dtos;
using Hastane.Prescription.Api.Entities;
using Mapster;
using PrescriptionEntity = Hastane.Prescription.Api.Entities.Prescription;

namespace Hastane.Prescription.Api.Mapping;

public class PrescriptionMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Liste için ProjectToType ile kullanılır: bu ifade SQL'e COUNT alt sorgusu olarak çevrilir,
        // kalemler veritabanından çekilmez.
        config.NewConfig<PrescriptionEntity, ResultPrescriptionDto>()
            .Map(d => d.ItemCount, s => s.Items.Count);

        config.NewConfig<PrescriptionEntity, GetByIdPrescriptionDto>();
        config.NewConfig<PrescriptionItem, PrescriptionItemDto>();

        // Id, IssuedAt ve kalem Id'leri sunucu işi (serviste atanır); Create DTO'sunda zaten yoklar.
        config.NewConfig<CreatePrescriptionDto, PrescriptionEntity>()
            .Ignore(p => p.Id)
            .Ignore(p => p.IssuedAt);

        config.NewConfig<CreatePrescriptionItemDto, PrescriptionItem>()
            .Ignore(i => i.Id)
            .Ignore(i => i.PrescriptionId)
            .Ignore(i => i.LineNo)
            .Ignore(i => i.Prescription!);

        // Güncelleme (UpdatePrescriptionDto) için eşleme yok: serviste yalnız Notes ve kalem listesi atanır.
    }
}
