using Hastane.Patient.Api.Data;
using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Entities;
using Hastane.Patient.Api.Integration;
using Hastane.Shared.Security;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Patient.Api.Services;

public class PatientService : IPatientService
{
    public const int MaxFavorites = 100;
    public const int MaxDoctorIdLength = 64;

    private readonly PatientDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _user;
    private readonly IAppointmentLookup _appointments;
    private readonly IDoctorLookup _doctors;
    private readonly TimeProvider _time;

    public PatientService(PatientDbContext db, IMapper mapper, ICurrentUser user,
        IAppointmentLookup appointments, IDoctorLookup doctors, TimeProvider time)
    {
        _db = db;
        _mapper = mapper;
        _user = user;
        _appointments = appointments;
        _doctors = doctors;
        _time = time;
    }

    // Kimlik HER ZAMAN token'dan; istekten kullanıcı kimliği alınmaz.
    private string Me => _user.UserId ?? throw new InvalidOperationException("Kimlik doğrulanmış kullanıcıda sub yok.");

    public async Task<PatientProfileDto> GetMineAsync(CancellationToken ct)
        => await LoadProfileOrEmptyAsync(Me, ct);

    public async Task<PatientProfileDto> UpsertMineAsync(UpdatePatientProfileDto dto, CancellationToken ct)
    {
        var userId = Me;
        var profile = await _db.Profiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var isNew = profile is null;
        if (profile is null)
        {
            profile = new PatientProfile { UserId = userId };
            _db.Profiles.Add(profile);
        }

        Apply(profile, dto);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (isNew)
        {
            // Aynı hastanın iki ilk PUT'u yarıştı: öteki satırı önce yazdı (PK çakışması). Bizimkini ona uygulayıp güncelle.
            _db.Entry(profile).State = EntityState.Detached;
            profile = await _db.Profiles.FirstAsync(p => p.UserId == userId, ct);
            Apply(profile, dto);
            await _db.SaveChangesAsync(ct);
        }

        return ToDto(profile);
    }

    public async Task<OperationResult<PatientProfileDto>> GetByUserIdAsync(string userId, CancellationToken ct)
    {
        // Hasta: yalnız kendisi; başkasınınki 404 (varlığı bile sızmasın).
        if (_user.UserId is not null && string.Equals(_user.UserId, userId, StringComparison.Ordinal)
            && _user.IsInRole(HastaneRoles.Patient))
            return OperationResult<PatientProfileDto>.Ok(await LoadProfileOrEmptyAsync(userId, ct));

        if (_user.IsAdmin)
        {
            var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
            return profile is null
                ? OperationResult<PatientProfileDto>.NotFound()
                : OperationResult<PatientProfileDto>.Ok(ToDto(profile));
        }

        if (_user.IsInRole(HastaneRoles.Doctor) && _user.DoctorId is not null)
        {
            // Doktorun kendi randevusu olan hasta: kararı Appointment verir (token aynen iletilir, oradaki liste doktora göre süzülür).
            var lookup = await _appointments.GetForPatientAsync(userId, ct);
            switch (lookup.Outcome)
            {
                case LookupOutcome.Unavailable:
                    return OperationResult<PatientProfileDto>.ServiceUnavailable("Randevu servisine ulaşılamadı; hasta bilgisi doğrulanamadı.");
                case LookupOutcome.Found when lookup.Value is { Count: > 0 }:
                    return OperationResult<PatientProfileDto>.Ok(await LoadProfileOrEmptyAsync(userId, ct));
            }
        }

        return OperationResult<PatientProfileDto>.NotFound();
    }

    public async Task<List<FavoriteDoctorDto>> GetFavoritesAsync(CancellationToken ct)
    {
        var userId = Me;
        var items = await _db.Favorites.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.DoctorId)
            .ToListAsync(ct);
        return _mapper.Map<List<FavoriteDoctorDto>>(items);
    }

    public async Task<OperationResult<object>> AddFavoriteAsync(string doctorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId) || doctorId.Length > MaxDoctorIdLength)
            return OperationResult<object>.Validation($"doctorId boş olamaz ve en çok {MaxDoctorIdLength} karakter olmalı.");

        var userId = Me;

        // Zaten favoriyse hiçbir şey yapma (idempotent): Doctor'a gitmeye bile gerek yok.
        if (await _db.Favorites.AnyAsync(f => f.UserId == userId && f.DoctorId == doctorId, ct))
            return OperationResult<object>.NoContent();

        if (await _db.Favorites.CountAsync(f => f.UserId == userId, ct) >= MaxFavorites)
            return OperationResult<object>.Conflict($"En çok {MaxFavorites} favori doktor eklenebilir.");

        var lookup = await _doctors.GetPublicAsync(doctorId, ct);
        switch (lookup.Outcome)
        {
            case LookupOutcome.Found:
                break;
            case LookupOutcome.Unavailable:
                return OperationResult<object>.ServiceUnavailable("Doktor servisine ulaşılamadı; doktor doğrulanamadı.");
            default:
                return OperationResult<object>.NotFound("Doktor bulunamadı.");
        }

        _db.Favorites.Add(new FavoriteDoctor { UserId = userId, DoctorId = doctorId, CreatedAt = _time.GetUtcNow().UtcDateTime });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı çift yarışta başkası tarafından eklendi (bileşik anahtar çakıştı): sonuç istenen durum, idempotent başarı.
        }

        return OperationResult<object>.NoContent();
    }

    public async Task RemoveFavoriteAsync(string doctorId, CancellationToken ct)
    {
        var userId = Me;
        await _db.Favorites.Where(f => f.UserId == userId && f.DoctorId == doctorId).ExecuteDeleteAsync(ct);
    }

    private async Task<PatientProfileDto> LoadProfileOrEmptyAsync(string userId, CancellationToken ct)
    {
        var profile = await _db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return profile is null ? new PatientProfileDto { UserId = userId } : ToDto(profile);
    }

    private PatientProfileDto ToDto(PatientProfile profile)
    {
        var dto = _mapper.Map<PatientProfileDto>(profile);
        dto.Age = AgeCalculator.Calculate(profile.DateOfBirth, DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime));
        return dto;
    }

    // Alan alan atama: UserId ve UpdatedAt istekten asla gelmez (mass assignment yok).
    private void Apply(PatientProfile profile, UpdatePatientProfileDto dto)
    {
        profile.DateOfBirth = dto.DateOfBirth;
        profile.Gender = dto.Gender;
        profile.BloodType = dto.BloodType;
        profile.Phone = Clean(dto.Phone);
        profile.Address = Clean(dto.Address);
        profile.City = Clean(dto.City);
        profile.EmergencyContactName = Clean(dto.EmergencyContactName);
        profile.EmergencyContactPhone = Clean(dto.EmergencyContactPhone);
        profile.UpdatedAt = _time.GetUtcNow().UtcDateTime;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
