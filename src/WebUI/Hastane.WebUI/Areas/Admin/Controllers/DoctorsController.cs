using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.Areas.Admin.Controllers;

public class DoctorsController(IAdminDoctorClient doctors, IAdminBranchClient branches) : AdminAreaController
{
    private const int PageSize = 10;

    [HttpGet]
    public async Task<IActionResult> Index(string? text, string? branchId, int page = 1, CancellationToken ct = default)
    {
        var result = await doctors.SearchAsync(text, branchId, Math.Max(page, 1), PageSize, ct);
        var choices = await BranchChoicesAsync(ct);

        return View(new DoctorIndexViewModel
        {
            Text = text,
            BranchId = branchId,
            Page = result.Value ?? new AdminPagedApiModel<AdminDoctorListItemApiModel>(),
            BranchChoices = choices,
            BranchNames = choices.ToDictionary(c => c.Value, c => c.Text),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
        => await FormAsync(new DoctorFormViewModel(), ct);

    // command: "Ekle/Çıkar" düğmelerinin değeri. Doluysa liste değişir ve form yeniden çizilir, API'ye gidilmez.
    [HttpPost]
    public async Task<IActionResult> Create(DoctorFormViewModel model, string? command, CancellationToken ct)
    {
        if (model.ApplyCommand(command))
        {
            ModelState.Clear();
            return await FormAsync(model, ct);
        }

        if (!ModelState.IsValid) return await FormAsync(model, ct);

        var result = await doctors.CreateAsync(model.ToBody<AdminDoctorWriteBody>(), ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result);
            return await FormAsync(model, ct);
        }

        SetSuccess("Doktor eklendi.");
        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var result = await doctors.GetAsync(id, ct);
        return result.TryGetValue(out var doctor)
            ? await FormAsync(DoctorFormViewModel.From(doctor), ct)
            : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string id, DoctorFormViewModel model, string? command, CancellationToken ct)
    {
        model.Id = id;
        if (model.ApplyCommand(command))
        {
            ModelState.Clear();
            return await FormAsync(model, ct);
        }

        if (!ModelState.IsValid) return await FormAsync(model, ct);

        var body = model.ToBody<AdminDoctorUpdateBody>();
        body.Id = id;
        var result = await doctors.UpdateAsync(id, body, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result);
            return await FormAsync(model, ct);
        }

        SetSuccess("Doktor güncellendi.");
        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    // Onay sayfası: yalnız gösterir, silmez.
    [HttpGet]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await doctors.GetAsync(id, ct);
        return result.TryGetValue(out var doctor) ? View(doctor) : NotFound();
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(string id, CancellationToken ct)
    {
        var result = await doctors.DeleteAsync(id, ct);
        if (!result.IsSuccess)
        {
            SetWarning(AdminDisplay.ProblemText(result, "Doktor silinemedi."));
            return RedirectToAction(nameof(Index), new { area = "Admin" });
        }

        SetSuccess("Doktor silindi.");
        return RedirectToAction(nameof(Index), new { area = "Admin" });
    }

    private async Task<IActionResult> FormAsync(DoctorFormViewModel model, CancellationToken ct)
    {
        model.BranchChoices = await BranchChoicesAsync(ct);
        return View("Form", model);
    }

    private async Task<List<SelectListItem>> BranchChoicesAsync(CancellationToken ct)
    {
        var result = await branches.GetAllAsync(ApiFailures.All, ct);
        return (result.Value ?? []).Select(b => new SelectListItem(b.Name, b.Id)).ToList();
    }
}
