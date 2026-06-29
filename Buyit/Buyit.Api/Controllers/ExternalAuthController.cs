using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Buyit.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class ExternalAuthController : ControllerBase
{
    private readonly GoogleAuthSettings _google;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalAuthService _externalAuth;
    private readonly ILogger<ExternalAuthController> _logger;
    private readonly string _frontendBaseUrl;

    public ExternalAuthController(
        IOptions<GoogleAuthSettings> googleOptions,
        IHttpClientFactory httpClientFactory,
        IExternalAuthService externalAuth,
        ILogger<ExternalAuthController> logger,
        IConfiguration config)
    {
        _google = googleOptions.Value;
        _httpClientFactory = httpClientFactory;
        _externalAuth = externalAuth;
        _logger = logger;
        _frontendBaseUrl = config["Frontend:BaseUrl"] ?? "http://localhost:5173";
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
    [ProducesResponseType(StatusCodes.Status302Found)]
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
            return Redirect($"{_frontendBaseUrl}/auth/callback#error={Uri.EscapeDataString("Invalid or tampered state parameter.")}");
        }

        if (string.IsNullOrEmpty(code))
            return Redirect($"{_frontendBaseUrl}/auth/callback#error={Uri.EscapeDataString("Missing authorization code.")}");

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
            return Redirect($"{_frontendBaseUrl}/auth/callback#error={Uri.EscapeDataString("Failed to exchange the authorization code with Google.")}");
        }

        var tokens = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
        if (tokens?.IdToken is null)
            return Redirect($"{_frontendBaseUrl}/auth/callback#error={Uri.EscapeDataString("Google did not return an ID token.")}");

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
            return Redirect($"{_frontendBaseUrl}/auth/callback#error={Uri.EscapeDataString("The Google ID token could not be validated.")}");
        }

        var claims = new GoogleClaims
        {
            Subject = payload.Subject,
            Email = payload.Email,
            EmailVerified = payload.EmailVerified,
            Name = payload.Name ?? string.Empty,
            Picture = payload.Picture
        };

        // FindOrCreateUserAsync links/creates the account and issues tokens. If it throws
        // (e.g. ValidationException for a missing/unverified email, or an unexpected error),
        // bounce the user back to the frontend with a readable message instead of leaking raw
        // JSON on the backend URL. We catch HERE — rather than letting the exception middleware
        // produce ProblemDetails — because this action must REDIRECT to the SPA.
        AuthResponse result;
        try
        {
            result = await _externalAuth.FindOrCreateUserAsync(claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google sign-in failed while finding/creating the user.");
            return Redirect($"{_frontendBaseUrl}/auth/callback#error={Uri.EscapeDataString("Google sign-in could not be completed. Please try again.")}");
        }

        // Serialize the whole AuthResponse to JSON, then Base64URL-encode it so it is safe to
        // place inside a URL fragment. The fragment (#...) is never sent to servers / logs.
        // IMPORTANT: use Web defaults (camelCase) so the keys match what the frontend expects
        // (accessToken/refreshToken/user/...). A raw JsonSerializer.Serialize() would emit
        // PascalCase (AccessToken/User/...), which the frontend cannot read.
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        return Redirect($"{_frontendBaseUrl}/auth/callback#data={encoded}");
    }

    private sealed class GoogleTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }
}