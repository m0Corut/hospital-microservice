using System.Reflection;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
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
            Gender = DoctorGender.Female,
            BranchId = "b1",
            Degrees = "BDS",
            Services = ["Dolgu"],
            Gallery = ["https://x.test/1.jpg"],
            SocialLinks = new SocialLinksDto { LinkedIn = "https://linkedin.com/in/a" },
            Educations = [new EducationDto { School = "İTÜ", Department = "Tıp", StartYear = 2000, EndYear = 2006 }],
            ClinicLocations =
            [
                new ClinicLocationDto
                {
                    ClinicName = "Merkez", City = "Ankara", HourlyFee = 250.5m, Currency = "USD",
                    Schedule = [new ScheduleSlotDto { DayOfWeek = DayOfWeek.Monday, StartTime = "09:00", EndTime = "12:00", SlotMinutes = 20 }],
                },
            ],
        }.Adapt<DoctorEntity>(StrictConfig());

        Assert.Null(entity.Id);
        Assert.Equal("Ayşe", entity.FirstName);
        Assert.Equal(DoctorGender.Female, entity.Gender);
        Assert.Equal("BDS", entity.Degrees);
        Assert.Equal("Dolgu", Assert.Single(entity.Services));
        Assert.Equal("https://linkedin.com/in/a", entity.SocialLinks.LinkedIn);
        Assert.Equal("İTÜ", Assert.Single(entity.Educations).School);
        var loc = Assert.Single(entity.ClinicLocations);
        Assert.Equal("Ankara", loc.City);
        Assert.Equal(250.5m, loc.HourlyFee);
        Assert.Equal("USD", loc.Currency);
        var slot = Assert.Single(loc.Schedule);
        Assert.Equal((DayOfWeek.Monday, "09:00", "12:00", 20), (slot.DayOfWeek, slot.StartTime, slot.EndTime, slot.SlotMinutes));
    }

    [Fact]
    public void Entity_To_Results_ComputesDerivedFees_WithoutStoringThem()
    {
        var entity = new DoctorEntity
        {
            Id = "x",
            Gallery = ["1", "2", "3", "4", "5", "6"],
            ClinicLocations =
            [
                new ClinicLocation { City = "Ankara", HourlyFee = 300m, Currency = "TRY" },
                new ClinicLocation { City = "İzmir", HourlyFee = 100m, Currency = "TRY" },
                new ClinicLocation { City = "Bursa", HourlyFee = 5m, Currency = "USD" },
                new ClinicLocation { City = "Adana" },
            ],
        };
        var config = StrictConfig();
        var item = entity.Adapt<DoctorSearchItemDto>(config);
        Assert.Equal(100m, item.MinFee);
        Assert.Equal(300m, item.MaxFee);
        Assert.Equal("TRY", item.Currency);
        Assert.Equal("Ankara", item.PrimaryCity);
        Assert.Equal(4, item.Gallery.Count);

        var none = new DoctorEntity { ClinicLocations = [new ClinicLocation { City = "X" }] }.Adapt<ResultDoctorDto>(config);
        Assert.Null(none.MinFee);
        Assert.Null(none.Currency);

        // Türetilmiş alanlar entity'de YOK (saklanmaz).
        var entityProps = typeof(DoctorEntity).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("MinFee", entityProps);
        Assert.DoesNotContain("PrimaryCity", entityProps);
    }

    private static List<ClinicLocation> Locations(params (decimal? Fee, string Currency)[] items)
        => items.Select(i => new ClinicLocation { City = "X", HourlyFee = i.Fee, Currency = i.Currency }).ToList();

    [Fact]
    public void DerivedFee_ZeroFeeIsUndefined_AndIgnored()
    {
        var locations = Locations((0m, "TRY"), (200m, "TRY"));
        Assert.Equal(200m, DoctorDerived.MinFee(locations));
        Assert.Equal(200m, DoctorDerived.MaxFee(locations));
        Assert.Equal("TRY", DoctorDerived.Currency(locations));
    }

    [Fact]
    public void DerivedFee_OnlyZeroFee_IsNull()
    {
        var locations = Locations((0m, "TRY"));
        Assert.Null(DoctorDerived.MinFee(locations));
        Assert.Null(DoctorDerived.Currency(locations));
    }

    [Fact]
    public void DerivedFee_MixedCurrencies_UsesFirstPricedLocationsCurrency()
    {
        // Sıfır ücretli ilk konum atlanır; ilk ÜCRETLİ konum USD: min yalnız USD'ler arasında (5 TRY daha ucuz görünse de).
        var locations = Locations((0m, "EUR"), (50m, "USD"), (5m, "TRY"), (40m, "USD"));
        Assert.Equal(40m, DoctorDerived.MinFee(locations));
        Assert.Equal("USD", DoctorDerived.Currency(locations));
    }

    [Theory]
    [InlineData(typeof(ResultDoctorDto))]
    [InlineData(typeof(DoctorSearchItemDto))]
    [InlineData(typeof(DoctorSummaryDto))]
    public void PublicDtos_DoNotExposePhoneOrEmail(Type dto)
    {
        var names = dto.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        Assert.DoesNotContain("Phone", names);
        Assert.DoesNotContain("Email", names);
        Assert.Contains("Phone", typeof(GetByIdDoctorDto).GetProperties().Select(p => p.Name));
    }
}
