using System;
using System.Collections.Generic;
using System.Text;

namespace Buyit.Application.DTOs;

/// <summary>The body a client sends to exchange a refresh token for a fresh token pair.</summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}