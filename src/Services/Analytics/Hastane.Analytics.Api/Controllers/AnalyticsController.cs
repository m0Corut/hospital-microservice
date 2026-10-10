using System.ComponentModel.DataAnnotations;
using Hastane.Analytics.Api.Dtos;
using Hastane.Analytics.Api.Kafka;
using Hastane.Analytics.Api.Services;
using Hastane.Analytics.Api.Settings;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hastane.Analytics.Api.Controllers;

// Sınıf düzeyinde yetki YOK, bilerek: her eylem kendi etiketini AÇIKÇA taşır; unutulan uç AuthorizationCoverage'ı kırar.
// Tüm uçlar yalnız Admin: kullanım istatistiği yönetim verisidir.
[ApiController]
[Route("api/analytics")]
public class AnalyticsController(
    IAnalyticsQueryService queries,
    KafkaConnectionState kafkaState,
    ReplayCoordinator replay,
    IOptions<KafkaOptions> kafka) : ControllerBase
{
    // GET api/analytics/doctors/top-viewed?days=7&take=10  -> [{ doctorId, views }]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpGet("doctors/top-viewed")]
    public async Task<ActionResult<List<TopViewedDoctorDto>>> TopViewed(
        [FromQuery, Range(1, 90)] int days = 7, [FromQuery, Range(1, 100)] int take = 10, CancellationToken ct = default)
        => Ok(await queries.TopViewedDoctorsAsync(days, take, ct));

    // GET api/analytics/search/top-terms?days=7&take=20  -> [{ term, count }]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpGet("search/top-terms")]
    public async Task<ActionResult<List<TopSearchTermDto>>> TopTerms(
        [FromQuery, Range(1, 90)] int days = 7, [FromQuery, Range(1, 100)] int take = 20, CancellationToken ct = default)
        => Ok(await queries.TopSearchTermsAsync(days, take, ct));

    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpGet("status")]
    public async Task<ActionResult<AnalyticsStatusDto>> Status(CancellationToken ct)
    {
        var (last, today) = await queries.ProcessingStatsAsync(ct);
        return Ok(new AnalyticsStatusDto
        {
            KafkaEnabled = kafkaState.Enabled,
            KafkaConnected = kafkaState.Connected,
            Topic = kafka.Value.Topic,
            GroupId = kafka.Value.GroupId,
            LastProcessedAt = last,
            ProcessedToday = today,
        });
    }

    // Yeniden oynatma gösterimi: toplamları sil + tüketici grubunun konumunu konunun başına sar. Hesap arka planda yeniden kurulur
    // (ilerlemeyi status ucunda "bugün işlenen" sayısı gösterir). Kafka yalnız SAKLAMA SÜRESİ (7 gün) kadar geçmişi tutar:
    // daha eski toplamlar yeniden kurulamaz.
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    [HttpPost("rebuild")]
    public async Task<ActionResult<RebuildResultDto>> Rebuild(CancellationToken ct)
    {
        if (!kafkaState.Enabled)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Olay akışı kapalı.", detail: "Kafka:BootstrapServers ayarlanmamış.");
        if (!kafkaState.Connected)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Kafka'ya bağlı değil.", detail: "Yeniden hesaplama için tüketicinin konuya bağlı olması gerekir.");

        var task = replay.Request();
        if (task is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Yeniden hesaplama zaten sürüyor.");

        RebuildOutcome outcome;
        try
        {
            outcome = await task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
        catch (TimeoutException)
        {
            replay.Withdraw(task);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Tüketici yanıt vermedi.", detail: "Biraz sonra yeniden deneyin.");
        }

        return outcome.Ok
            ? Ok(outcome.Result)
            : Problem(statusCode: StatusCodes.Status409Conflict, title: "Yeniden hesaplama yapılamadı.", detail: outcome.Error);
    }
}
