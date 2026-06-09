using Buyit.Application.Common;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Buyit.Infrastructure.Services
{
    public class JwtTokenService : IJwtTokenService
    {
        private readonly JwtSettings _settings;

        public JwtTokenService(IOptions<JwtSettings> options)
        {
            _settings = options.Value;
        }

        public string GenerateAccessToken(User user)
        {
            // 1. Turn the secret text into raw bytes, then wrap it as a security key
            var keyBytes = Encoding.UTF8.GetBytes(_settings.Secret);
            var securityKey = new SymmetricSecurityKey(keyBytes);

            // 2. Bundle the key with the algorithm we'll sign with (HMAC-SHA256)
            var signingCredentials = new SigningCredentials(
                securityKey, SecurityAlgorithms.HmacSha256);
            // 3. Build the claims — the facts about the user to embed in the token
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", user.Role.ToString())
            };
            // 4. Assemble the token: claims + issuer/audience + expiry + signature
            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes),
                signingCredentials: signingCredentials);
            // 5. Serialize the token object into the compact "header.payload.signature" string
            var tokenHandler = new JwtSecurityTokenHandler();
            return tokenHandler.WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            // 32 random bytes from a cryptographically secure source = an unguessable token
            var randomBytes = RandomNumberGenerator.GetBytes(32);
            // Encode the raw bytes as text so it can be stored in a string column / sent as JSON
            return Convert.ToBase64String(randomBytes);
        }
    }
}
