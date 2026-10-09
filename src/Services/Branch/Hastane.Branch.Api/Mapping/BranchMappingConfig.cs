using Hastane.Branch.Api.Dtos;
using Mapster;
using BranchEntity = Hastane.Branch.Api.Entities.Branch;

namespace Hastane.Branch.Api.Mapping;

// Hangi sınıf hangisine dönüşür, tek yerde yazılır (kurstaki "profil" mantığı, Mapster'da IRegister).
public class BranchMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<BranchEntity, ResultBranchDto>();
        config.NewConfig<BranchEntity, GetByIdBranchDto>();
        // Create'te Id gelmez; Mongo üretir.
        config.NewConfig<CreateBranchDto, BranchEntity>().Ignore(d => d.Id!);
        config.NewConfig<UpdateBranchDto, BranchEntity>();
    }
}
