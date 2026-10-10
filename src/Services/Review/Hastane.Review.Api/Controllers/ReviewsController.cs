using Hastane.Review.Api.Dtos;
using Hastane.Review.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Review.Api.Controllers;

// Sınıf düzeyinde yetki YOK, bilerek: sınıf + metot [Authorize]'ları VE ile birleşir, sınıf varsayılanı bir ucu
// sessizce "giriş yapmış herkese" açabilir. Her eylem kendi etiketini AÇIKÇA taşır; unutulan uç AuthorizationCoverage'ı kırar.
[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private const int MaxSummaryIds = 100;
    // Ortak politika yok (Shared'a dokunulmaz): yorumu yalnız Patient yazabildiği için sahip de Patient; Admin hepsini yönetir.
    // Doctor rolü buraya giremez (403).
    private const string PatientOrAdmin = HastaneRoles.Patient + "," + HastaneRoles.Admin;

    private readonly IReviewService _service;
    private readonly ICurrentUser _currentUser;

    public ReviewsController(IReviewService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    // Okuma uçları anonim ([AllowAnonymous]) ama token VARSA kimlik yine okunur: UseAuthentication varsayılan şemayla her
    // istekte çalışır ve geçerli token'da User'ı doldurur (AllowAnonymous yalnız YETKİ kontrolünü atlar). Token yoksa ya da
    // geçersizse (süresi dolmuş, imza bozuk) User anonim kalır: 401 DÖNMEZ, istek anonim gibi işlenir, IsMine = false.
    private string? ViewerId => _currentUser.UserId;

    // GET api/reviews?doctorId=... ile doktora göre süzülür (yeniden eskiye).
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<List<ResultReviewDto>>> GetAll([FromQuery] string? doctorId, CancellationToken ct)
        => Ok(string.IsNullOrWhiteSpace(doctorId)
            ? await _service.GetAllAsync(ViewerId, ct)
            : await _service.GetByDoctorIdAsync(doctorId, ViewerId, ct));

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<ActionResult<ResultReviewDto>> GetById(string id, CancellationToken ct)
    {
        var review = await _service.GetByIdAsync(id, ViewerId, ct);
        return review is null ? NotFound() : Ok(review);
    }

    // Doktorun puan özeti. Yorumu olmayan doktor için de 200 (ReviewCount=0).
    [AllowAnonymous]
    [HttpGet("doctors/{doctorId}/summary")]
    public async Task<ActionResult<DoctorRatingSummaryDto>> GetDoctorSummary(string doctorId, CancellationToken ct)
        => Ok(await _service.GetDoctorSummaryAsync(doctorId, ct));

    // Toplu özet: GET api/reviews/summaries?doctorIds=a,b,c. Sabit "summaries" yolu "{id}" parametresinden önce
    // eşleşir (ASP.NET literal segmenti parametreye tercih eder), bu yüzden GetById ile karışmaz.
    // Not: yorumdaki ReviewerName/Comment ham döner; HTML kaçırma (XSS) ekran katmanının işidir.
    [AllowAnonymous]
    [HttpGet("summaries")]
    public async Task<ActionResult<List<DoctorRatingSummaryDto>>> GetSummaries([FromQuery] string? doctorIds, CancellationToken ct)
    {
        var ids = (doctorIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct() // ilk görülen sıra korunur
            .ToList();
        if (ids.Count > MaxSummaryIds) return BadRequest($"En çok {MaxSummaryIds} doktor kimliği istenebilir.");
        return Ok(await _service.GetDoctorSummariesAsync(ids, ct));
    }

    // Yorum sahibi İSTEMCİDEN alınmaz: ReviewerId = token "sub", ReviewerName = token "name" (yoksa e-posta).
    // Gövdeye reviewerId/reviewerName yazılsa bile DTO'da bu alanlar yok, yok sayılır (impersonation / mass assignment).
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    [HttpPost]
    public async Task<ActionResult<ResultReviewDto>> Create(CreateReviewDto dto, CancellationToken ct)
    {
        var reviewerId = _currentUser.UserId;
        if (string.IsNullOrEmpty(reviewerId)) return Unauthorized();
        var created = await _service.CreateAsync(dto, reviewerId, ReviewerDisplayName(), ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // Sahiplik filtresi serviste (ownerId): Admin için null = herkesin yorumu; Patient için kendi sub'ı.
    // Başkasının (ya da ReviewerId'si olmayan eski) yorumu "bulunamadı" ile aynı 404: varlığı sızdırmaz.
    [Authorize(Roles = PatientOrAdmin)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateReviewDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        return await _service.UpdateAsync(dto, OwnerFilter(), ct) ? NoContent() : NotFound();
    }

    [Authorize(Roles = PatientOrAdmin)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _service.DeleteAsync(id, OwnerFilter(), ct) ? NoContent() : NotFound();

    // ICurrentUser'da ad yok (Shared'a dokunulmaz): "name" claim'i doğrudan token'dan okunur; yoksa e-posta, o da yoksa kimlik.
    private string ReviewerDisplayName()
    {
        var name = User.FindFirst(HastaneClaims.Name)?.Value;
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        return _currentUser.Email ?? _currentUser.UserId ?? string.Empty;
    }

    // Admin: filtre yok (null). Diğerleri: yalnız kendi yorumları. Kimliği olmayan çağıran hiçbir şeyle eşleşmesin diye "" verilir.
    private string? OwnerFilter() => _currentUser.IsAdmin ? null : _currentUser.UserId ?? string.Empty;
}
