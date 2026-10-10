using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;
using Mapster;

namespace Hastane.Queue.Api.Mapping;

public class QueueMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // PatientUserId DTO'da yok (hasta kimliği dışarı çıkmaz); AheadCount serviste hesaplanır.
        config.NewConfig<QueueEntry, QueueEntryDto>();
        config.NewConfig<QueueEntry, MyQueueEntryDto>().Ignore(d => d.AheadCount);
        // Herkese açık biçimler (DisplayItemDto, QueueBroadcastDto) bilerek elle kurulur (QueuePublic): hangi alanın
        // dışarı çıktığı tek yerde, okunur durur; eşleme yapılandırmasına gömülmez.
    }
}
