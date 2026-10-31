using System.Net;
using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using backend_stockpilot.Options;
using backend_stockpilot.Services.Interfaces;

namespace backend_stockpilot.Services;

public class EmailService(
    IOptions<EmailOptions> options,
    IHostEnvironment env,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly EmailOptions _o = options.Value;

    public async Task SendInvitationAsync(
        string toEmail, string role, string temporaryPassword, string invitedBy, CancellationToken ct = default)
    {
        // Development convenience: with no SMTP host configured, log the message instead of sending it
        if (string.IsNullOrWhiteSpace(_o.Host))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException("Email is not configured (Email:Host is empty).");

            logger.LogWarning(
                "DEV ONLY - no SMTP configured. Invitation for {Email}: temporary password is {Password}",
                toEmail, temporaryPassword);
            return;
        }

        var roleLabel = Regex.Replace(role, "(?<=[a-z])(?=[A-Z])", " ");
        Func<string, string> e = WebUtility.HtmlEncode;

        var text =
            $"Hi,\n\n{invitedBy} added you to StockPilot as {roleLabel}.\n\n" +
            $"Email: {toEmail}\nTemporary password: {temporaryPassword}\n\n" +
            $"Sign in at {_o.LoginUrl} with this email and password, or use \"Continue with Google\" " +
            "with the same email address. You'll be asked to complete your profile and choose a new password.\n\n" +
            "Because this email contains your temporary password, please change it right away and then delete this message.";

        var html =
            $"<p>Hi,</p><p><strong>{e(invitedBy)}</strong> added you to StockPilot as <strong>{e(roleLabel)}</strong>.</p>" +
            $"<p>Email: <strong>{e(toEmail)}</strong><br>Temporary password: <strong style=\"font-family:monospace\">{e(temporaryPassword)}</strong></p>" +
            $"<p><a href=\"{e(_o.LoginUrl)}\">Sign in to StockPilot</a> with this email and password, or use <em>Continue with Google</em> " +
            "with the same email address. You'll be asked to complete your profile and choose a new password.</p>" +
            "<p style=\"color:#64748b\">Because this email contains your temporary password, please change it right away and then delete this message.</p>";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "You've been added to StockPilot";
        message.Body = new BodyBuilder { TextBody = text, HtmlBody = html }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(_o.Host, _o.Port, SecureSocketOptions.Auto, ct);
        await client.AuthenticateAsync(_o.Username, _o.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}