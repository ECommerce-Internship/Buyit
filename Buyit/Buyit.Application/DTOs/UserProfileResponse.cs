using System;
using System.Collections.Generic;
using System.Text;

namespace Buyit.Application.DTOs;

/// <summary>A safe, public view of the signed-in user's profile (no password hash).</summary>
public class UserProfileResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}