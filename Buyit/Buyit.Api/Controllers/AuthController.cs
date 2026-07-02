using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;   // JwtRegisteredClaimNames.Sub
using System.Security.Claims;             // User.FindFirstValue(...)
using Microsoft.AspNetCore.Authorization; // [Authorize]
using Buyit.Domain.Exceptions;            // UnauthorizedException (for the GetUserId guard)

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    /// <summary>Create a new account and receive tokens.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var result = await _auth.RegisterAsync(request);
        SetRefreshTokenCookie(result.RefreshToken);
        result.RefreshToken = string.Empty;
        return Ok(result);
    }

    /// <summary>Register as a seller: creates a Seller account + a Pending store, returns tokens.</summary>
    [HttpPost("register-seller")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> RegisterSeller([FromBody] RegisterSellerRequest request)
    {
        var result = await _auth.RegisterSellerAsync(request);
        SetRefreshTokenCookie(result.RefreshToken);
        result.RefreshToken = string.Empty;
        return Ok(result);
    }

    /// <summary>Upgrade the signed-in Customer to a Seller and open their first Pending store.
    /// Returns fresh tokens carrying the new Seller role + storeIds.</summary>
    [Authorize]
    [HttpPost("become-seller")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> BecomeSeller([FromBody] CreateStoreRequest request)
    {
        var userId = GetUserId();
        var result = await _auth.BecomeSellerAsync(userId, request);
        SetRefreshTokenCookie(result.RefreshToken);
        result.RefreshToken = string.Empty;
        return Ok(result);
    }

    /// <summary>Log in with email + password and receive tokens.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        SetRefreshTokenCookie(result.RefreshToken);
        result.RefreshToken = string.Empty;
        return Ok(result);
    }

    /// <summary>Exchange a valid refresh token (read from the HttpOnly cookie) for a fresh access + refresh token pair (rotates the old one).</summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> RefreshToken()
    {
        var cookieToken = Request.Cookies[RefreshCookieName];
        if (string.IsNullOrEmpty(cookieToken))
            throw new UnauthorizedException("No refresh token cookie present.");

        var result = await _auth.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = cookieToken });
        SetRefreshTokenCookie(result.RefreshToken);
        result.RefreshToken = string.Empty;
        return Ok(result);
    }

    /// <summary>Revoke the refresh token (read from the HttpOnly cookie) so it can no longer be used. Returns 204 No Content.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        var cookieToken = Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrEmpty(cookieToken))
            await _auth.LogoutAsync(new LogoutRequest { RefreshToken = cookieToken });

        ClearRefreshTokenCookie();
        return NoContent();
    }
    /// <summary>Get the currently-authenticated user's profile.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfileResponse>> GetMe()
    {
        var userId = GetUserId();
        var result = await _auth.GetProfileAsync(userId);
        return Ok(result);
    }

    /// <summary>Update the currently-authenticated user's first name, last name, and phone number.</summary>
    [Authorize]
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfileResponse>> UpdateMe([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserId();
        var result = await _auth.UpdateProfileAsync(userId, request);
        return Ok(result);
    }

    /// <summary>Change the currently-authenticated user's password. Returns 204 No Content.</summary>
    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetUserId();
        await _auth.ChangePasswordAsync(userId, request);
        return NoContent();
    }

    // Reads the signed-in user's id from the JWT "sub" claim. The JWT middleware already
    // validated the token before this runs, so the claim is trustworthy. We use "sub"
    // (NOT ClaimTypes.NameIdentifier) because Program.cs sets MapInboundClaims = false.
    private int GetUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!int.TryParse(sub, out var userId))
            throw new UnauthorizedException("Token is missing a valid user id.");
        return userId;
    }

    private const string RefreshCookieName = "refreshToken";

    private void SetRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            // The frontend (http://localhost:5173) and API (https://localhost:7001) differ in
            // SCHEME, which Chrome's "schemeful same-site" rules treat as cross-site even though
            // both are "localhost" — so Lax would never send this cookie back. None (+ Secure)
            // is required for it to survive a cross-scheme/cross-origin XHR like restoreSession().
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/",
        });
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/" });
    }
}