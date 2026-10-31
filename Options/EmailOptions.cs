namespace backend_stockpilot.Options;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;      // e.g. smtp.gmail.com
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;  // the sending mailbox
    public string Password { get; set; } = string.Empty;  // app password, set via user-secrets
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "StockPilot";
    public string LoginUrl { get; set; } = "http://localhost:5173/login";
}