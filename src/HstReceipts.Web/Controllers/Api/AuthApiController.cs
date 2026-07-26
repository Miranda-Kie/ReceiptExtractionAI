using System.Security.Claims;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Core.Options;
using HstReceipts.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HstReceipts.Web.Controllers.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthApiController : ControllerBase
{
    private const string BatchSessionKey = "CurrentBatch";

    private readonly IUserAuthService _authService;
    private readonly ILoginVerificationService _verification;
    private readonly IEmailSender _emailSender;
    private readonly IHostEnvironment _environment;
    private readonly DocumentIntelligenceOptions _documentIntelligence;
    private readonly ILogger<AuthApiController> _logger;

    public AuthApiController(
        IUserAuthService authService,
        ILoginVerificationService verification,
        IEmailSender emailSender,
        IHostEnvironment environment,
        IOptions<DocumentIntelligenceOptions> documentIntelligence,
        ILogger<AuthApiController> logger)
    {
        _authService = authService;
        _verification = verification;
        _emailSender = emailSender;
        _environment = environment;
        _documentIntelligence = documentIntelligence.Value;
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

        var role = User.IsInRole(AppRoles.Owner) ? AppRoles.Owner
            : User.IsInRole(AppRoles.Admin) ? AppRoles.Admin
            : User.IsInRole(AppRoles.Officer) ? AppRoles.Officer
            : User.IsInRole(AppRoles.Demo) ? AppRoles.Demo
            : "User";
        return Ok(new
        {
            authenticated = true,
            username = User.Identity.Name,
            role,
            isOwner = AppRoles.IsOwner(role),
            isAdmin = AppRoles.IsAdmin(role),
            canManageUsers = AppRoles.CanManageUsers(role),
            isDemo = User.IsInRole(AppRoles.Demo),
            canSaveToDatabase = CanSaveToDatabase(role)
        });
    }

    public sealed class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public sealed class VerifyRequest
    {
        public string VerificationToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public sealed class ResendRequest
    {
        public string VerificationToken { get; set; } = string.Empty;
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
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new { error = "This account has no email on file for verification. Contact an administrator." });
        }

        var challenge = _verification.CreateChallenge(user);
        var emailSent = await TrySendVerificationEmailAsync(challenge, cancellationToken);
        _logger.LogInformation(
            "Password OK for {Username}; verification required ({MaskedEmail}), emailSent={EmailSent}.",
            user.Username,
            challenge.MaskedEmail,
            emailSent);

        return Ok(BuildVerificationResponse(challenge, emailSent));
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest request)
    {
        if (!_verification.TryConsume(request.VerificationToken ?? string.Empty, request.Code ?? string.Empty, out var snapshot) ||
            snapshot is null)
        {
            return Unauthorized(new { error = "Invalid or expired verification code." });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, snapshot.Id.ToString()),
            new(ClaimTypes.Name, snapshot.Username),
            new(ClaimTypes.Role, snapshot.Role)
        };

        await ClearReceiptSessionAsync();
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = false });

        _logger.LogInformation("API verified sign-in for {Username} as {Role}.", snapshot.Username, snapshot.Role);
        return Ok(new
        {
            authenticated = true,
            username = snapshot.Username,
            role = snapshot.Role,
            isOwner = AppRoles.IsOwner(snapshot.Role),
            isAdmin = AppRoles.IsAdmin(snapshot.Role),
            canManageUsers = AppRoles.CanManageUsers(snapshot.Role),
            isDemo = false,
            canSaveToDatabase = CanSaveToDatabase(snapshot.Role)
        });
    }

    [HttpPost("resend-code")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendCode(
        [FromBody] ResendRequest request,
        CancellationToken cancellationToken)
    {
        var result = _verification.Resend(request.VerificationToken ?? string.Empty);
        if (!result.Ok || result.Challenge is null)
        {
            if (result.RetryAfterSeconds is int retryAfter)
            {
                Response.Headers.RetryAfter = retryAfter.ToString();
                return StatusCode(
                    StatusCodes.Status429TooManyRequests,
                    new
                    {
                        error = result.Error ?? "Please wait before requesting another code.",
                        retryAfterSeconds = retryAfter
                    });
            }

            return Unauthorized(new { error = result.Error ?? "Verification session expired. Sign in again." });
        }

        var emailSent = await TrySendVerificationEmailAsync(result.Challenge, cancellationToken);
        return Ok(BuildVerificationResponse(result.Challenge, emailSent));
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
            isOwner = false,
            isAdmin = false,
            canManageUsers = false,
            isDemo = true,
            canSaveToDatabase = false
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

    private async Task<bool> TrySendVerificationEmailAsync(
        LoginVerificationChallenge challenge,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Verification code for {MaskedEmail} was not emailed. Set Smtp in appsettings.Development.local.json.",
                challenge.MaskedEmail);
            return false;
        }

        try
        {
            var (plain, html) = AuthEmailTemplates.VerificationCode(challenge.Code);
            await _emailSender.SendAsync(
                challenge.Email,
                "HST Receipts verification code",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email verification code to {MaskedEmail}.", challenge.MaskedEmail);
            return false;
        }
    }

    private object BuildVerificationResponse(LoginVerificationChallenge challenge, bool emailSent)
    {
        var includeDevCode = _environment.IsDevelopment() && !emailSent;
        if (includeDevCode)
        {
            return new
            {
                requiresVerification = true,
                verificationToken = challenge.VerificationToken,
                maskedEmail = challenge.MaskedEmail,
                emailSent,
                emailConfigured = _emailSender.IsConfigured,
                expiresAtUtc = challenge.ExpiresAtUtc,
                canResendAtUtc = challenge.CanResendAtUtc,
                // Shown only in Development when SMTP send did not succeed.
                devCode = challenge.Code,
                message = emailSent
                    ? $"A verification code was sent to {challenge.MaskedEmail}."
                    : _emailSender.IsConfigured
                        ? $"Could not send email to {challenge.MaskedEmail}. Use the development code below or check SMTP settings."
                        : $"Email delivery is not configured. Use the development code below, or add Smtp settings in appsettings.Development.local.json."
            };
        }

        return new
        {
            requiresVerification = true,
            verificationToken = challenge.VerificationToken,
            maskedEmail = challenge.MaskedEmail,
            emailSent,
            emailConfigured = _emailSender.IsConfigured,
            expiresAtUtc = challenge.ExpiresAtUtc,
            canResendAtUtc = challenge.CanResendAtUtc,
            message = emailSent
                ? $"A verification code was sent to {challenge.MaskedEmail}."
                : $"Could not send email to {challenge.MaskedEmail}. Contact an administrator."
        };
    }

    private async Task ClearReceiptSessionAsync()
    {
        await HttpContext.Session.LoadAsync();
        HttpContext.Session.Remove(BatchSessionKey);
        HttpContext.Session.Clear();
        await HttpContext.Session.CommitAsync();
    }

    private bool CanSaveToDatabase(string role)
    {
        if (AppRoles.IsOwner(role)) return true;
        if (AppRoles.IsAdmin(role)) return _documentIntelligence.IsConfigured;
        return false;
    }
}
