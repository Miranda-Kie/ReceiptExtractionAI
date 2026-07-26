using System.Net;
using System.Text;

namespace HstReceipts.Infrastructure.Auth;

public static class AuthEmailTemplates
{
    private const string Brand = "#0d6e6a";
    private const string BrandSoft = "#e6f3f2";
    private const string Ink = "#1a2332";
    private const string InkSoft = "#5a6578";
    private const string Border = "#d5dee6";
    private const string Bg = "#e8eef2";

    public static (string Plain, string Html) VerificationCode(string code)
    {
        var safeCode = WebUtility.HtmlEncode(code);
        var plain =
            $"Your HST Receipts sign-in code is: {code}\n\n" +
            "This code expires in 10 minutes.\n" +
            "If you did not try to sign in, ignore this email.";

        var html = Wrap(
            title: "Sign-in verification",
            eyebrow: "HST Receipts",
            heading: "Your sign-in code",
            intro: "Use this code to finish signing in. It expires in <strong>10 minutes</strong>.",
            highlightHtml:
                $"""
                <div style="margin:24px 0;padding:20px 16px;background:{BrandSoft};border:1px solid {Border};border-radius:12px;text-align:center;">
                  <div style="font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:{InkSoft};font-weight:700;margin-bottom:8px;">Verification code</div>
                  <div style="font-size:36px;line-height:1.2;letter-spacing:0.28em;font-weight:800;color:{Brand};font-family:ui-monospace,Consolas,'Courier New',monospace;">{safeCode}</div>
                </div>
                """,
            footerNote: "If you did not try to sign in, you can safely ignore this email.");

        return (plain, html);
    }

    public static (string Plain, string Html) EmailChangeCode(string username, string code)
    {
        var safeUser = WebUtility.HtmlEncode(username);
        var safeCode = WebUtility.HtmlEncode(code);
        var plain =
            $"Confirm the new email address for HST Receipts account ({username}).\n\n" +
            $"Your verification code is: {code}\n\n" +
            "This code expires in 10 minutes.\n" +
            "If you did not request this change, ignore this email.";

        var html = Wrap(
            title: "Confirm email change",
            eyebrow: "HST Receipts",
            heading: "Confirm your new email",
            intro: $"Enter this code to confirm the new email address for <strong>{safeUser}</strong>. It expires in <strong>10 minutes</strong>.",
            highlightHtml:
                $"""
                <div style="margin:24px 0;padding:20px 16px;background:{BrandSoft};border:1px solid {Border};border-radius:12px;text-align:center;">
                  <div style="font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:{InkSoft};font-weight:700;margin-bottom:8px;">Verification code</div>
                  <div style="font-size:36px;line-height:1.2;letter-spacing:0.28em;font-weight:800;color:{Brand};font-family:ui-monospace,Consolas,'Courier New',monospace;">{safeCode}</div>
                </div>
                """,
            footerNote: "If you did not request an email change, you can safely ignore this email.");

        return (plain, html);
    }

    public static (string Plain, string Html) PasswordResetCode(string username, string code)
    {
        var safeUser = WebUtility.HtmlEncode(username);
        var safeCode = WebUtility.HtmlEncode(code);
        var plain =
            $"A password reset was requested for your HST Receipts account ({username}).\n\n" +
            $"Your verification code is: {code}\n\n" +
            "Enter this code on the sign-in page to set a new password.\n" +
            "This code expires in 10 minutes.\n" +
            "If you did not request this, ignore this email.";

        var html = Wrap(
            title: "Password reset code",
            eyebrow: "HST Receipts",
            heading: "Reset your password",
            intro: $"Enter this code on the sign-in page to set a new password for <strong>{safeUser}</strong>. It expires in <strong>10 minutes</strong>.",
            highlightHtml:
                $"""
                <div style="margin:24px 0;padding:20px 16px;background:{BrandSoft};border:1px solid {Border};border-radius:12px;text-align:center;">
                  <div style="font-size:12px;letter-spacing:0.08em;text-transform:uppercase;color:{InkSoft};font-weight:700;margin-bottom:8px;">Verification code</div>
                  <div style="font-size:36px;line-height:1.2;letter-spacing:0.28em;font-weight:800;color:{Brand};font-family:ui-monospace,Consolas,'Courier New',monospace;">{safeCode}</div>
                </div>
                """,
            footerNote: "If you did not request a password reset, you can safely ignore this email.");

        return (plain, html);
    }

