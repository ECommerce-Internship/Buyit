namespace Buyit.Application.DTOs;

/// <summary>What register and login return on success.</summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }            // access-token lifetime, in seconds
    public UserDto User { get; set; } = new();
}
