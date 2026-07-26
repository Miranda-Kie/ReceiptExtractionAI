using System.Security.Claims;
using HstReceipts.Core.Entities;
using HstReceipts.Core.Interfaces;
using HstReceipts.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HstReceipts.Web.Controllers.Api;

[ApiController]
[Route("api/users")]
[Authorize(Roles = $"{AppRoles.Owner},{AppRoles.Admin}")]
public sealed class UsersApiController : ControllerBase
{
    private readonly IUserAuthService _authService;
    private readonly IPasswordResetService _passwordReset;
    private readonly IEmailChangeVerificationService _emailChange;
    private readonly IEmailSender _emailSender;
    private readonly IHostEnvironment _environment;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<UsersApiController> _logger;

    public UsersApiController(
        IUserAuthService authService,
        IPasswordResetService passwordReset,
        IEmailChangeVerificationService emailChange,
        IEmailSender emailSender,
        IHostEnvironment environment,
        IHttpContextAccessor httpContextAccessor,
        ILogger<UsersApiController> logger)
    {
        _authService = authService;
        _passwordReset = passwordReset;
        _emailChange = emailChange;
        _emailSender = emailSender;
        _environment = environment;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var users = await _authService.ListUsersAsync(cancellationToken);
        var payload = users.Select(u => new
        {
            id = u.Id,
            username = u.Username,
            email = u.Email,
            role = u.Role,
            isActive = u.IsActive,
            status = u.Status
        });

        return Ok(new { users = payload });
    }

