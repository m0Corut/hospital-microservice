using Hastane.Notification.Api.Dtos;
using Hastane.Notification.Api.Entities;
using Mapster;

namespace Hastane.Notification.Api.Mapping;

public class NotificationMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // UserId/DoctorId bilerek DTO'da yok: alıcı kimliği listede dönmez.
        config.NewConfig<AppNotification, ResultNotificationDto>()
            .Map(d => d.Id, s => s.Id ?? string.Empty);
        // Id'yi Mongo, CreatedAt'i servis atar; boş Kind "info" olur.
        config.NewConfig<CreateNotificationDto, AppNotification>()
            .Ignore(n => n.Id!)
            .Ignore(n => n.CreatedAt)
            .Ignore(n => n.ReadAt!)
            .Map(n => n.Kind, d => string.IsNullOrEmpty(d.Kind) ? "info" : d.Kind);
    }
}