    public static (string Plain, string Html) SetPasswordInvite(string username, string setPasswordUrl)
    {
        var safeUser = WebUtility.HtmlEncode(username);
        var safeUrl = WebUtility.HtmlEncode(setPasswordUrl);
        var plain =
            $"An HST Receipts account was created for you ({username}).\n\n" +
            $"Open this link to set your password and sign in:\n{setPasswordUrl}\n\n" +
            "This link expires in 2 hours.\n" +
            "If you were not expecting this email, ignore it.";

        var html = Wrap(
            title: "Set your password",
            eyebrow: "HST Receipts",
            heading: "Welcome — set your password",
            intro: $"An account was created for <strong>{safeUser}</strong>. Open the link below to choose a password. This link expires in <strong>2 hours</strong>.",
            highlightHtml:
                $"""
                <div style="margin:28px 0;text-align:center;">
                  <a href="{safeUrl}" style="display:inline-block;padding:14px 28px;background:{Brand};color:#ffffff;text-decoration:none;border-radius:999px;font-weight:700;font-size:15px;">
                    Set password
                  </a>
                </div>
                <p style="margin:0;font-size:13px;line-height:1.5;color:{InkSoft};word-break:break-all;">
                  Or paste this link into your browser:<br/>
                  <a href="{safeUrl}" style="color:{Brand};">{safeUrl}</a>
                </p>
                """,
            footerNote: "If you were not expecting this email, you can safely ignore it.");

        return (plain, html);
    }

    public static (string Plain, string Html) PasswordReset(string username, string resetUrl)
    {
        var safeUser = WebUtility.HtmlEncode(username);
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        var plain =
            $"A password reset was requested for your HST Receipts account ({username}).\n\n" +
            $"Open this link to set a new password:\n{resetUrl}\n\n" +
            "This link expires in 2 hours.\n" +
            "If you did not request this, ignore this email.";

        var html = Wrap(
            title: "Password reset",
            eyebrow: "HST Receipts",
            heading: "Reset your password",
            intro: $"We received a request to reset the password for <strong>{safeUser}</strong>. This link expires in <strong>2 hours</strong>.",
            highlightHtml:
                $"""
                <div style="margin:28px 0;text-align:center;">
                  <a href="{safeUrl}" style="display:inline-block;padding:14px 28px;background:{Brand};color:#ffffff;text-decoration:none;border-radius:999px;font-weight:700;font-size:15px;">
                    Set new password
                  </a>
                </div>
                <p style="margin:0;font-size:13px;line-height:1.5;color:{InkSoft};word-break:break-all;">
                  Or paste this link into your browser:<br/>
                  <a href="{safeUrl}" style="color:{Brand};">{safeUrl}</a>
                </p>
                """,
            footerNote: "If you did not request a password reset, you can safely ignore this email.");

        return (plain, html);
    }

    public static (string Plain, string Html) AccountRegistrationConfirmation(
        string username,
        string? signInUrl = null)
    {
        var safeUser = WebUtility.HtmlEncode(username);
        var safeSignIn = string.IsNullOrWhiteSpace(signInUrl)
            ? null
            : WebUtility.HtmlEncode(signInUrl);

        var plain = new StringBuilder();
        plain.AppendLine("HST Receipts — account registration successful");
        plain.AppendLine();
        plain.AppendLine($"Your HST Receipts account \"{username}\" is ready.");
        plain.AppendLine("Your password has been set successfully.");
        plain.AppendLine();
        if (!string.IsNullOrWhiteSpace(signInUrl))
        {
            plain.AppendLine("Sign in here:");
            plain.AppendLine(signInUrl);
            plain.AppendLine();
        }
        else
        {
            plain.AppendLine("You can sign in with your username and password.");
            plain.AppendLine();
        }

        plain.AppendLine("If you did not expect this account, contact an administrator.");

        var highlight = new StringBuilder();
        highlight.Append(
            $"""
            <p style="margin:0 0 20px 0;font-size:15px;line-height:1.55;color:{InkSoft};">
              Your password is set and your account is ready to use.
            </p>
            """);

        if (safeSignIn is not null)
        {
            highlight.Append(
                $"""
                <div style="margin:0 0 8px 0;text-align:center;">
                  <a href="{safeSignIn}" style="display:inline-block;padding:14px 28px;background:{Brand};color:#ffffff;text-decoration:none;border-radius:999px;font-weight:700;font-size:15px;">
                    Sign in to HST Receipts
                  </a>
                </div>
                """);
        }

        var html = Wrap(
            title: "Account ready",
            eyebrow: "HST Receipts",
            heading: "Registration successful",
            intro: $"Welcome — your account <strong>{safeUser}</strong> has been registered successfully.",
            highlightHtml: highlight.ToString(),
            footerNote: "If you did not expect this account, contact an administrator and do not share this email.");

        return (plain.ToString(), html);
    }

