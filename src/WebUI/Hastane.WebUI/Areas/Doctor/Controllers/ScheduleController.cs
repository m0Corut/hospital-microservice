using System.Text.RegularExpressions;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

// Çalışma Saatleri: yalnız her kliniğin haftalık programı (Schedule) düzenlenir.
// Servis kısmi güncelleme (PATCH) sunmaz: PUT doctors/{id} kaydın TAMAMINI ister. Bu yüzden kayıt anında doktorun kaydı
// yeniden okunur (GET), yalnız Schedule listeleri değiştirilir, kalan her alan aynen geri yazılır. Kimlik token'dandır.
public partial class ScheduleController(IDoctorPanelProfileClient profile) : DoctorPanelController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var doctor = (await profile.GetAsync(DoctorId, ct)).Value!;
        return View(new ScheduleFormViewModel
        {
            ClinicLocations = doctor.ClinicLocations.Select(c => new ScheduleClinicForm
            {
                ClinicName = c.ClinicName,
                Schedule = c.Schedule.Select(s => new ScheduleSlotForm
                {
                    DayOfWeek = s.DayOfWeek,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    SlotMinutes = s.SlotMinutes,
                }).ToList(),
            }).ToList(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(ScheduleFormViewModel model, string? op, CancellationToken ct)
    {
        model.ClinicLocations ??= [];
        foreach (var clinic in model.ClinicLocations) clinic.Schedule ??= [];

        // Satır ekle/çıkar düğmeleri (JS'siz): op = add-slot:<klinik> | remove-slot:<klinik>:<satır>
        if (ApplyOperation(model, op))
        {
            ModelState.Clear(); // indeksler kayar; eski girdiler yanlış satırı doldurmasın
            return View(model);
        }

        Normalize(model);
        ValidateRows(model);
        if (!ModelState.IsValid) return View(model);

        var doctor = (await profile.GetAsync(DoctorId, ct)).Value!;

        // Sayfa açıkken klinik listesi başka yerden (Profilim) değiştiyse indeksle eşleştirme yanlış kliniğe yazardı.
        if (doctor.ClinicLocations.Count != model.ClinicLocations.Count
            || doctor.ClinicLocations.Where((c, i) => c.ClinicName != model.ClinicLocations[i].ClinicName).Any())
        {
            SetWarning("Klinik listeniz bu sayfa açıkken değişmiş. Güncel programı kontrol edip yeniden kaydedin.");
            return RedirectToAction(nameof(Index));
        }

        var form = ProfileFormViewModel.From(doctor);
        for (var i = 0; i < form.ClinicLocations.Count; i++)
            form.ClinicLocations[i].Schedule = model.ClinicLocations[i].Schedule;

        var result = await profile.UpdateAsync(DoctorId, form.ToWriteBody(DoctorId), ct);
        if (result.IsSuccess)
        {
            SetSuccess("Çalışma saatleriniz güncellendi.");
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddApiProblem(result);
        return View(model);
    }

    private static bool ApplyOperation(ScheduleFormViewModel model, string? op)
    {
        if (string.IsNullOrEmpty(op)) return false;
        var parts = op.Split(':');
        var clinics = model.ClinicLocations;

        if (parts is ["add-slot", var c] && int.TryParse(c, out var ci) && ci >= 0 && ci < clinics.Count)
        {
            if (clinics[ci].Schedule.Count < ScheduleFormViewModel.MaxSlotsPerClinic)
                clinics[ci].Schedule.Add(new ScheduleSlotForm());
            return true;
        }

        if (parts is ["remove-slot", var c2, var s] && int.TryParse(c2, out var cj) && int.TryParse(s, out var si)
            && cj >= 0 && cj < clinics.Count && si >= 0 && si < clinics[cj].Schedule.Count)
        {
            clinics[cj].Schedule.RemoveAt(si);
            return true;
        }

        return false;
    }

    // <input type="time"> bazen "09:00:00" gönderir; servis yalnız "HH:mm" kabul eder.
    private static void Normalize(ScheduleFormViewModel model)
    {
        foreach (var slot in model.ClinicLocations.SelectMany(c => c.Schedule))
        {
            slot.StartTime = TrimSeconds(slot.StartTime);
            slot.EndTime = TrimSeconds(slot.EndTime);
        }
    }

    private static string TrimSeconds(string? time)
    {
        time = time?.Trim() ?? string.Empty;
        return time.Length == 8 && time[5] == ':' ? time[..5] : time;
    }

    // Servisin DoctorRules.ValidateSchedule kurallarının satır bazlı aynası: servis tek bir "Schedule" iletisi döner,
    // burada hata hangi satırdaysa o satırın alanına konur. Servis yine son sözü söyler (400 klinik düzeyinde gösterilir).
    private void ValidateRows(ScheduleFormViewModel model)
    {
        for (var c = 0; c < model.ClinicLocations.Count; c++)
        {
            var slots = model.ClinicLocations[c].Schedule;
            var key = $"ClinicLocations[{c}].Schedule";
            if (slots.Count > ScheduleFormViewModel.MaxSlotsPerClinic)
                ModelState.AddModelError(key, $"Bir klinik için en çok {ScheduleFormViewModel.MaxSlotsPerClinic} saat aralığı eklenebilir.");

            var valid = new List<(int Index, ScheduleSlotForm Slot)>();
            for (var s = 0; s < slots.Count; s++)
            {
                var slot = slots[s];
                var rowKey = $"{key}[{s}]";
                var ok = true;
                if (!string.IsNullOrEmpty(slot.StartTime) && !TimeRegex().IsMatch(slot.StartTime))
                {
                    ModelState.AddModelError($"{rowKey}.StartTime", "Saat SS:dd biçiminde olmalı.");
                    ok = false;
                }

                if (!string.IsNullOrEmpty(slot.EndTime) && !TimeRegex().IsMatch(slot.EndTime))
                {
                    ModelState.AddModelError($"{rowKey}.EndTime", "Saat SS:dd biçiminde olmalı.");
                    ok = false;
                }

                var filled = !string.IsNullOrEmpty(slot.StartTime) && !string.IsNullOrEmpty(slot.EndTime);
                if (ok && filled && string.CompareOrdinal(slot.EndTime, slot.StartTime) <= 0)
                {
                    ModelState.AddModelError($"{rowKey}.EndTime", "Bitiş, başlangıçtan sonra olmalı.");
                    ok = false;
                }

                if (ok && filled) valid.Add((s, slot));
            }

            // Aynı gün içinde aralıklar kesişmemeli (bitiş = sonraki başlangıç serbest). Hata sonraki (kaydıran) satıra yazılır.
            foreach (var day in valid.GroupBy(v => v.Slot.DayOfWeek))
            {
                var sorted = day.OrderBy(v => v.Slot.StartTime, StringComparer.Ordinal).ToList();
                for (var i = 1; i < sorted.Count; i++)
                    if (string.CompareOrdinal(sorted[i].Slot.StartTime, sorted[i - 1].Slot.EndTime) < 0)
                        ModelState.AddModelError($"{key}[{sorted[i].Index}].StartTime",
                            $"Bu aralık {sorted[i - 1].Slot.StartTime}-{sorted[i - 1].Slot.EndTime} ile çakışıyor.");
            }
        }
    }

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimeRegex();
}
