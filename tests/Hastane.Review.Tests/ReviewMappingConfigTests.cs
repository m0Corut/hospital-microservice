using Hastane.Review.Api.Dtos;
using Hastane.Review.Api.Mapping;
using Mapster;
using ReviewEntity = Hastane.Review.Api.Entities.Review;

namespace Hastane.Review.Tests;

public class ReviewMappingConfigTests
{
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(ReviewMappingConfig).Assembly);
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
    public void CreateDto_To_Entity_LeavesIdAndCreatedAtUnset()
    {
        var entity = new CreateReviewDto
        {
            DoctorId = "d1",
            ReviewerName = "Ali",
            Rating = 4,
            Comment = "İyi",
        }.Adapt<ReviewEntity>(StrictConfig());

        Assert.Null(entity.Id);
        Assert.Equal(default, entity.CreatedAt);
        Assert.Equal("d1", entity.DoctorId);
        Assert.Equal(4, entity.Rating);
    }
}
