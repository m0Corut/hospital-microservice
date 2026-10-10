using System.Security.Claims;
using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

// Doktor paneli controller'larının ortak tabanı: rol kapısı DoctorAreaController'dan gelir (burada ayrıca etiket YOK).
// Burada ikinci kapı: hesap bir doktor kaydına bağlı değilse (token'da doctor_id yok) hiçbir action çalışmaz,
// API çağrılmaz; "bağlanmadı" sayfası gösterilir.
public abstract class DoctorPanelController : DoctorAreaController
{
    // Doktorun kimliği YALNIZ token'dan (çerezdeki oturum). Rota/form/sorgu dizgesinden asla okunmaz.
    protected string? OwnDoctorId
        => User.FindFirstValue(HastaneClaims.DoctorId) is { Length: > 0 } id ? id : null;

    // Kapıdan geçen action'larda boş olamaz.
    protected string DoctorId => OwnDoctorId!;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (OwnDoctorId is null)
            context.Result = View("NotLinked");

        base.OnActionExecuting(context);
    }

    // Hasta kimlikleri -> ad, tek toplu çağrı. Çözülemeyen kimlik için "Hasta #1a2b3c4d".
    protected async Task<Func<string, string>> PatientLabelsAsync(IEnumerable<string> patientIds, CancellationToken ct)
    {
        var names = await HttpContext.RequestServices.GetRequiredService<IDoctorPanelUserClient>().GetNamesAsync(patientIds, ct);
        return id => names.TryGetValue(id, out var name) && name.Length > 0 ? name : "Hasta #" + DoctorPanelFormat.ShortId(id);
    }

    protected async Task<Func<string, string>> BranchLabelsAsync(CancellationToken ct)
    {
        var result = await HttpContext.RequestServices.GetRequiredService<IBranchClient>().GetAllAsync(ApiFailures.All, ct);
        var names = result.TryGetValue(out var branches)
            ? branches.ToDictionary(b => b.Id, b => b.Name)
            : [];
        return id => names.TryGetValue(id, out var name) ? name : DoctorPanelFormat.ShortId(id);
    }
}
