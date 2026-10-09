using Hastane.Review.Api.Dtos;
using Mapster;
using ReviewEntity = Hastane.Review.Api.Entities.Review;

namespace Hastane.Review.Api.Mapping;

public class ReviewMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ReviewEntity, ResultReviewDto>();
        // Id'yi Mongo, CreatedAt'i servis (DateTime.UtcNow) atar; Create DTO'sunda zaten yoklar.
        // Güncelleme eşlemesi yok: UpdateReviewDto kaydı komple değil yalnız üç alanı değiştirir (serviste Set).
        config.NewConfig<CreateReviewDto, ReviewEntity>()
            .Ignore(r => r.Id!)
            .Ignore(r => r.CreatedAt);
    }
}
