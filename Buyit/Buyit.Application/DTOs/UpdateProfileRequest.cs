using System;
using System.Collections.Generic;
using System.Text;

namespace Buyit.Application.DTOs;

/// <summary>The fields a user is allowed to change on their own profile.</summary>
public class UpdateProfileRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}