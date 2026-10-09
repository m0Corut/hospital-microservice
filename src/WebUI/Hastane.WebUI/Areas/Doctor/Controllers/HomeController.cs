using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class HomeController : DoctorAreaController
{
    // Sınıf düzeyindeki alan kapısı (Doctor) geçerli; action'a ek etiket gerekmez.
    public IActionResult Index() => View();
}