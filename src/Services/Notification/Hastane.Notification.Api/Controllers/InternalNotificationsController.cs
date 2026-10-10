using Hastane.Notification.Api.Dtos;
using Hastane.Notification.Api.Security;
using Hastane.Notification.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Notification.Api.Controllers;

// Servisten servise uçlar: JWT değil X-Internal-Key. Gateway bu yolları dışarı AÇMAZ.
// [AllowAnonymous] "JWT istemez" demektir; kapı InternalApiKey filtresidir (anahtar yoksa/yanlışsa 401).
[ApiController]
[Route("api/internal/notifications")]
public class InternalNotificationsController(INotificationService service) : ControllerBase
{
    [AllowAnonymous]
    [InternalApiKey]
    [HttpPost]
    public async Task<ActionResult<ResultNotificationDto>> Create(CreateNotificationDto dto, CancellationToken ct)
    {
        // Alıcı tam bir kimlikle adreslenir: ikisi birden ya da hiçbiri belirsizliktir.
        var hasUser = !string.IsNullOrWhiteSpace(dto.UserId);
        var hasDoctor = !string.IsNullOrWhiteSpace(dto.DoctorId);
        if (hasUser == hasDoctor)
        {
            ModelState.AddModelError(nameof(dto.UserId), "userId ya da doctorId'den tam biri verilmeli.");
            return ValidationProblem(ModelState);
        }
        var created = await service.CreateAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [AllowAnonymous]
    [InternalApiKey]
    [HttpPost("email")]
    public async Task<IActionResult> SendEmail(SendEmailDto dto, CancellationToken ct)
    {
        if (await service.SendEmailAsync(dto, ct)) return Accepted();
        return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
        {
            Status = StatusCodes.Status502BadGateway,
            Title = "E-posta gönderilemedi",
            Detail = "SMTP sunucusuna iletilemedi.",
        });
    }
}