    public sealed class CreateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = AppRoles.Officer;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId))
        {
            return Unauthorized();
        }

        var (ok, error, created) = await _authService.CreateUserAsync(
            request.Username,
            request.Role,
            request.Email,
            requesterId,
            cancellationToken);
        if (!ok || created is null)
        {
            return BadRequest(new { error = error ?? "Could not create user." });
        }

        var ticket = _passwordReset.CreateTicket(
            created.Id,
            created.Username,
            created.Email!,
            forSetPasswordInvite: true);
        var absoluteLink = $"{Request.Scheme}://{Request.Host}{ticket.ResetPath}";
        var emailSent = await TrySendSetPasswordInviteAsync(ticket, absoluteLink, cancellationToken);
        _logger.LogInformation(
            "Created user {Username}; set-password invite emailSent={EmailSent}.",
            created.Username,
            emailSent);

        if (_environment.IsDevelopment() && !emailSent)
        {
            return Ok(new
            {
                ok = true,
                emailSent,
                maskedEmail = ticket.MaskedEmail,
                message = _emailSender.IsConfigured
                    ? $"User {created.Username} created. Could not email set-password link to {ticket.MaskedEmail}. Use the development link below."
                    : $"User {created.Username} created. SMTP is not configured — use the development link for {ticket.MaskedEmail}.",
                setPasswordLink = ticket.ResetPath,
                user = new
                {
                    id = created.Id,
                    username = created.Username,
                    email = created.Email,
                    role = created.Role,
                    isActive = created.IsActive,
                    status = created.Status
                }
            });
        }

        return Ok(new
        {
            ok = true,
            emailSent,
            maskedEmail = ticket.MaskedEmail,
            message = emailSent
                ? $"User {created.Username} created. A set-password link was sent to {ticket.MaskedEmail}."
                : $"User {created.Username} created, but the set-password email could not be sent to {ticket.MaskedEmail}.",
            user = new
            {
                id = created.Id,
                username = created.Username,
                email = created.Email,
                role = created.Role,
                isActive = created.IsActive,
                status = created.Status
            }
        });
    }

    private async Task<bool> TrySendSetPasswordInviteAsync(
        PasswordResetTicket ticket,
        string absoluteLink,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Set-password link for {Username} was not emailed.",
                ticket.Username);
            return false;
        }

        try
        {
            var (plain, html) = AuthEmailTemplates.SetPasswordInvite(ticket.Username, absoluteLink);
            await _emailSender.SendAsync(
                ticket.Email,
                "HST Receipts — set your password",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to email set-password link to {MaskedEmail}.",
                ticket.MaskedEmail);
            return false;
        }
    }

    public sealed class SetPasswordRequest
    {
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> SetPassword(
        Guid id,
        [FromBody] SetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var (ok, error) = await _authService.SetPasswordAsync(id, request.Password ?? string.Empty, cancellationToken);
        if (!ok)
        {
            return BadRequest(new { error });
        }

        return Ok(new { ok = true });
    }

    public sealed class UpdateEmailRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public sealed class VerifyEmailChangeRequest
    {
        public string VerificationToken { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public sealed class ResendEmailChangeRequest
    {
        public string VerificationToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Starts an email change for the signed-in account holder only.
    /// The account email is updated only after <see cref="VerifyEmailChange"/>.
    /// </summary>
    [HttpPut("{id:guid}/email")]
    public async Task<IActionResult> BeginEmailChange(
        Guid id,
        [FromBody] UpdateEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAccountHolder(id))
        {
            return Forbid();
        }

        var (ok, error) = await _authService.ValidateEmailChangeAsync(id, request.Email, cancellationToken);
        if (!ok)
        {
            return BadRequest(new { error });
        }

        var user = await _authService.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound(new { error = "User not found." });
        }

        var challenge = _emailChange.CreateChallenge(user.Id, user.Username, request.Email.Trim());
        var emailSent = await TrySendEmailChangeCodeAsync(challenge, cancellationToken);
        return Ok(BuildEmailChangeResponse(challenge, emailSent));
    }

    [HttpPost("{id:guid}/email/verify")]
    public async Task<IActionResult> VerifyEmailChange(
        Guid id,
        [FromBody] VerifyEmailChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAccountHolder(id))
        {
            return Forbid();
        }

        var token = request.VerificationToken ?? string.Empty;
        if (!_emailChange.TryValidate(
                token,
                request.Code ?? string.Empty,
                id,
                out var newEmail) ||
            string.IsNullOrWhiteSpace(newEmail))
        {
            return Unauthorized(new { error = "Invalid or expired verification code." });
        }

        // Persist the new address first; only then consume the code so a failed save can retry.
        var (ok, error) = await _authService.UpdateEmailAsync(id, newEmail, cancellationToken);
        if (!ok)
        {
            return BadRequest(new { error = error ?? "Could not update email." });
        }

        _emailChange.Consume(token);
        _logger.LogInformation("Email verified and saved for user {UserId} → {Email}.", id, newEmail);
        return Ok(new { ok = true, email = newEmail });
    }

    [HttpPost("{id:guid}/email/resend")]
    public async Task<IActionResult> ResendEmailChangeCode(
        Guid id,
        [FromBody] ResendEmailChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAccountHolder(id))
        {
            return Forbid();
        }

        var existing = _emailChange.GetChallenge(request.VerificationToken ?? string.Empty);
        if (existing is null || existing.UserId != id)
        {
            return Unauthorized(new { error = "Verification session expired. Start the email change again." });
        }

        var result = _emailChange.Resend(request.VerificationToken ?? string.Empty);
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

            return Unauthorized(new { error = result.Error ?? "Verification session expired. Start the email change again." });
        }

        var emailSent = await TrySendEmailChangeCodeAsync(result.Challenge, cancellationToken);
        return Ok(BuildEmailChangeResponse(result.Challenge, emailSent));
    }

    private async Task<bool> TrySendEmailChangeCodeAsync(
        EmailChangeChallenge challenge,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Email-change code for {Username} → {MaskedEmail} was not emailed.",
                challenge.Username,
                challenge.MaskedEmail);
            return false;
        }

        try
        {
            var (plain, html) = AuthEmailTemplates.EmailChangeCode(challenge.Username, challenge.Code);
            await _emailSender.SendAsync(
                challenge.NewEmail,
                "HST Receipts confirm email change",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to email email-change code to {MaskedEmail}.",
                challenge.MaskedEmail);
            return false;
        }
    }

    private bool IsAccountHolder(Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId) &&
        requesterId == userId;

    private object BuildEmailChangeResponse(EmailChangeChallenge challenge, bool emailSent)
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

    public sealed class UpdateStatusRequest
    {
        public bool IsActive { get; set; }
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId))
        {
            return Unauthorized();
        }

        var (ok, error) = await _authService.UpdateStatusAsync(
            id,
            request.IsActive,
            requesterId,
            cancellationToken);
        if (!ok)
        {
            return BadRequest(new { error });
        }

        return Ok(new
        {
            ok = true,
            isActive = request.IsActive,
            status = request.IsActive ? UserStatus.Active : UserStatus.Inactive
        });
    }

    public sealed class UpdateRoleRequest
    {
        public string Role { get; set; } = AppRoles.Officer;
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId))
        {
            return Unauthorized();
        }

        var (ok, error) = await _authService.UpdateRoleAsync(
            id,
            request.Role,
            requesterId,
            cancellationToken);
        if (!ok)
        {
            return BadRequest(new { error });
        }

        return Ok(new { ok = true, role = request.Role?.Trim() });
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId))
        {
            return Unauthorized();
        }

        var requester = await _authService.FindByIdAsync(requesterId, cancellationToken);
        if (requester is null || !AppRoles.CanManageUsers(requester.Role))
        {
            return Forbid();
        }

        var user = await _authService.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound(new { error = "User not found." });
        }

        var isSelf = requesterId == id;
        // Signed-in Owner/Admin may always reset their own password; otherwise Owner-level
        // targets require an Owner actor (same rule as role/status management).
        if (!isSelf)
        {
            if (AppRoles.IsOwner(user.Role) && !AppRoles.IsOwner(requester.Role))
            {
                return BadRequest(new { error = "Only an Owner can reset an Owner password." });
            }

            if (AppRoles.IsAdmin(user.Role) && !AppRoles.IsOwner(requester.Role))
            {
                return BadRequest(new { error = "Only an Owner can reset an Admin password." });
            }
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return BadRequest(new { error = "User has no email for password reset." });
        }

        var ticket = _passwordReset.CreateTicket(user.Id, user.Username, user.Email);
        _logger.LogInformation(
            "{Actor} requested password reset for {Username} (self={IsSelf}).",
            requester.Role,
            user.Username,
            isSelf);

        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is null
            ? string.Empty
            : $"{request.Scheme}://{request.Host}";
        var absoluteLink = string.IsNullOrEmpty(baseUrl)
            ? ticket.ResetPath
            : $"{baseUrl}{ticket.ResetPath}";

        var emailSent = false;
        if (_emailSender.IsConfigured)
        {
            try
            {
                var (plain, html) = AuthEmailTemplates.PasswordReset(user.Username, absoluteLink);
                await _emailSender.SendAsync(
                    user.Email,
                    "HST Receipts password reset",
                    plain,
                    html,
                    cancellationToken);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to email password reset to {MaskedEmail}.", ticket.MaskedEmail);
            }
        }

        if (_environment.IsDevelopment() && !emailSent)
        {
            return Ok(new
            {
                ok = true,
                maskedEmail = ticket.MaskedEmail,
                emailSent,
                message = _emailSender.IsConfigured
                    ? $"Could not email reset link to {ticket.MaskedEmail}. Use the development link below."
                    : $"SMTP is not configured. Use the development link for {ticket.MaskedEmail}.",
                resetLink = ticket.ResetPath
            });
        }

        return Ok(new
        {
            ok = true,
            maskedEmail = ticket.MaskedEmail,
            emailSent,
            message = emailSent
                ? $"Password reset link sent to {ticket.MaskedEmail}."
                : $"Could not send email to {ticket.MaskedEmail}."
        });
    }

}

