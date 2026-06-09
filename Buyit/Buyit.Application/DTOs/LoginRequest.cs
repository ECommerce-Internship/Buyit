namespace Buyit.Application.DTOs;

/// <summary>The data an existing user submits to log in.</summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
