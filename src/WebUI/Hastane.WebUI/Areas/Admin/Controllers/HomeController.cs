using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

public class HomeController : AdminAreaController
{
    // Sınıf düzeyindeki alan kapısı (Admin) geçerli; action'a ek etiket gerekmez.
    public IActionResult Index() => View();
}