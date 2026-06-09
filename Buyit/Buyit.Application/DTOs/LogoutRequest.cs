using System;
using System.Collections.Generic;
using System.Text;

namespace Buyit.Application.DTOs;

/// <summary>The body a client sends to revoke (log out) a refresh token.</summary>
public class LogoutRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}