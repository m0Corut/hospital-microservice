using Hastane.Prescription.Api.Data;
using Hastane.Prescription.Api.Dtos;
using Hastane.Prescription.Api.Entities;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using PrescriptionEntity = Hastane.Prescription.Api.Entities.Prescription;

namespace Hastane.Prescription.Api.Services;

public class PrescriptionService : IPrescriptionService
{
    private readonly PrescriptionDbContext _db;
    private readonly IMapper _mapper;

    // DbContext Scoped: istek başına bir tane. Değişiklik izleyicisi ve bağlantı o isteğe aittir.
    public PrescriptionService(PrescriptionDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<List<ResultPrescriptionDto>> GetAllAsync(
        string? patientId, string? doctorId, Guid? appointmentId, CancellationToken ct)
    {
        IQueryable<PrescriptionEntity> query = _db.Prescriptions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(patientId)) query = query.Where(p => p.PatientId == patientId);
        if (!string.IsNullOrWhiteSpace(doctorId)) query = query.Where(p => p.DoctorId == doctorId);
        if (appointmentId is not null) query = query.Where(p => p.AppointmentId == appointmentId);

        // Kalemleri YÜKLEMİYORUZ. ProjectToType, Mapster'ın "s.Items.Count" kuralını SQL'e çevirir:
        // SELECT ..., (SELECT COUNT(*) FROM PrescriptionItems i WHERE i.PrescriptionId = p.Id) AS ItemCount
        // Tek sorgu, sayıyı veritabanı hesaplar. (Kalemleri çekip C#'ta saymak boşuna veri taşırdı.)
        return await query
            .OrderByDescending(p => p.IssuedAt).ThenByDescending(p => p.Id)
            .ProjectToType<ResultPrescriptionDto>(_mapper.Config)
            .ToListAsync(ct);
    }

    public async Task<GetByIdPrescriptionDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        // Include: reçete ile kalemlerini TEK sorguda (JOIN) getirir. Include olmasa Items boş kalırdı;
        // önce reçeteleri, sonra her biri için ayrı kalem sorgusu atmak ise N+1 sorunudur.
        var entity = await _db.Prescriptions.AsNoTracking()
            .Include(p => p.Items.OrderBy(i => i.LineNo))
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        return entity is null ? null : _mapper.Map<GetByIdPrescriptionDto>(entity);
    }

    public async Task<OperationResult<GetByIdPrescriptionDto>> CreateAsync(CreatePrescriptionDto dto, CancellationToken ct)
    {
        // İkinci savunma hattı: DTO doğrulaması (controller) atlansa bile (başka bir servis/test doğrudan çağırırsa)
        // kural burada da korunur.
        if (!HasValidItemCount(dto.Items)) return OperationResult<GetByIdPrescriptionDto>.Validation(ItemCountError);

        var entity = _mapper.Map<PrescriptionEntity>(dto);
        entity.Id = Guid.CreateVersion7();
        entity.IssuedAt = DateTime.UtcNow;
        Number(entity.Items, entity.Id);

        // Ya hep ya hiç: reçete + tüm kalemleri tek SaveChanges'te, tek veritabanı transaction'ında yazılır.
        // Herhangi bir satır hata verirse hiçbiri kalıcı olmaz (reçete kalemsiz ya da yarım kalem listesiyle kalmaz).
        _db.Prescriptions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return OperationResult<GetByIdPrescriptionDto>.Ok(_mapper.Map<GetByIdPrescriptionDto>(entity));
    }

    public async Task<OperationResult<GetByIdPrescriptionDto>> UpdateAsync(UpdatePrescriptionDto dto, CancellationToken ct)
    {
        if (!HasValidItemCount(dto.Items)) return OperationResult<GetByIdPrescriptionDto>.Validation(ItemCountError);

        var entity = await _db.Prescriptions.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == dto.Id, ct);
        if (entity is null) return OperationResult<GetByIdPrescriptionDto>.NotFound();

        entity.Notes = dto.Notes;

        // "Kalemleri tamamen değiştir": eskileri sil, yenileri (yeni Id'lerle) ekle; hepsi tek SaveChanges'te.
        var oldItems = entity.Items.ToList();
        _db.PrescriptionItems.RemoveRange(oldItems);
        var newItems = _mapper.Map<List<PrescriptionItem>>(dto.Items);
        Number(newItems, entity.Id);
        // AddRange ile açıkça "eklenecek" işaretlenir: Id'si dolu bir nesneyi EF yoksa "var olan, güncellenecek" sanardı.
        _db.PrescriptionItems.AddRange(newItems);

        await _db.SaveChangesAsync(ct);

        return OperationResult<GetByIdPrescriptionDto>.Ok(new GetByIdPrescriptionDto
        {
            Id = entity.Id,
            AppointmentId = entity.AppointmentId,
            DoctorId = entity.DoctorId,
            PatientId = entity.PatientId,
            IssuedAt = entity.IssuedAt,
            Notes = entity.Notes,
            Items = _mapper.Map<List<PrescriptionItemDto>>(newItems),
        });
    }

    private const string ItemCountError = "Bir reçetede en az 1, en çok 20 kalem olmalı.";

    private static bool HasValidItemCount<T>(List<T>? items) => items is { Count: >= 1 and <= 20 };

    // Kalemlere Id verir, reçeteye bağlar ve istemcinin gönderdiği sırayla 1,2,3... numaralar.
    private static void Number(List<PrescriptionItem> items, Guid prescriptionId)
    {
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Id = Guid.CreateVersion7();
            items[i].PrescriptionId = prescriptionId;
            items[i].LineNo = i + 1;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        // Tek DELETE komutu; kalemleri veritabanındaki ON DELETE CASCADE siler.
        var deleted = await _db.Prescriptions.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0;
    }
}
