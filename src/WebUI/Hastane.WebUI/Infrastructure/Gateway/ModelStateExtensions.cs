using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hastane.WebUI.Infrastructure.Gateway;

public static class ModelStateExtensions
{
    // API'nin 400/409 ProblemDetails'ini form hatalarına çevirir. Alan adı eşleşirse (örn. "Email") o alanın altında,
    // eşleşmezse (Identity kodu "PasswordTooShort" gibi) model düzeyinde görünür. Görünümde
    // <div asp-validation-summary="All"> bulunmalı ki eşleşmeyenler de görünsün.
    public static void AddApiProblem(this ModelStateDictionary modelState, ApiResult result, string fallbackMessage = "İşlem tamamlanamadı.")
    {
        var problem = result.Problem;
        var added = false;

        if (problem is { Errors.Count: > 0 })
        {
            foreach (var (key, messages) in problem.Errors)
                foreach (var message in messages)
                {
                    modelState.AddModelError(key, message);
                    added = true;
                }
        }

        if (!added)
            modelState.AddModelError(string.Empty, problem?.Detail ?? problem?.Title ?? fallbackMessage);
    }
}