[ApiController]
[Route("api/password-reset")]
[AllowAnonymous]
public sealed class PasswordResetApiController : ControllerBase
{
    private readonly IPasswordResetService _passwordReset;
    private readonly IUserAuthService _authService;
    private readonly IEmailSender _emailSender;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PasswordResetApiController> _logger;

    public PasswordResetApiController(
        IPasswordResetService passwordReset,
        IUserAuthService authService,
        IEmailSender emailSender,
        IHostEnvironment environment,
        ILogger<PasswordResetApiController> logger)
    {
        _passwordReset = passwordReset;
        _authService = authService;
        _emailSender = emailSender;
        _environment = environment;
        _logger = logger;
    }

    public sealed class RequestResetRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public sealed class ResendResetRequest
    {
        public string VerificationToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sign-in "Forgot password": email a reset link to the registered address.
    /// Always returns a success-shaped payload to avoid account enumeration.
    /// </summary>
    [HttpPost("request")]
    public async Task<IActionResult> RequestByEmail(
        [FromBody] RequestResetRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        var maskedSubmitted = LoginVerificationService.MaskEmail(
            string.IsNullOrWhiteSpace(email) ? "user@example.com" : email);
        var genericMessage =
            "If that email is registered, a password reset link was sent. Open the link in the email to set a new password.";

        PasswordResetTicket? ticket = null;
        var emailSent = false;

        if (email.Length > 0 &&
            email.Count(c => c == '@') == 1 &&
            !email.StartsWith('@') &&
            !email.EndsWith('@'))
        {
            var user = await _authService.FindByEmailAsync(email, cancellationToken);
            if (user is not null &&
                user.IsActive &&
                !string.IsNullOrWhiteSpace(user.Email))
            {
                ticket = _passwordReset.CreateTicket(user.Id, user.Username, user.Email);
                emailSent = await TrySendPasswordResetLinkAsync(ticket, cancellationToken);
                _logger.LogInformation(
                    "Forgot-password link requested for {Username} ({MaskedEmail}), emailSent={EmailSent}.",
                    user.Username,
                    ticket.MaskedEmail,
                    emailSent);
            }
        }

        var maskedEmail = ticket?.MaskedEmail ?? maskedSubmitted;
        var includeDevLink = _environment.IsDevelopment() && ticket is not null && !emailSent;

        if (includeDevLink)
        {
            return Ok(new
            {
                ok = true,
                message = _emailSender.IsConfigured
                    ? $"Could not email a reset link to {maskedEmail}. Use the development link below."
                    : $"Email delivery is not configured. Use the development link for {maskedEmail}.",
                maskedEmail,
                emailSent,
                emailConfigured = _emailSender.IsConfigured,
                resetLink = ticket!.ResetPath
            });
        }

        return Ok(new
        {
            ok = true,
            message = ticket is not null && emailSent
                ? $"Password reset link sent to {maskedEmail}."
                : genericMessage,
            maskedEmail,
            emailSent,
            emailConfigured = _emailSender.IsConfigured
        });
    }

