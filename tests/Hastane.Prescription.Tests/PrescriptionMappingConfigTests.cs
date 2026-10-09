using Hastane.Prescription.Api.Dtos;
using Hastane.Prescription.Api.Entities;
using Hastane.Prescription.Api.Mapping;
using Mapster;
using PrescriptionEntity = Hastane.Prescription.Api.Entities.Prescription;

namespace Hastane.Prescription.Tests;

public class PrescriptionMappingConfigTests
{
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(PrescriptionMappingConfig).Assembly);
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
    public void CreateDto_To_Entity_MapsItems_AndLeavesServerFieldsUnset()
    {
        var entity = new CreatePrescriptionDto
        {
            AppointmentId = Guid.NewGuid(),
            DoctorId = "d1",
            PatientId = "p1",
            Notes = "not",
            Items = [new CreatePrescriptionItemDto { MedicineName = "Parol", Dosage = "500 mg", Frequency = "günde 3 kez", DurationDays = 5 }],
        }.Adapt<PrescriptionEntity>(StrictConfig());

        Assert.Equal(Guid.Empty, entity.Id);
        Assert.Equal(default, entity.IssuedAt);
        // DoctorId eşlenmez: token'daki doctor_id'den serviste atanır (gövdedeki değer güvenilmez).
        Assert.Null(entity.DoctorId);
        var item = Assert.Single(entity.Items);
        Assert.Equal(Guid.Empty, item.Id);
        Assert.Equal(Guid.Empty, item.PrescriptionId);
        Assert.Equal("Parol", item.MedicineName);
    }

    [Fact]
    public void Entity_To_Dtos_MapItemsAndItemCount()
    {
        var entity = new PrescriptionEntity { DoctorId = "d", PatientId = "p", Id = Guid.NewGuid() };
        entity.Items.Add(new PrescriptionItem { MedicineName = "A", Dosage = "1", Frequency = "f", DurationDays = 1 });
        entity.Items.Add(new PrescriptionItem { MedicineName = "B", Dosage = "1", Frequency = "f", DurationDays = 2 });

        var config = StrictConfig();
        Assert.Equal(2, entity.Adapt<GetByIdPrescriptionDto>(config).Items.Count);
        Assert.Equal(2, entity.Adapt<ResultPrescriptionDto>(config).ItemCount);
    }
}
