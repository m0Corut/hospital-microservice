using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Identity.Api.Mapping;
using Mapster;

namespace Hastane.Identity.Tests;

public class UserMappingConfigTests
{
    // Sıkı mod: açık eşleme zorunlu, hedefteki her alanın kaynağı olmalı (ya da bilerek Ignore edilmeli).
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(UserMappingConfig).Assembly);
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
    public void AppUser_To_Summary_CopiesPublicFields_AndLeavesRolesEmpty()
    {
        var user = new AppUser
        {
            Id = "abc",
            FirstName = "Ayşe",
            LastName = "Yılmaz",
            Email = "ayse@example.com",
            PasswordHash = "gizli-hash",
            SecurityStamp = "gizli-damga",
        };

        var dto = user.Adapt<UserSummaryDto>(StrictConfig());

        Assert.Equal("abc", dto.Id);
        Assert.Equal("Ayşe", dto.FirstName);
        Assert.Equal("Yılmaz", dto.LastName);
        Assert.Equal("ayse@example.com", dto.Email);
        Assert.Empty(dto.Roles);
    }

    [Fact]
    public void AppUser_To_Summary_CopiesDoctorLink()
    {
        var user = new AppUser { Id = "abc", Email = "d@b.co", DoctorId = "665f1c2e9b1d4a0012345678" };

        var dto = user.Adapt<UserSummaryDto>(StrictConfig());

        Assert.Equal("665f1c2e9b1d4a0012345678", dto.DoctorId);
        Assert.Null(new AppUser { Email = "x@y.zz" }.Adapt<UserSummaryDto>(StrictConfig()).DoctorId);
    }
}