    [HttpPost("resend")]
    public async Task<IActionResult> ResendCode(
        [FromBody] ResendResetRequest request,
        CancellationToken cancellationToken)
    {
        var result = _passwordReset.ResendCode(request.VerificationToken ?? string.Empty);
        if (!result.Ok || result.Ticket is null)
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

            return Unauthorized(new { error = result.Error ?? "Verification session expired. Request a new password reset." });
        }

        var ticket = result.Ticket;
        var emailSent = await TrySendPasswordResetCodeAsync(ticket, cancellationToken);
        var includeDevCode = _environment.IsDevelopment() && !emailSent;
        if (includeDevCode)
        {
            return Ok(new
            {
                ok = true,
                verificationToken = ticket.Token,
                maskedEmail = ticket.MaskedEmail,
                emailSent,
                emailConfigured = _emailSender.IsConfigured,
                expiresAtUtc = ticket.ExpiresAtUtc,
                canResendAtUtc = ticket.CanResendAtUtc,
                devCode = ticket.Code,
                message = emailSent
                    ? $"A new code was sent to {ticket.MaskedEmail}."
                    : $"Could not email a new code to {ticket.MaskedEmail}. Use the development code below."
            });
        }

        return Ok(new
        {
            ok = true,
            verificationToken = ticket.Token,
            maskedEmail = ticket.MaskedEmail,
            emailSent,
            emailConfigured = _emailSender.IsConfigured,
            expiresAtUtc = ticket.ExpiresAtUtc,
            canResendAtUtc = ticket.CanResendAtUtc,
            message = emailSent
                ? $"A new code was sent to {ticket.MaskedEmail}."
                : $"Could not email a new code to {ticket.MaskedEmail}."
        });
    }

    [HttpGet("{token}")]
    public IActionResult Peek(string token)
    {
        var ticket = _passwordReset.Peek(token);
        if (ticket is null)
        {
            return NotFound(new { error = "Reset link is invalid or expired." });
        }

        return Ok(new
        {
            username = ticket.Username,
            maskedEmail = ticket.MaskedEmail,
            isSetPasswordInvite = ticket.IsSetPasswordInvite
        });
    }

    public sealed class CompleteResetRequest
    {
        public string Token { get; set; } = string.Empty;
        /// <summary>Required for forgot-password code flow; omit when using an emailed reset link.</summary>
        public string? Code { get; set; }
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete(
        [FromBody] CompleteResetRequest request,
        CancellationToken cancellationToken)
    {
        var token = request.Token ?? string.Empty;
        PasswordResetTicket? ticket;

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            if (!_passwordReset.TryValidateCode(token, request.Code, out ticket) || ticket is null)
            {
                return BadRequest(new { error = "Invalid or expired verification code." });
            }
        }
        else
        {
            // Link flow: peek first so a failed password rule does not burn the ticket.
            ticket = _passwordReset.Peek(token);
            if (ticket is null)
            {
                return BadRequest(new
                {
                    error = "This link is invalid or expired. Request a new email if you still need to set or reset your password."
                });
            }
        }

        var isInvite = ticket.IsSetPasswordInvite;
        var newPassword = request.Password ?? string.Empty;
        var (ok, error) = await _authService.SetPasswordAsync(
            ticket.UserId,
            newPassword,
            cancellationToken);
        if (!ok)
        {
            return BadRequest(new { error });
        }

