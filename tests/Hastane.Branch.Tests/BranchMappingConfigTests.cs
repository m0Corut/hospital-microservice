using Hastane.Branch.Api.Dtos;
using Hastane.Branch.Api.Mapping;
using Mapster;
using BranchEntity = Hastane.Branch.Api.Entities.Branch;

namespace Hastane.Branch.Tests;

public class BranchMappingConfigTests
{
    // Sıkı mod: açık eşleme zorunlu, hedefteki her alanın kaynağı olmalı (ya da bilerek Ignore edilmeli).
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(BranchMappingConfig).Assembly);
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
    public void CreateDto_To_Entity_IgnoresId()
    {
        var entity = new CreateBranchDto { Name = "Kardiyoloji", Icon = "fa-heart-pulse" }
            .Adapt<BranchEntity>(StrictConfig());
        Assert.Null(entity.Id);
        Assert.Equal("Kardiyoloji", entity.Name);
        Assert.Equal("fa-heart-pulse", entity.Icon);
    }
}
