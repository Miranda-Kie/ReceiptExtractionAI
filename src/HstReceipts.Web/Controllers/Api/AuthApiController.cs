using System.Security.Claims;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HstReceipts.Web.Controllers.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthApiController : ControllerBase
{
    private const string BatchSessionKey = "CurrentBatch";
    private const string AiLearningSessionKey = "AiLearningEnabled";

    private readonly IUserAuthService _authService;
    private readonly ILogger<AuthApiController> _logger;

    public AuthApiController(IUserAuthService authService, ILogger<AuthApiController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpGet("me")]
    [AllowAnonymous]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(new { authenticated = false });
        }

        return Ok(new
        {
            authenticated = true,
            username = User.Identity.Name,
            role = User.IsInRole(AppRoles.Admin) ? AppRoles.Admin
                : User.IsInRole(AppRoles.Officer) ? AppRoles.Officer
                : User.IsInRole(AppRoles.Demo) ? AppRoles.Demo
                : "User",
            isAdmin = User.IsInRole(AppRoles.Admin),
            isDemo = User.IsInRole(AppRoles.Demo)
        });
    }

    public sealed class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        var result = await _authService.ValidateCredentialsDetailedAsync(
            username,
            request.Password ?? string.Empty,
            cancellationToken);

        if (!result.Succeeded)
        {
            return Unauthorized(new { error = result.ErrorMessage ?? "Invalid username or password." });
        }

        var user = result.User!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };

        await ClearReceiptSessionAsync();
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });

        _logger.LogInformation("API sign-in for {Username} as {Role}.", user.Username, user.Role);
        return Ok(new
        {
            authenticated = true,
            username = user.Username,
            role = user.Role,
            isAdmin = string.Equals(user.Role, AppRoles.Admin, StringComparison.OrdinalIgnoreCase),
            isDemo = false
        });
    }

    /// <summary>
    /// Portfolio demo: no password. Can OCR and export Excel only — cannot save to the database.
    /// </summary>
    [HttpPost("demo")]
    [AllowAnonymous]
    public async Task<IActionResult> DemoLogin()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "demo"),
            new(ClaimTypes.Name, "demo"),
            new(ClaimTypes.Role, AppRoles.Demo)
        };

        await ClearReceiptSessionAsync();
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });

        _logger.LogInformation("API demo sign-in.");
        return Ok(new
        {
            authenticated = true,
            username = "demo",
            role = AppRoles.Demo,
            isAdmin = false,
            isDemo = true
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await ClearReceiptSessionAsync();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { authenticated = false });
    }

    private async Task ClearReceiptSessionAsync()
    {
        await HttpContext.Session.LoadAsync();
        HttpContext.Session.Remove(BatchSessionKey);
        HttpContext.Session.Remove(AiLearningSessionKey);
        HttpContext.Session.Clear();
        await HttpContext.Session.CommitAsync();
    }
}
