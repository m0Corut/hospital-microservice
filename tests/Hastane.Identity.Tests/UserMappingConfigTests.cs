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
    public void Summary_To_LoginResult_CopiesFields_AndLeavesMessageEmpty()
    {
        var summary = new UserSummaryDto
        {
            Id = "abc",
            FirstName = "Ayşe",
            LastName = "Yılmaz",
            Email = "a@b.co",
            Roles = ["Patient"],
        };

        var result = summary.Adapt<LoginResultDto>(StrictConfig());

        Assert.Equal(["Patient"], result.Roles);
        Assert.Equal("a@b.co", result.Email);
        Assert.Equal(string.Empty, result.Message);
    }
}
