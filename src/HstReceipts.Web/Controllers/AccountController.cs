using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HstReceipts.Web.Controllers;

/// <summary>
/// Legacy MVC auth routes. UI is the React SPA; JSON auth is under /api/auth.
/// </summary>
public class AccountController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        var target = "/client/login";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            target += "?returnUrl=" + Uri.EscapeDataString(returnUrl);
        }

        return Redirect(target);
    }

    [HttpPost]
    [AllowAnonymous]
    public IActionResult Login()
    {
        return Redirect("/client/login");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/client/login");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return Redirect("/client/login");
    }
}
