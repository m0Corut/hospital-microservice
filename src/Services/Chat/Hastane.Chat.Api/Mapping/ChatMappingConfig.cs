using Hastane.Chat.Api.Dtos;
using Hastane.Chat.Api.Entities;
using Mapster;

namespace Hastane.Chat.Api.Mapping;

public class ChatMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // UnreadCount saklanmaz: iki sayaçtan isteği yapanın tarafınınki serviste doldurulur.
        config.NewConfig<Conversation, ConversationDto>()
            .Ignore(d => d.UnreadCount);
        // SenderId bilerek DTO'da yok: karşı tarafa kullanıcı kimliği sızmasın.
        config.NewConfig<ChatMessage, MessageDto>();
    }
}
