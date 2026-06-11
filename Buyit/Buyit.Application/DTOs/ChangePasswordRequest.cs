using System;
using System.Collections.Generic;
using System.Text;

namespace Buyit.Application.DTOs;

/// <summary>The body for changing the signed-in user's password.</summary>
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}