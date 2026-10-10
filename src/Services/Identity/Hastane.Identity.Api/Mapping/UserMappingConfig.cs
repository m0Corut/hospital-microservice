using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Mapster;

namespace Hastane.Identity.Api.Mapping;

public class UserMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Roller AppUser üzerinde değil ayrı tabloda (AspNetUserRoles); serviste doldurulur.
        config.NewConfig<AppUser, UserSummaryDto>()
            .Map(d => d.Email, s => s.Email ?? string.Empty)
            .Map(d => d.PhotoUrl, s => s.PhotoFileName == null ? null : "/gateway/users/" + s.Id + "/photo")
            .Ignore(d => d.Roles);
    }
}
