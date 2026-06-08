using Buyit.Domain.Entities;

namespace Buyit.Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}

