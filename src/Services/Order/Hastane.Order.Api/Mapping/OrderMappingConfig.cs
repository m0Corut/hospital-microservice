using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Entities;
using Mapster;
using OrderEntity = Hastane.Order.Api.Entities.Order;

namespace Hastane.Order.Api.Mapping;

public class OrderMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Yalnız çıkış yönü: CheckoutDto -> Order eşlemesi bilerek yok (hasta, fiyat, numara, durum sunucu alanlarıdır;
        // servis tek tek atar, gövdeden gelen hiçbir şey bu alanlara sızamasın).
        config.NewConfig<OrderEntity, ResultOrderDto>()
            .Map(d => d.ItemCount, s => s.Items.Count);
        config.NewConfig<OrderEntity, GetByIdOrderDto>();
        config.NewConfig<OrderItem, OrderItemDto>();
    }
}
