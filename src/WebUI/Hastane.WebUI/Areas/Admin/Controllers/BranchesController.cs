using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Branş yönetimi. Yetki: AdminAreaController (alan + Admin rolü). Silme: GET onay sayfasını gösterir, silmeyi yalnız POST yapar.
public class BranchesController(IAdminBranchClient branches) : AdminAreaController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await branches.GetAllAsync(ApiFailures.None, ct);
        return View(result.Value ?? []);
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new BranchFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(BranchFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Form", model);

        var result = await branches.CreateAsync(ToBody(model, null), ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result);
            return View("Form", model);
        }

        SetSuccess("Branş eklendi.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var result = await branches.GetAsync(id, ct);
        if (!result.TryGetValue(out var branch)) return NotFound();

        return View("Form", new BranchFormViewModel
        {
            Id = branch.Id,
            Name = branch.Name,
            Description = branch.Description,
            Icon = branch.Icon,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string id, BranchFormViewModel model, CancellationToken ct)
    {
        model.Id = id;
        if (!ModelState.IsValid) return View("Form", model);

        var result = await branches.UpdateAsync(id, ToBody(model, id), ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result);
            return View("Form", model);
        }

        SetSuccess("Branş güncellendi.");
        return RedirectToAction(nameof(Index));
    }

    // Onay sayfası: yalnız gösterir, hiçbir şeyi silmez.
    [HttpGet]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await branches.GetAsync(id, ct);
        return result.TryGetValue(out var branch) ? View(branch) : NotFound();
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(string id, CancellationToken ct)
    {
        var result = await branches.DeleteAsync(id, ct);
        if (!result.IsSuccess)
        {
            SetWarning(AdminDisplay.ProblemText(result, "Branş silinemedi."));
            return RedirectToAction(nameof(Index));
        }

        SetSuccess("Branş silindi.");
        return RedirectToAction(nameof(Index));
    }

    private static AdminBranchWriteBody ToBody(BranchFormViewModel model, string? id) => new()
    {
        Id = id,
        Name = model.Name!.Trim(),
        Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
        Icon = string.IsNullOrWhiteSpace(model.Icon) ? null : model.Icon.Trim(),
    };
}
