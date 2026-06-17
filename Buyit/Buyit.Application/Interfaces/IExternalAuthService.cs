using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

/// <summary>
/// Handles what happens AFTER Google has verified a user: find the matching
/// account or create one, then return tokens so the user is logged in.
/// </summary>
public interface IExternalAuthService
{
    /// <summary>
    /// Given the claims Google returned, find the existing linked user or
    /// create a brand-new one, then issue an access + refresh token pair.
    /// </summary>
    /// <param name="claims">The facts Google sent about the user.</param>
    /// <returns>An AuthResponse containing tokens and a safe user view.</returns>
    Task<AuthResponse> FindOrCreateUserAsync(GoogleClaims claims);
}