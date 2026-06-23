namespace Buyit.Application.DTOs;

/// <summary>Payload to register a Seller and open their first store in one call.</summary>
public class RegisterSellerRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string? StoreDescription { get; set; }
}