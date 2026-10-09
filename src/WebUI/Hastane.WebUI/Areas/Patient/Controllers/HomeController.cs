using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class HomeController : PatientAreaController
{
    // Sınıf düzeyindeki alan kapısı (Patient) geçerli; action'a ek etiket gerekmez.
    public IActionResult Index() => View();
}