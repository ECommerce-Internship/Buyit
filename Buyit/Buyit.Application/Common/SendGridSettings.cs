namespace Buyit.Application.Common;

public class SendGridSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "noreply@buyit.com";
    public string FromName { get; set; } = "Buyit Store";
}