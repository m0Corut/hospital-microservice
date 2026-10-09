using Hastane.Branch.Api.Dtos;
using Hastane.Branch.Api.Settings;
using MapsterMapper;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using BranchEntity = Hastane.Branch.Api.Entities.Branch;

namespace Hastane.Branch.Api.Services;

public class BranchService : IBranchService
{
    private readonly IMongoCollection<BranchEntity> _branches;
    private readonly IMapper _mapper;

    public BranchService(IMapper mapper, IOptions<DatabaseSettings> options, IMongoClient client)
    {
        _mapper = mapper;
        IDatabaseSettings settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _branches = database.GetCollection<BranchEntity>(settings.BranchCollectionName);
    }

    public async Task<List<ResultBranchDto>> GetAllAsync(CancellationToken ct)
    {
        var list = await _branches.Find(_ => true).ToListAsync(ct);
        return _mapper.Map<List<ResultBranchDto>>(list);
    }

    public async Task<GetByIdBranchDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        // Geçersiz biçimli id Mongo'da hata fırlatır; baştan "yok" sayıyoruz (500 yerine 404).
        if (!ObjectId.TryParse(id, out _)) return null;
        var entity = await _branches.Find(b => b.Id == id).FirstOrDefaultAsync(ct);
        return entity is null ? null : _mapper.Map<GetByIdBranchDto>(entity);
    }

    public async Task<ResultBranchDto> CreateAsync(CreateBranchDto dto, CancellationToken ct)
    {
        var entity = _mapper.Map<BranchEntity>(dto);
        await _branches.InsertOneAsync(entity, cancellationToken: ct);
        return _mapper.Map<ResultBranchDto>(entity);
    }

    public async Task<bool> UpdateAsync(UpdateBranchDto dto, CancellationToken ct)
    {
        if (!ObjectId.TryParse(dto.Id, out _)) return false;
        var entity = _mapper.Map<BranchEntity>(dto);
        var result = await _branches.ReplaceOneAsync(b => b.Id == dto.Id, entity, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return false;
        var result = await _branches.DeleteOneAsync(b => b.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
