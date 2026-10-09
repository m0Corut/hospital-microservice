using System.Reflection;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Mapping;
using Mapster;
using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Tests;

public class DoctorMappingConfigTests
{
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(DoctorMappingConfig).Assembly);
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
    public void CreateDto_To_Entity_IgnoresId_AndMapsNestedLists()
    {
        var entity = new CreateDoctorDto
        {
            FirstName = "Ayşe",
            LastName = "Yılmaz",
            BranchId = "b1",
            Educations = [new EducationDto { School = "İTÜ", Department = "Tıp", StartYear = 2000, EndYear = 2006 }],
            ClinicLocations = [new ClinicLocationDto { ClinicName = "Merkez", City = "Ankara" }],
        }.Adapt<DoctorEntity>(StrictConfig());

        Assert.Null(entity.Id);
        Assert.Equal("Ayşe", entity.FirstName);
        Assert.Equal("İTÜ", Assert.Single(entity.Educations).School);
        Assert.Equal("Ankara", Assert.Single(entity.ClinicLocations).City);
    }

    [Fact]
    public void ResultDto_DoesNotExposePhoneOrEmail()
    {
        var names = typeof(ResultDoctorDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToList();
        Assert.DoesNotContain("Phone", names);
        Assert.DoesNotContain("Email", names);
        Assert.Contains("Phone", typeof(GetByIdDoctorDto).GetProperties().Select(p => p.Name));
    }
}