        var loginCheck = await _authService.ValidateCredentialsDetailedAsync(
            ticket.Username,
            newPassword,
            cancellationToken);
        if (!loginCheck.Succeeded && !loginCheck.IsInactive)
        {
            _logger.LogError(
                "Password {Kind} for {Username} saved but hash verification failed: {Error}",
                isInvite ? "set-invite" : "reset",
                ticket.Username,
                loginCheck.ErrorMessage);
            return BadRequest(new
            {
                error = isInvite
                    ? "Password could not be verified after save. Ask an admin to send a new set-password link."
                    : "Password could not be verified after save. Request a new reset."
            });
        }

        if (!_passwordReset.TryConsume(token, out _))
        {
            _logger.LogWarning(
                "Password ticket for {Username} could not be consumed after success.",
                ticket.Username);
        }

        var confirmationSent = isInvite
            ? await TrySendAccountRegistrationConfirmationAsync(ticket, cancellationToken)
            : await TrySendPasswordResetConfirmationAsync(ticket, cancellationToken);
        _logger.LogInformation(
            "Password {Kind} completed and verified for {Username}; confirmationEmailSent={EmailSent}.",
            isInvite ? "set-invite" : "reset",
            ticket.Username,
            confirmationSent);
        return Ok(new
        {
            ok = true,
            emailSent = confirmationSent,
            maskedEmail = ticket.MaskedEmail,
            isSetPasswordInvite = isInvite,
            message = isInvite
                ? confirmationSent
                    ? $"Account ready. A registration confirmation was sent to {ticket.MaskedEmail}."
                    : $"Password saved. A registration confirmation email could not be sent to {ticket.MaskedEmail}."
                : confirmationSent
                    ? $"A confirmation has been sent to {ticket.MaskedEmail}."
                    : $"Password updated. A confirmation email could not be sent to {ticket.MaskedEmail}."
        });
    }

    private async Task<bool> TrySendPasswordResetLinkAsync(
        PasswordResetTicket ticket,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Password-reset link for {Username} was not emailed.",
                ticket.Username);
            return false;
        }

        try
        {
            var absoluteLink = $"{Request.Scheme}://{Request.Host}{ticket.ResetPath}";
            var (plain, html) = AuthEmailTemplates.PasswordReset(ticket.Username, absoluteLink);
            await _emailSender.SendAsync(
                ticket.Email,
                "HST Receipts password reset",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to email password-reset link to {MaskedEmail}.",
                ticket.MaskedEmail);
            return false;
        }
    }

    private async Task<bool> TrySendPasswordResetCodeAsync(
        PasswordResetTicket ticket,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Password-reset code for {Username} was not emailed.",
                ticket.Username);
            return false;
        }

        try
        {
            var (plain, html) = AuthEmailTemplates.PasswordResetCode(ticket.Username, ticket.Code);
            await _emailSender.SendAsync(
                ticket.Email,
                "HST Receipts password reset code",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to email password-reset code to {MaskedEmail}.",
                ticket.MaskedEmail);
            return false;
        }
    }

    private async Task<bool> TrySendAccountRegistrationConfirmationAsync(
        PasswordResetTicket ticket,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Registration confirmation for {Username} was not emailed.",
                ticket.Username);
            return false;
        }

        try
        {
            var signInUrl = $"{Request.Scheme}://{Request.Host}/client/login?signin=1";
            var (plain, html) = AuthEmailTemplates.AccountRegistrationConfirmation(
                ticket.Username,
                signInUrl);
            await _emailSender.SendAsync(
                ticket.Email,
                "HST Receipts — account registration successful",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to email registration confirmation to {MaskedEmail}.",
                ticket.MaskedEmail);
            return false;
        }
    }

    private async Task<bool> TrySendPasswordResetConfirmationAsync(
        PasswordResetTicket ticket,
        CancellationToken cancellationToken)
    {
        if (!_emailSender.IsConfigured)
        {
            _logger.LogWarning(
                "SMTP is not configured. Password-reset confirmation for {Username} was not emailed.",
                ticket.Username);
            return false;
        }

        try
        {
            var signInUrl = $"{Request.Scheme}://{Request.Host}/client/login?signin=1";
            var (plain, html) = AuthEmailTemplates.PasswordResetConfirmation(ticket.Username, signInUrl);
            await _emailSender.SendAsync(
                ticket.Email,
                "Your HST Receipts password was changed",
                plain,
                html,
                cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to email password-reset confirmation to {MaskedEmail}.",
                ticket.MaskedEmail);
            return false;
        }
    }
}
