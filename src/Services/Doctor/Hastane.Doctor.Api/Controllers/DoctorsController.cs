using Hastane.Doctor.Api.Activity;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Search;
using Hastane.Doctor.Api.Services;
using Hastane.Doctor.Api.Validation;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Doctor.Api.Controllers;

// Sınıf düzeyinde yetki YOK, bilerek: sınıf + metot [Authorize]'ları VE ile birleşir, sınıf varsayılanı bir ucu
// sessizce "giriş yapmış herkese" açabilir ya da gevşetilemez. Her eylem kendi etiketini AÇIKÇA taşır;
// etiketi unutulan uç AuthorizationCoverage testini kırar, politika değişirse yetki tablosu testi kırılır.
[ApiController]
[Route("api/doctors")]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorService _service;
    private readonly ICurrentUser _currentUser;
    private readonly IDoctorActivityPublisher _activity;

    public DoctorsController(IDoctorService service, ICurrentUser currentUser, IDoctorActivityPublisher activity)
    {
        _service = service;
        _currentUser = currentUser;
        _activity = activity;
    }

    // GET api/doctors?branchId=... ile branşa göre süzülür.
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<List<ResultDoctorDto>>> GetAll([FromQuery] string? branchId, CancellationToken ct)
        => Ok(string.IsNullOrWhiteSpace(branchId)
            ? await _service.GetAllAsync(ct)
            : await _service.GetByBranchIdAsync(branchId, ct));

    // Sabit rotalar ("search", "batch") ASP.NET Core yönlendirmesinde {id} gibi parametreli rotadan ÖNCE kazanır;
    // yine de metodlar {id}'den önce tanımlı ve testle kilitli.
    // GET api/doctors/search?text=&gender=&branchIds=a,b&city=&minFee=&maxFee=&date=2026-10-12&sort=name&page=1&pageSize=10
    [AllowAnonymous]
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<DoctorSearchItemDto>>> Search([FromQuery] DoctorSearchQuery query, CancellationToken ct)
    {
        try
        {
            var (result, engine) = await _service.SearchWithEngineAsync(query, ct);
            Response.Headers["X-Search-Engine"] = engine; // teşhis: yanıtı hangi motor üretti (gövde değişmez)
            // Olay akışı: anonim arama istatistiği. TryPublish bekletmez ve fırlatmaz; Kafka kapalıyken bile bu satır mikrosaniyedir.
            _activity.TryPublish(DoctorActivityEvent.Search(query, result.TotalCount, engine));
            return Ok(result);
        }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    // GET api/doctors/suggest?q=yilm  -> en çok 8 öneri (doktor adı / hizmet). Anonim, salt okunur.
    [AllowAnonymous]
    [HttpGet("suggest")]
    public async Task<ActionResult<List<DoctorSuggestionDto>>> Suggest([FromQuery] string? q, CancellationToken ct)
    {
        try { return Ok(await _service.SuggestAsync(q, ct)); }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    // Arama dizini bakımı (yalnız Admin): kaynak gerçek Mongo; dizin türetilmiş kopyadır, bozulursa buradan onarılır.
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpGet("search-index/status")]
    public async Task<ActionResult<SearchIndexStatusDto>> SearchIndexStatus(CancellationToken ct)
        => Ok(await _service.GetSearchIndexStatusAsync(ct));

    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpPost("search-index/rebuild")]
    public async Task<ActionResult<SearchIndexRebuildResultDto>> RebuildSearchIndex(CancellationToken ct)
    {
        try
        {
            var count = await _service.RebuildSearchIndexAsync(ct);
            if (count is null)
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Arama dizini kapalı.", detail: "Elasticsearch:Url ayarlanmamış.");
            return Ok(new SearchIndexRebuildResultDto { Indexed = count.Value });
        }
        catch (SearchIndexException ex)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Arama dizinine ulaşılamadı.", detail: ex.Message);
        }
    }

    // GET api/doctors/batch?ids=a,b,c
    [AllowAnonymous]
    [HttpGet("batch")]
    public async Task<ActionResult<List<DoctorSummaryDto>>> Batch([FromQuery] string? ids, CancellationToken ct)
    {
        try { return Ok(await _service.GetBatchAsync(ids, ct)); }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    // Herkese açık profil (anonim): GetByIdDoctorDto'dan AYRI bir DTO, Phone/Email alanı hiç yok.
    [AllowAnonymous]
    [HttpGet("{id}/public")]
    public async Task<ActionResult<PublicDoctorDto>> GetPublic(string id, CancellationToken ct)
    {
        var doctor = await _service.GetPublicByIdAsync(id, ct);
        if (doctor is null) return NotFound();
        _activity.TryPublish(DoctorActivityEvent.ProfileView(id));
        return Ok(doctor);
    }

    // Ayrıntıda telefon + e-posta var (listede yok): yalnız giriş yapmış kullanıcıya.
    [Authorize(Policy = HastanePolicies.AuthenticatedUser)]
    [HttpGet("{id}")]
    public async Task<ActionResult<GetByIdDoctorDto>> GetById(string id, CancellationToken ct)
    {
        var doctor = await _service.GetByIdAsync(id, ct);
        if (doctor is null) return NotFound();
        _activity.TryPublish(DoctorActivityEvent.ProfileView(id));
        return Ok(doctor);
    }

    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<ResultDoctorDto>> Create(CreateDoctorDto dto, CancellationToken ct)
    {
        try
        {
            var created = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    // Rol yetmez, SAHİPLİK de şart: Doctor rolü olan herkes değil, yalnız token'ındaki doctor_id == {id} olan doktor
    // kendi kaydını güncelleyebilir (Admin her kaydı). doctor_id'si olmayan (bağlanmamış) Doctor hiçbirini güncelleyemez.
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateDoctorDto dto, CancellationToken ct)
    {
        if (!_currentUser.IsAdmin && !(_currentUser.DoctorId is { Length: > 0 } own && own == id))
            return Forbid();
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        try { return await _service.UpdateAsync(dto, ct) ? NoContent() : NotFound(); }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
