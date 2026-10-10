using Hastane.WebUI.Clients;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hastane.WebUI.ViewModels.DoctorPanel;

// Reçete formunun ortak işlemleri (doktor ve yönetici panelinde aynı form): kalem ekle/çıkar, kalem sayısı, gövde.
public static class PrescriptionFormOperations
{
    public const string AddItemOp = "add-item";
    public const string RemoveItemOpPrefix = "remove-item:";

    // "Kalem ekle / çıkar" düğmeleri formu JS'siz yeniden çizer. Dönüş true: işlem yapıldı, doğrulama/kaydetme atlanır.
    public static bool ApplyItemOperation(ModelStateDictionary modelState, PrescriptionFormViewModel model, string? op)
    {
        if (op == AddItemOp)
        {
            if (model.Items.Count < PrescriptionFormViewModel.MaxItems)
                model.Items.Add(new PrescriptionItemForm());
        }
        else if (op is not null && op.StartsWith(RemoveItemOpPrefix, StringComparison.Ordinal)
                 && int.TryParse(op[RemoveItemOpPrefix.Length..], out var index)
                 && index >= 0 && index < model.Items.Count)
        {
            model.Items.RemoveAt(index);
        }
        else
        {
            return false;
        }

        // Eski ModelState'teki "Items[2]..." girdileri kayan indekslerle yanlış satırı doldurmasın.
        modelState.Clear();
        return true;
    }

    public static bool ValidateItemCount(ModelStateDictionary modelState, PrescriptionFormViewModel model)
    {
        if (model.Items.Count is >= 1 and <= PrescriptionFormViewModel.MaxItems) return true;
        modelState.AddModelError(nameof(model.Items), $"Reçetede 1 ile {PrescriptionFormViewModel.MaxItems} arasında ilaç kalemi olmalı.");
        return false;
    }

    public static List<DoctorPanelPrescriptionItemBody> ToBody(PrescriptionFormViewModel model)
        => model.Items.Select(i => new DoctorPanelPrescriptionItemBody(i.MedicineName, i.Dosage, i.Frequency, i.DurationDays, i.Instructions)).ToList();
}
