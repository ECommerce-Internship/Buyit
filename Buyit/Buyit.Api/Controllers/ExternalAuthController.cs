using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Buyit.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class ExternalAuthController : ControllerBase
{
    private readonly GoogleAuthSettings _google;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalAuthService _externalAuth;
    private readonly ILogger<ExternalAuthController> _logger;

    public ExternalAuthController(
        IOptions<GoogleAuthSettings> googleOptions,
        IHttpClientFactory httpClientFactory,
        IExternalAuthService externalAuth,
        ILogger<ExternalAuthController> logger)
    {
        _google = googleOptions.Value;
        _httpClientFactory = httpClientFactory;
        _externalAuth = externalAuth;
        _logger = logger;
    }

    /// <summary>Starts Google sign-in: sets an anti-CSRF state cookie and redirects to Google.</summary>
    [HttpGet("login/google")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult LoginGoogle()
    {
        var state = Guid.NewGuid().ToString("N");

        Response.Cookies.Append("g_state", state, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(5)
        });

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _google.ClientId,
            ["redirect_uri"] = _google.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["access_type"] = "online",
            ["prompt"] = "select_account"
        };

        var authUrl = QueryHelpers.AddQueryString(
            "https://accounts.google.com/o/oauth2/v2/auth", query);

        return Redirect(authUrl);
    }

    /// <summary>Handles Google's callback: validates state, exchanges code, returns a Buyit JWT.</summary>
    [HttpGet("callback/google")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> GoogleCallback(
        [FromQuery] string? code,
        [FromQuery] string? state)
    {
        var expectedState = Request.Cookies["g_state"];
        Response.Cookies.Delete("g_state");

        if (string.IsNullOrEmpty(state) ||
            string.IsNullOrEmpty(expectedState) ||
            state != expectedState)
        {
            _logger.LogWarning("Google callback rejected: state mismatch.");
            return BadRequest("Invalid or tampered state parameter.");
        }

        if (string.IsNullOrEmpty(code))
            return BadRequest("Missing authorization code.");

        var http = _httpClientFactory.CreateClient();

        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _google.ClientId,
            ["client_secret"] = _google.ClientSecret,
            ["redirect_uri"] = _google.RedirectUri,
            ["grant_type"] = "authorization_code"
        };

        using var tokenResponse =
            await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            var error = await tokenResponse.Content.ReadAsStringAsync();
            _logger.LogError("Google token exchange failed: {Error}", error);
            return BadRequest("Failed to exchange the authorization code with Google.");
        }

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
        if (tokens?.IdToken is null)
            return BadRequest("Google did not return an ID token.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                tokens.IdToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _google.ClientId }
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google ID token failed validation.");
            return BadRequest("The Google ID token could not be validated.");
        }

        var claims = new GoogleClaims
        {
            Subject = payload.Subject,
            Email = payload.Email,
            EmailVerified = payload.EmailVerified,
            Name = payload.Name ?? string.Empty,
            Picture = payload.Picture
        };

        var result = await _externalAuth.FindOrCreateUserAsync(claims);
        return Ok(result);
    }

    private sealed class GoogleTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }
}