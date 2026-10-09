using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Settings;
using MapsterMapper;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Api.Services;

public class DoctorService : IDoctorService
{
    private readonly IMongoCollection<DoctorEntity> _doctors;
    private readonly IMapper _mapper;

    // IMongoClient DI'dan gelir (Singleton): bağlantı havuzu uygulama boyunca tek ve paylaşılır.
    public DoctorService(IMapper mapper, IMongoClient client, IOptions<DatabaseSettings> options)
    {
        _mapper = mapper;
        IDatabaseSettings settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _doctors = database.GetCollection<DoctorEntity>(settings.DoctorCollectionName);
    }

    public async Task<List<ResultDoctorDto>> GetAllAsync(CancellationToken ct)
    {
        var list = await _doctors.Find(_ => true).ToListAsync(ct);
        return _mapper.Map<List<ResultDoctorDto>>(list);
    }

    public async Task<List<ResultDoctorDto>> GetByBranchIdAsync(string branchId, CancellationToken ct)
    {
        var list = await _doctors.Find(d => d.BranchId == branchId).ToListAsync(ct);
        return _mapper.Map<List<ResultDoctorDto>>(list);
    }

    public async Task<GetByIdDoctorDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        // Geçersiz biçimli id Mongo'da hata fırlatır; baştan "yok" sayıyoruz (500 yerine 404).
        if (!ObjectId.TryParse(id, out _)) return null;
        var entity = await _doctors.Find(d => d.Id == id).FirstOrDefaultAsync(ct);
        return entity is null ? null : _mapper.Map<GetByIdDoctorDto>(entity);
    }

    public async Task<ResultDoctorDto> CreateAsync(CreateDoctorDto dto, CancellationToken ct)
    {
        var entity = _mapper.Map<DoctorEntity>(dto);
        await _doctors.InsertOneAsync(entity, cancellationToken: ct);
        return _mapper.Map<ResultDoctorDto>(entity);
    }

    public async Task<bool> UpdateAsync(UpdateDoctorDto dto, CancellationToken ct)
    {
        if (!ObjectId.TryParse(dto.Id, out _)) return false;
        var entity = _mapper.Map<DoctorEntity>(dto);
        var result = await _doctors.ReplaceOneAsync(d => d.Id == dto.Id, entity, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return false;
        var result = await _doctors.DeleteOneAsync(d => d.Id == id, ct);
        return result.DeletedCount > 0;
    }
}
