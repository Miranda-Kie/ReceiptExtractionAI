using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HstReceipts.Web.Controllers;

/// <summary>
/// Legacy MVC entry points. Primary UI is the React SPA at /client/;
/// receipt operations live under /api/receipts.
/// </summary>
[Authorize]
public class ReceiptsController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult Process() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult SetAiLearning() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult Export() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult Save() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult CompareExport() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult ExportOnly() => Redirect("/client/");

    [HttpPost]
    [HttpGet]
    public IActionResult ExportSave() => Redirect("/client/");
}
