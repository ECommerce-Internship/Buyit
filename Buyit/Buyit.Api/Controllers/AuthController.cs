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
        return Ok(result);
    }

    /// <summary>Log in with email + password and receive tokens.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return Ok(result);
    }

    /// <summary>Exchange a valid refresh token for a fresh access + refresh token pair (rotates the old one).</summary>
    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _auth.RefreshTokenAsync(request);
        return Ok(result);
    }

    /// <summary>Revoke a refresh token so it can no longer be used. Returns 204 No Content.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        await _auth.LogoutAsync(request);
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
}