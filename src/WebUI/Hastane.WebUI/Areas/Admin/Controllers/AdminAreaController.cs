using Hastane.Shared.Security;
using Hastane.WebUI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Admin alanındaki TÜM controller'lar bunu miras alır: alan etiketi + rol kapısı tek yerde.
// Alan içindeki action'lara ayrıca [Authorize(Roles = ...)] KOYMA: sınıf + action etiketleri VE'lenir, rol daraltır/kilitler.
[Area("Admin")]
[Authorize(Roles = HastaneRoles.Admin)]
public abstract class AdminAreaController : AppController
{
}