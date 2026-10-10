using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Entities;
using Mapster;

namespace Hastane.Market.Api.Mapping;

public class MarketMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Yalnız çıkış yönü: giriş DTO'ları entity'ye servis tarafından alan alan atanır (Id, tarih, RowVersion sunucunun işi).
        // Category enum'u metne çevrilir ("MedicalDevice").
        config.NewConfig<Product, ResultProductDto>()
            .Map(d => d.Category, s => s.Category.ToString());

        config.NewConfig<StockReservationItem, StockItemDto>();
    }
}
