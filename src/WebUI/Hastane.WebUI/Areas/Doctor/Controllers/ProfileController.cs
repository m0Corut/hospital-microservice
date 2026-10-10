using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class ProfileController(IDoctorPanelProfileClient profile, IBranchClient branches) : DoctorPanelController
{
    // Rotada id YOK: hangi doktor kaydı olduğu yalnız token'daki doctor_id'den gelir. Başka doktorun
    // kimliğiyle istek göndermenin yolu yok (form/sorgu alanları yok sayılır, PUT yolu ve gövde Id'si DoctorId'den kurulur).
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var doctor = (await profile.GetAsync(DoctorId, ct)).Value!;
        var model = ProfileFormViewModel.From(doctor);
        await FillBranchOptionsAsync(model, ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Index(ProfileFormViewModel model, string? op, CancellationToken ct)
    {
        if (ApplyOperation(model, op))
        {
            await FillBranchOptionsAsync(model, ct);
            return View(model);
        }

        if (ModelState.IsValid)
        {
            var result = await profile.UpdateAsync(DoctorId, model.ToWriteBody(DoctorId), ct);
            if (result.IsSuccess)
            {
                SetSuccess("Profiliniz güncellendi.");
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddApiProblem(result);
        }

        await FillBranchOptionsAsync(model, ct);
        return View(model);
    }

    private async Task FillBranchOptionsAsync(ProfileFormViewModel model, CancellationToken ct)
    {
        var result = await branches.GetAllAsync(ApiFailures.All, ct);
        model.BranchOptions = result.TryGetValue(out var list)
            ? list.Select(b => new KeyValuePair<string, string>(b.Id, b.Name)).ToList()
            : [];
        // Liste alınamadıysa mevcut branş yine de seçili kalsın.
        if (model.BranchOptions.All(o => o.Key != model.BranchId) && model.BranchId.Length > 0)
            model.BranchOptions.Add(new KeyValuePair<string, string>(model.BranchId, model.BranchId));
    }

    // Liste satırı ekle/çıkar düğmeleri (JS'siz). op biçimleri:
    //   add:<Liste>                     remove:<Liste>:<i>
    //   add-slot:<klinik>               remove-slot:<klinik>:<i>
    // <Liste>: Services, Gallery, Educations, Experiences, Awards, ClinicLocations
    private bool ApplyOperation(ProfileFormViewModel model, string? op)
    {
        if (string.IsNullOrEmpty(op)) return false;
        var parts = op.Split(':');
        var done = parts[0] switch
        {
            "add" when parts.Length == 2 => Add(model, parts[1]),
            "remove" when parts.Length == 3 && int.TryParse(parts[2], out var i) => Remove(model, parts[1], i),
            "add-slot" when parts.Length == 2 && int.TryParse(parts[1], out var c) && InRange(model.ClinicLocations, c)
                => Do(() => model.ClinicLocations[c].Schedule.Add(new ScheduleSlotForm())),
            "remove-slot" when parts.Length == 3 && int.TryParse(parts[1], out var c2) && int.TryParse(parts[2], out var s)
                              && InRange(model.ClinicLocations, c2) && InRange(model.ClinicLocations[c2].Schedule, s)
                => Do(() => model.ClinicLocations[c2].Schedule.RemoveAt(s)),
            _ => false,
        };

        // Kayan indekslerde eski ModelState girdileri yanlış satırı doldurmasın.
        if (done) ModelState.Clear();
        return done;
    }

    private static bool InRange<T>(List<T> list, int index) => index >= 0 && index < list.Count;

    private static bool Do(Action action)
    {
        action();
        return true;
    }

    private static bool Add(ProfileFormViewModel m, string list) => list switch
    {
        "Services" => Do(() => m.Services.Add(string.Empty)),
        "Gallery" => Do(() => m.Gallery.Add(string.Empty)),
        "Educations" => Do(() => m.Educations.Add(new EducationForm())),
        "Experiences" => Do(() => m.Experiences.Add(new ExperienceForm())),
        "Awards" => Do(() => m.Awards.Add(new AwardForm())),
        "ClinicLocations" => Do(() => m.ClinicLocations.Add(new ClinicLocationForm())),
        _ => false,
    };

    private static bool Remove(ProfileFormViewModel m, string list, int i) => list switch
    {
        "Services" when InRange(m.Services, i) => Do(() => m.Services.RemoveAt(i)),
        "Gallery" when InRange(m.Gallery, i) => Do(() => m.Gallery.RemoveAt(i)),
        "Educations" when InRange(m.Educations, i) => Do(() => m.Educations.RemoveAt(i)),
        "Experiences" when InRange(m.Experiences, i) => Do(() => m.Experiences.RemoveAt(i)),
        "Awards" when InRange(m.Awards, i) => Do(() => m.Awards.RemoveAt(i)),
        "ClinicLocations" when InRange(m.ClinicLocations, i) => Do(() => m.ClinicLocations.RemoveAt(i)),
        _ => false,
    };
}
