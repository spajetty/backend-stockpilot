namespace backend_stockpilot.Services.Interfaces;

public interface IEmailService
{
    Task SendInvitationAsync(
        string toEmail,
        string role,
        string temporaryPassword,
        string invitedBy,
        CancellationToken ct = default);
}