    public static (string Plain, string Html) PasswordResetConfirmation(string username, string? signInUrl = null)
    {
        var safeUser = WebUtility.HtmlEncode(username);
        var safeSignIn = string.IsNullOrWhiteSpace(signInUrl)
            ? null
            : WebUtility.HtmlEncode(signInUrl);

        var plain = new StringBuilder();
        plain.AppendLine("HST Receipts — password change confirmation");
        plain.AppendLine();
        plain.AppendLine($"This email confirms that the password for user \"{username}\" was changed successfully.");
        plain.AppendLine();
        if (!string.IsNullOrWhiteSpace(signInUrl))
        {
            plain.AppendLine("Sign in with your new password:");
            plain.AppendLine(signInUrl);
            plain.AppendLine();
        }
        else
        {
            plain.AppendLine("You can now sign in with your new password.");
            plain.AppendLine();
        }

        plain.AppendLine("For your security, the earlier reset link will no longer work.");
        plain.AppendLine("If you did not change your password, contact an administrator immediately.");

        var highlight = new StringBuilder();
        highlight.Append(
            $"""
            <p style="margin:0 0 20px 0;font-size:15px;line-height:1.55;color:{InkSoft};">
              You can sign in with your new password right away. For your security, the earlier reset link will no longer work.
            </p>
            """);

        if (safeSignIn is not null)
        {
            highlight.Append(
                $"""
                <div style="margin:0 0 8px 0;text-align:center;">
                  <a href="{safeSignIn}" style="display:inline-block;padding:14px 28px;background:{Brand};color:#ffffff;text-decoration:none;border-radius:999px;font-weight:700;font-size:15px;">
                    Sign in to HST Receipts
                  </a>
                </div>
                """);
        }

        var html = Wrap(
            title: "Password changed",
            eyebrow: "HST Receipts",
            heading: "Password change confirmation",
            intro: $"This email confirms that the password for user <strong>{safeUser}</strong> was changed successfully.",
            highlightHtml: highlight.ToString(),
            footerNote: "If you did not change your password, contact an administrator immediately and do not share this email.");

        return (plain.ToString(), html);
    }

    private static string Wrap(
        string title,
        string eyebrow,
        string heading,
        string intro,
        string highlightHtml,
        string footerNote)
    {
        var sb = new StringBuilder();
        sb.Append(
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{WebUtility.HtmlEncode(title)}</title>
            </head>
            <body style="margin:0;padding:0;background:{Bg};font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Bg};padding:32px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#ffffff;border:1px solid {Border};border-radius:16px;overflow:hidden;">
                      <tr>
                        <td style="padding:20px 28px;background:{Brand};">
                          <div style="font-size:13px;font-weight:800;letter-spacing:0.04em;color:#ffffff;">{WebUtility.HtmlEncode(eyebrow)}</div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:28px 28px 8px 28px;">
                          <h1 style="margin:0 0 12px 0;font-size:24px;line-height:1.25;color:{Ink};">{WebUtility.HtmlEncode(heading)}</h1>
                          <p style="margin:0;font-size:15px;line-height:1.55;color:{InkSoft};">{intro}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:8px 28px 24px 28px;">
                          {highlightHtml}
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:0 28px 28px 28px;">
                          <p style="margin:0;font-size:13px;line-height:1.5;color:{InkSoft};">{WebUtility.HtmlEncode(footerNote)}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:16px 28px;background:#f5f8fa;border-top:1px solid {Border};">
                          <p style="margin:0;font-size:12px;color:{InkSoft};">HST Receipts · Receipt extraction &amp; export</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """);
        return sb.ToString();
    }
}
