using Hastane.Notification.Api.Dtos;
using Hastane.Notification.Api.Entities;
using Hastane.Notification.Api.Mapping;
using Hastane.Notification.Api.Security;
using Hastane.Notification.Api.Validation;
using Mapster;

namespace Hastane.Notification.Tests;

// Konteyner gerektirmeyen birim testler: bağlantı/e-posta doğrulayıcıları, anahtar karşılaştırma, eşleme.
public class ValidationAndMappingTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("/", true)]
    [InlineData("/Patient/Appointments/Details/5", true)]
    [InlineData("/a?b=c#d", true)]
    [InlineData("//evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("\\\\evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://evil.com", false)]
    [InlineData("evil.com", false)]
    [InlineData("/a\\b", false)]
    [InlineData("/a\tb", false)]
    [InlineData("/a\nb", false)]
    public void RelativeLink_AcceptsOnlyAppRelativePaths(string? link, bool expected)
        => Assert.Equal(expected, RelativeLinkAttribute.IsValidLink(link));

    [Fact]
    public void RelativeLink_RejectsOver500Characters()
    {
        Assert.True(RelativeLinkAttribute.IsValidLink("/" + new string('a', 499)));
        Assert.False(RelativeLinkAttribute.IsValidLink("/" + new string('a', 500)));
    }

    [Theory]
    [InlineData("hasta@example.com", true)]
    [InlineData("ad.soyad+etiket@alt.example.org", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("hasta", false)]
    [InlineData("Ali <ali@example.com>", false)]
    [InlineData("a@b.com,c@d.com", false)]
    [InlineData("a@b.com\r\nBcc: x@y.com", false)]
    [InlineData("a b@example.com", false)]
    public void SingleLineEmail_AcceptsOnlyPlainSingleAddress(string? address, bool expected)
        => Assert.Equal(expected, SingleLineEmailAttribute.IsValidAddress(address));

    [Theory]
    [InlineData("anahtar", "anahtar", true)]
    [InlineData("anahtar", "Anahtar", false)]
    [InlineData("anahtar", "anahtar2", false)]
    [InlineData("anahtar", "", false)]
    [InlineData("anahtar", null, false)]
    [InlineData("", "", false)]       // ayar boş: boş başlık bile geçemez
    [InlineData("", "x", false)]
    [InlineData(null, "x", false)]
    [InlineData("   ", "   ", false)] // yalnız boşluk da "tanımlı" sayılmaz
    public void InternalApiKey_KeysMatch_NeverMatchesWhenSettingEmpty(string? configured, string? provided, bool expected)
        => Assert.Equal(expected, InternalApiKeyAttribute.KeysMatch(configured, provided));

    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(NotificationMappingConfig).Assembly);
        config.RequireExplicitMapping = true;
        config.RequireDestinationMemberSource = true;
        return config;
    }

    [Fact]
    public void Mapping_Configuration_Compiles() => StrictConfig().Compile();

    [Fact]
    public void Entity_To_ResultDto_DropsRecipientIds_AndKeepsFields()
    {
        var entity = new AppNotification
        {
            Id = "64b7f0f0f0f0f0f0f0f0f0f0",
            UserId = "secret-user",
            DoctorId = null,
            Title = "t",
            Message = "m",
            Link = "/x",
            Kind = "info",
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };

        var dto = entity.Adapt<ResultNotificationDto>(StrictConfig());

        Assert.Equal(entity.Id, dto.Id);
        Assert.Equal("t", dto.Title);
        Assert.Equal("/x", dto.Link);
        Assert.Null(dto.ReadAt);
        Assert.DoesNotContain(typeof(ResultNotificationDto).GetProperties(), p => p.Name is "UserId" or "DoctorId");
    }

    [Fact]
    public void CreateDto_To_Entity_DefaultsKindToInfo_AndLeavesServerFieldsUnset()
    {
        var dto = new CreateNotificationDto { UserId = "u", Title = "t", Message = "m", Kind = null };

        var entity = dto.Adapt<AppNotification>(StrictConfig());

        Assert.Equal("info", entity.Kind);
        Assert.Equal("u", entity.UserId);
        Assert.Null(entity.Id);
        Assert.Equal(default, entity.CreatedAt);
        Assert.Null(entity.ReadAt);
    }
}
