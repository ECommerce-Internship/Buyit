namespace Buyit.Application.Common;

public class ResendSettings
{
    public string ApiKey { get; set; } = string.Empty;
    // Resend's shared test sender — works with zero domain verification, perfect for now
    // until a real domain is authenticated for production sending.
    public string FromEmail { get; set; } = "onboarding@resend.dev";
    public string FromName { get; set; } = "Buyit Store";
}